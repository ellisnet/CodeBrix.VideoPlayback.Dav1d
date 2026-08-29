using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using CodeBrix.VideoPlayback.Dav1d.Interop;
using CodeBrix.VideoPlayback.Decoding;
using CodeBrix.VideoPlayback.Frames;

namespace CodeBrix.VideoPlayback.Dav1d.Decoding;

/// <summary>
/// Decodes AV1 with dav1d, straight into the playback session's own frame-buffer pool.
/// </summary>
/// <remarks>
/// <para>
/// Create one through <see cref="Dav1dDecoderFactory" /> rather than directly; a session does that for you
/// once <see cref="CodeBrixVideoPlaybackDav1d.Register()" /> has been called.
/// </para>
/// <para>
/// The loop is the one <see cref="IVideoDecoder" /> documents - send a packet, pull frames until there are
/// none, send the next - with one wrinkle worth knowing about, because it is dav1d's protocol showing
/// through: <see cref="SendPacket" /> returns FALSE when the decoder is holding as much data as it can. That
/// is not an error and nothing has been lost; pull frames with <see cref="TryReceiveFrame" /> and offer THE
/// SAME packet again. The packet is only consumed when the call returns true.
/// </para>
/// <para>One thread at a time, like every decoder. The frames it produces may be read from any thread.</para>
/// </remarks>
public sealed unsafe class Dav1dVideoDecoder : IVideoDecoder
{
    private readonly object gate = new object();
    private readonly Stack<Dav1dPictureLease> leases = new Stack<Dav1dPictureLease>();
    private readonly Dav1dFrameAllocator allocator;
    private readonly Dav1dInputBufferPool inputBuffers = new Dav1dInputBufferPool();
    private readonly Action<string> logger;

    private GCHandle self;
    private IntPtr context;
    private Dav1dData* pending;
    private bool hasPending;
    private ReadOnlyMemory<byte> pendingPacketData;
    private bool draining;
    private bool drainFinished;
    private bool disposed;
    private long frameNumber;
    private readonly long frameSizeLimit;
    private string lastLogMessage;
    private VideoStreamInfo info = VideoStreamInfo.Unknown;

    private Dav1dContentLightLevel cachedContentLight;
    private Dav1dMasteringDisplay cachedMasteringDisplay;
    private bool cachedHadContentLight;
    private bool cachedHadMasteringDisplay;
    private HdrMetadata cachedHdr;

    /// <summary>Opens a decoder.</summary>
    /// <param name="codecId">The codec identifier the decoder was created for.</param>
    /// <param name="codecPrivate">
    /// The container's initialisation data - an <c>av1C</c> configuration record, or the bare configuration
    /// OBUs, or nothing at all.
    /// </param>
    /// <param name="options">The settings, including the frame-buffer pool to decode into.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options" /> is null.</exception>
    /// <exception cref="Dav1dException">
    /// The native library is missing or is the wrong version, the settings are outside dav1d's limits, or the
    /// decoder could not be opened.
    /// </exception>
    public Dav1dVideoDecoder(string codecId, ReadOnlyMemory<byte> codecPrivate, VideoDecoderOptions options)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));

        Dav1dLibrary.EnsureLoaded();

        if (options.BufferPool == null)
        {
            throw new Dav1dException(
                "This decoder writes decoded frames into a frame-buffer pool supplied by its host, and "
                + $"{nameof(VideoDecoderOptions)}.{nameof(VideoDecoderOptions.BufferPool)} was null. A "
                + "playback session sets it for you; code driving a decoder directly must set it itself.");
        }

        CodecId = codecId;
        frameSizeLimit = options.FrameSizeLimit;
        Dav1dDecoderOptions dav1dOptions = options as Dav1dDecoderOptions;
        logger = dav1dOptions?.Logger;

        self = GCHandle.Alloc(this);
        allocator = new Dav1dFrameAllocator(options.BufferPool);
        pending = (Dav1dData*)NativeMemory.AllocZeroed((nuint)sizeof(Dav1dData));

        Dav1dSettings settings = default;
        Dav1dNative.DefaultSettings(&settings);

        settings.ThreadCount = Validate(options.Threads, 256, nameof(VideoDecoderOptions.Threads), "threads");
        settings.MaxFrameDelay = Validate(
            options.MaxFrameDelay,
            256,
            nameof(VideoDecoderOptions.MaxFrameDelay),
            "frames of delay");
        settings.ApplyGrain = options.ApplyFilmGrain ? 1 : 0;
        settings.FrameSizeLimit = options.FrameSizeLimit >= uint.MaxValue
            ? uint.MaxValue
            : (uint)options.FrameSizeLimit;

        if (dav1dOptions != null)
        {
            settings.OperatingPoint = dav1dOptions.OperatingPoint;
            settings.AllLayers = dav1dOptions.AllLayers ? 1 : 0;
            settings.StrictStdCompliance = dav1dOptions.StrictStdCompliance ? 1 : 0;
            settings.OutputInvisibleFrames = dav1dOptions.OutputInvisibleFrames ? 1 : 0;
        }

        settings.Allocator.Cookie = allocator.Cookie;
        settings.Allocator.AllocPictureCallback = Dav1dFrameAllocator.AllocateCallback;
        settings.Allocator.ReleasePictureCallback = Dav1dFrameAllocator.ReleaseCallback;

        settings.Logger.Cookie = GCHandle.ToIntPtr(self);
        settings.Logger.Callback = &WriteLogMessage;

        int reportedDelay = Dav1dNative.GetFrameDelay(&settings);
        FrameDelay = reportedDelay > 0 ? reportedDelay : 1;

        IntPtr opened = IntPtr.Zero;
        int result = Dav1dNative.Open(&opened, &settings);
        if (result < 0)
        {
            Cleanup();
            throw Failure("dav1d_open", result);
        }

        context = opened;
        ThreadCount = settings.ThreadCount;

        if (Dav1dDecoderFactory.TryProbe(codecPrivate.Span, out VideoStreamInfo probed)) info = probed;
    }

    /// <inheritdoc />
    public VideoStreamInfo Info => info;

    /// <inheritdoc />
    /// <remarks>
    /// Always true. dav1d writes decoded samples into the host pool's memory, so nothing is copied between
    /// this decoder's output and a presenter's upload.
    /// </remarks>
    public bool SupportsExternalBuffers => true;

    /// <inheritdoc />
    public string CodecId { get; }

    /// <summary>The number of threads the decoder was opened with; 0 means dav1d counted the logical cores.</summary>
    public int ThreadCount { get; }

    /// <summary>
    /// How many frames the decoder buffers internally, as dav1d reports for the settings it was opened with.
    /// </summary>
    /// <remarks>
    /// Always at least 1. It is how many times <see cref="SendPacket" /> can be expected to be called before
    /// the first frame appears, which is worth knowing when a short clip has to show its first picture fast.
    /// </remarks>
    public int FrameDelay { get; }

    /// <summary>The most recent diagnostic message the native library produced, or null.</summary>
    public string LastLogMessage => Volatile.Read(ref lastLogMessage);

    /// <summary>
    /// Reads and CLEARS the decoder's event flags - whether the last picture began a new coded sequence, or
    /// carried new operating parameters for the one already running.
    /// </summary>
    /// <returns>The flags raised since this was last called.</returns>
    /// <remarks>
    /// Internal because nothing a player does depends on it: the stream description on
    /// <see cref="Info" /> already changes when a new sequence header changes the picture's shape or colour,
    /// which is the part that matters. It is here so the test suite can show the binding reads them.
    /// </remarks>
    internal Dav1dEventFlags TakeEventFlags()
    {
        ThrowIfDisposed();

        Dav1dEventFlags flags = Dav1dEventFlags.None;
        int result = Dav1dNative.GetEventFlags(context, &flags);
        return result < 0 ? Dav1dEventFlags.None : flags;
    }

    /// <summary>The picture allocator this decoder installed in dav1d.</summary>
    /// <remarks>Exposed so the test suite can watch the allocation and release counts, and the threads
    /// releases arrive on, without having to infer them from the pool.</remarks>
    internal Dav1dFrameAllocator Allocator => allocator;

    /// <inheritdoc />
    /// <exception cref="Dav1dException">The bitstream could not be decoded.</exception>
    /// <exception cref="ObjectDisposedException">The decoder has been disposed.</exception>
    public bool SendPacket(VideoPacket packet)
    {
        ThrowIfDisposed();

        if (hasPending)
        {
            // The contract says a caller who is told "full" offers the SAME packet again, and that is what
            // the session does. A caller who offers a different one instead has changed its mind rather than
            // made a mistake, so once the held packet has gone in, the new one is taken as well - dropping
            // it would lose a frame silently, which is the one outcome nobody could debug.
            bool sameAsHeld = pendingPacketData.Equals(packet.Data);
            if (!TrySendPending()) return false;
            if (sameAsHeld) return true;
        }

        if (packet.IsEmpty) return true;

        ReadOnlySpan<byte> data = packet.Data.Span;
        Dav1dInputBuffer buffer = inputBuffers.Rent(data);

        int wrapped = Dav1dNative.DataWrap(
            pending,
            buffer.Address,
            (UIntPtr)(uint)data.Length,
            Dav1dInputBufferPool.FreeCallback,
            GCHandle.ToIntPtr(buffer.Handle));

        if (wrapped < 0)
        {
            inputBuffers.ReturnUnused(buffer);
            throw Failure("dav1d_data_wrap", wrapped);
        }

        pending->Properties.Timestamp = packet.Timestamp.Ticks;
        pending->Properties.Duration = packet.Duration.Ticks;
        pendingPacketData = packet.Data;
        hasPending = true;
        return TrySendPending();
    }

    /// <inheritdoc />
    /// <exception cref="Dav1dException">The bitstream could not be decoded.</exception>
    /// <exception cref="ObjectDisposedException">The decoder has been disposed.</exception>
    public bool TryReceiveFrame(out VideoFrame frame)
    {
        frame = null;
        ThrowIfDisposed();

        Dav1dPictureLease lease = TakeLease();
        int result = Dav1dNative.GetPicture(context, lease.Picture);

        // dav1d only enters its own drain path on the SECOND consecutive call: the first one after a
        // send just resets the flag. So at the end of the stream, one "nothing yet" can be followed by
        // more frames, and answering false on it would truncate the video by a frame or two.
        if (Dav1dErrorCodes.IsTryAgain(result) && draining && !drainFinished)
        {
            result = Dav1dNative.GetPicture(context, lease.Picture);
            if (Dav1dErrorCodes.IsTryAgain(result)) drainFinished = true;
        }

        if (Dav1dErrorCodes.IsTryAgain(result))
        {
            RecycleUnusedLease(lease);
            return false;
        }

        if (result < 0)
        {
            RecycleUnusedLease(lease);
            throw Failure("dav1d_get_picture", result);
        }

        frame = BuildFrame(lease);
        return true;
    }

    /// <inheritdoc />
    /// <exception cref="ObjectDisposedException">The decoder has been disposed.</exception>
    public void Flush()
    {
        ThrowIfDisposed();

        DropPending();
        Dav1dNative.Flush(context);
        draining = false;
        drainFinished = false;
    }

    /// <inheritdoc />
    /// <exception cref="ObjectDisposedException">The decoder has been disposed.</exception>
    public void Drain()
    {
        ThrowIfDisposed();
        draining = true;
        drainFinished = false;
    }

    /// <summary>
    /// Takes the next finished frame and applies the film grain the stream asked for, producing a frame over
    /// a second buffer from the same pool.
    /// </summary>
    /// <param name="frame">The grained frame, carrying one reference the caller owns; null when none was ready.</param>
    /// <returns>True when a frame was produced.</returns>
    /// <remarks>
    /// Only meaningful on a decoder opened with <see cref="VideoDecoderOptions.ApplyFilmGrain" /> set to
    /// false, because a decoder that is already synthesising grain would then do it twice. It exists so the
    /// test suite can prove that grain synthesis really runs and produces the picture the conformance hashes
    /// expect, rather than being quietly skipped - which is a mistake a checksum of the ungrained picture
    /// would never catch.
    /// </remarks>
    internal bool TryReceiveGrainedFrame(out VideoFrame frame)
    {
        frame = null;
        ThrowIfDisposed();

        Dav1dPictureLease source = TakeLease();
        int result = Dav1dNative.GetPicture(context, source.Picture);

        if (Dav1dErrorCodes.IsTryAgain(result) && draining && !drainFinished)
        {
            result = Dav1dNative.GetPicture(context, source.Picture);
            if (Dav1dErrorCodes.IsTryAgain(result)) drainFinished = true;
        }

        if (Dav1dErrorCodes.IsTryAgain(result))
        {
            RecycleUnusedLease(source);
            return false;
        }

        if (result < 0)
        {
            RecycleUnusedLease(source);
            throw Failure("dav1d_get_picture", result);
        }

        Dav1dPictureLease grained = TakeLease();
        try
        {
            int applied = Dav1dNative.ApplyGrain(context, grained.Picture, source.Picture);
            if (applied < 0)
            {
                RecycleUnusedLease(grained);
                throw Failure("dav1d_apply_grain", applied);
            }
        }
        finally
        {
            source.ReleasePictureOnly();
            RecycleUnusedLease(source);
        }

        frame = BuildFrame(grained);
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
        }

        // Drain first, then close: dav1d holds pictures it has finished with until they are collected, and
        // collecting them here is what lets their buffers go back to the pool in an orderly way. Buffers
        // still referenced by frames the application is holding stay valid; their leases release them.
        if (context != IntPtr.Zero)
        {
            Dav1dPictureLease lease = new Dav1dPictureLease(this, allocator.Pool);
            try
            {
                for (int guard = 0; guard < 1024; guard++)
                {
                    lease.Reset();
                    int result = Dav1dNative.GetPicture(context, lease.Picture);
                    if (result < 0) break;
                    lease.ReleasePictureOnly();
                }
            }
            catch (Exception)
            {
                // A decoder being disposed after a failure has nothing left to report.
            }
            finally
            {
                lease.FreeNative();
            }
        }

        DropPending();

        if (context != IntPtr.Zero)
        {
            IntPtr toClose = context;
            context = IntPtr.Zero;
            Dav1dNative.Close(&toClose);
        }

        Cleanup();
    }

    /// <summary>Takes a released lease back for reuse, or frees it when the decoder has gone.</summary>
    /// <param name="lease">The lease whose frame has just been released.</param>
    internal void RecycleLease(Dav1dPictureLease lease)
    {
        lock (gate)
        {
            if (!disposed && leases.Count < 64)
            {
                lease.Reset();
                leases.Push(lease);
                return;
            }
        }

        lease.FreeNative();
    }

    private Dav1dPictureLease TakeLease()
    {
        lock (gate)
        {
            if (leases.Count > 0)
            {
                Dav1dPictureLease reused = leases.Pop();
                reused.Reset();
                return reused;
            }
        }

        return new Dav1dPictureLease(this, allocator.Pool);
    }

    private void RecycleUnusedLease(Dav1dPictureLease lease)
    {
        lease.Reset();

        lock (gate)
        {
            if (!disposed && leases.Count < 64)
            {
                leases.Push(lease);
                return;
            }
        }

        lease.FreeNative();
    }

    private bool TrySendPending()
    {
        int result = Dav1dNative.SendData(context, pending);

        if (Dav1dErrorCodes.IsTryAgain(result)) return false;

        if (result < 0)
        {
            DropPending();
            throw Failure("dav1d_send_data", result);
        }

        hasPending = false;
        pendingPacketData = default;
        draining = false;
        drainFinished = false;
        return true;
    }

    private void DropPending()
    {
        if (!hasPending) return;
        Dav1dNative.DataUnref(pending);
        hasPending = false;
        pendingPacketData = default;
    }

    private VideoFrame BuildFrame(Dav1dPictureLease lease)
    {
        Dav1dPicture* picture = lease.Picture;

        VideoFrameBuffer buffer = null;
        if (picture->AllocatorData != IntPtr.Zero)
        {
            buffer = GCHandle.FromIntPtr(picture->AllocatorData).Target as VideoFrameBuffer;
        }

        if (buffer == null)
        {
            lease.ReleasePictureOnly();
            RecycleUnusedLease(lease);
            throw new Dav1dException(
                "dav1d returned a picture that was not allocated through this package's frame-buffer "
                + "allocator, so there is no pool buffer behind it. That should be impossible and means the "
                + "native library and this binding disagree about the layout of a Dav1dPicture.");
        }

        int width = picture->Parameters.Width;
        int height = picture->Parameters.Height;
        int bitDepth = picture->Parameters.BitsPerComponent;
        VideoPixelLayout layout = Dav1dFrameAllocator.MapLayout(picture->Parameters.Layout);

        int displayWidth = width;
        int displayHeight = height;
        bool isKeyFrame = false;

        if (picture->FrameHeader != null)
        {
            Dav1dFrameHeader* frameHeader = picture->FrameHeader;
            if (frameHeader->RenderWidth > 0) displayWidth = frameHeader->RenderWidth;
            if (frameHeader->RenderHeight > 0) displayHeight = frameHeader->RenderHeight;
            isKeyFrame = frameHeader->IsKeyFrame;
        }

        VideoColorInfo color = VideoColorInfo.Unspecified;
        if (picture->SequenceHeader != null) color = ReadColor(picture->SequenceHeader);

        long timestamp = picture->Properties.Timestamp;
        if (timestamp == long.MinValue) timestamp = 0;

        VideoFrameInfo frameInfo = new VideoFrameInfo(
            width,
            height,
            displayWidth,
            displayHeight,
            layout,
            bitDepth,
            new TimeSpan(timestamp),
            timestamp,
            frameNumber++,
            isKeyFrame,
            color,
            ReadHdr(picture));

        UpdateStreamInfo(frameInfo);

        // The lease is the pool the frame returns itself to: that is what maps the managed reference count
        // onto dav1d's. The buffer's Tag is deliberately left alone - it belongs to whichever presenter
        // takes this frame next, for its upload fence.
        return VideoFrame.Create(buffer, frameInfo, lease);
    }

    private void UpdateStreamInfo(in VideoFrameInfo frameInfo)
    {
        VideoStreamInfo current = info;
        if (current.IsKnown
            && current.Width == frameInfo.Width
            && current.Height == frameInfo.Height
            && current.BitDepth == frameInfo.BitDepth
            && current.Layout == frameInfo.Layout
            && current.Color == frameInfo.Color
            && ReferenceEquals(current.Hdr, frameInfo.Hdr))
        {
            return;
        }

        info = new VideoStreamInfo(
            frameInfo.Width,
            frameInfo.Height,
            frameInfo.DisplayWidth,
            frameInfo.DisplayHeight,
            frameInfo.Layout,
            frameInfo.BitDepth,
            frameInfo.Color)
        {
            Hdr = frameInfo.Hdr,
        };
    }

    /// <summary>
    /// Builds the high-dynamic-range description a picture carries, reusing the last one when nothing has
    /// changed.
    /// </summary>
    /// <param name="picture">The decoded picture.</param>
    /// <returns>The metadata, or null when the stream carries none.</returns>
    /// <remarks>
    /// The comparison is on the CONTENT of dav1d's two metadata structures, not on the addresses they live
    /// at. Those addresses are reference-counted allocations that dav1d frees and re-allocates as sequence
    /// headers come and go, so an address can be reused for different values - and a cache keyed on the
    /// address would then hand out the previous stream's mastering display. Both structures are a few bytes,
    /// so comparing them costs less than the allocation it saves, and a stream with no metadata - which is
    /// almost all of them - never gets that far.
    /// </remarks>
    private HdrMetadata ReadHdr(Dav1dPicture* picture)
    {
        bool hasContentLight = picture->ContentLight != null;
        bool hasMasteringDisplay = picture->MasteringDisplay != null;

        if (!hasContentLight && !hasMasteringDisplay) return null;

        if (cachedHdr != null
            && hasContentLight == cachedHadContentLight
            && hasMasteringDisplay == cachedHadMasteringDisplay
            && (!hasContentLight || Same(picture->ContentLight, cachedContentLight))
            && (!hasMasteringDisplay || Same(picture->MasteringDisplay, cachedMasteringDisplay)))
        {
            return cachedHdr;
        }

        HdrMetadata metadata = new HdrMetadata();

        if (hasMasteringDisplay)
        {
            Dav1dMasteringDisplay* display = picture->MasteringDisplay;
            const double Chromaticity = 1.0 / (1 << 16);
            metadata.RedPrimaryX = display->Primaries[0] * Chromaticity;
            metadata.RedPrimaryY = display->Primaries[1] * Chromaticity;
            metadata.GreenPrimaryX = display->Primaries[2] * Chromaticity;
            metadata.GreenPrimaryY = display->Primaries[3] * Chromaticity;
            metadata.BluePrimaryX = display->Primaries[4] * Chromaticity;
            metadata.BluePrimaryY = display->Primaries[5] * Chromaticity;
            metadata.WhitePointX = display->WhitePoint[0] * Chromaticity;
            metadata.WhitePointY = display->WhitePoint[1] * Chromaticity;
            metadata.MaxLuminance = display->MaxLuminance / 256.0;
            metadata.MinLuminance = display->MinLuminance / 16384.0;
            cachedMasteringDisplay = *display;
        }

        if (hasContentLight)
        {
            metadata.MaxContentLightLevel = picture->ContentLight->MaxContentLightLevel;
            metadata.MaxFrameAverageLightLevel = picture->ContentLight->MaxFrameAverageLightLevel;
            cachedContentLight = *picture->ContentLight;
        }

        cachedHadContentLight = hasContentLight;
        cachedHadMasteringDisplay = hasMasteringDisplay;
        cachedHdr = metadata;
        return metadata;
    }

    private static bool Same(Dav1dContentLightLevel* left, in Dav1dContentLightLevel right) =>
        left->MaxContentLightLevel == right.MaxContentLightLevel
        && left->MaxFrameAverageLightLevel == right.MaxFrameAverageLightLevel;

    private static bool Same(Dav1dMasteringDisplay* left, in Dav1dMasteringDisplay right)
    {
        if (left->MaxLuminance != right.MaxLuminance || left->MinLuminance != right.MinLuminance) return false;

        fixed (Dav1dMasteringDisplay* pinned = &right)
        {
            for (int index = 0; index < 6; index++)
            {
                if (left->Primaries[index] != pinned->Primaries[index]) return false;
            }

            return left->WhitePoint[0] == pinned->WhitePoint[0] && left->WhitePoint[1] == pinned->WhitePoint[1];
        }
    }

    /// <summary>Reads the colour description out of a dav1d sequence header.</summary>
    /// <param name="header">The sequence header.</param>
    /// <returns>The colour description.</returns>
    /// <remarks>
    /// dav1d records the primaries, transfer characteristic, matrix coefficients and chroma sample position
    /// as the numbers the AV1 specification gives them, and so does this library, so those four are a
    /// straight conversion. The range is not: AV1 states a single "full range" flag, while the library
    /// distinguishes "studio", "full" and "the stream did not say".
    /// </remarks>
    internal static VideoColorInfo ReadColor(Dav1dSequenceHeader* header) =>
        new VideoColorInfo(
            (VideoColorPrimaries)header->ColorPrimaries,
            (VideoTransferCharacteristics)header->TransferCharacteristics,
            (VideoMatrixCoefficients)header->MatrixCoefficients,
            header->ColorRange != 0 ? VideoColorRange.Full : VideoColorRange.Limited,
            (VideoChromaSiting)header->ChromaSamplePosition);

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void WriteLogMessage(IntPtr cookie, byte* format, IntPtr arguments)
    {
        try
        {
            if (GCHandle.FromIntPtr(cookie).Target is not Dav1dVideoDecoder decoder) return;
            if (format == null) return;

            string message = Marshal.PtrToStringUTF8((IntPtr)format);
            if (string.IsNullOrEmpty(message)) return;

            message = message.TrimEnd('\n', '\r');
            Volatile.Write(ref decoder.lastLogMessage, message);
            decoder.logger?.Invoke(message);
        }
        catch (Exception)
        {
            // dav1d logs from its frame threads; an exception escaping here would end the process.
        }
    }

    private static int Validate(int value, int maximum, string propertyName, string unit)
    {
        if (value >= 0 && value <= maximum) return value;

        throw new Dav1dException(
            $"{propertyName} was set to {value}. dav1d accepts 0 to {maximum} {unit}, where 0 means "
            + "\"decide for me\".");
    }

    private Dav1dException Failure(string call, int result)
    {
        string name = Dav1dErrorCodes.Describe(result);
        string explanation = Dav1dErrorCodes.Explain(result);
        string log = LastLogMessage;

        string message = $"{call} failed with {name}";
        if (!string.IsNullOrEmpty(explanation)) message += $": {explanation}";
        message += ".";

        // dav1d states its frame-size refusal only through the log, and its log messages arrive with their
        // printf conversions unexpanded (see Dav1dDecoderOptions.Logger), so the number that actually
        // matters would otherwise be missing from the one message a caller has to act on.
        if (string.Equals(name, "ERANGE", StringComparison.Ordinal))
        {
            message += $" The configured limit is {frameSizeLimit} luma samples "
                + $"({nameof(VideoDecoderOptions)}.{nameof(VideoDecoderOptions.FrameSizeLimit)}); raise it to "
                + "play a larger frame, or leave it where it is to refuse one.";
        }

        string packet = DescribeFailingPacket();
        if (!string.IsNullOrEmpty(packet)) message += $" It was reading {packet}.";

        if (!string.IsNullOrEmpty(log)) message += $" The decoder said: \"{log}\".";

        return new Dav1dException(message, name, result);
    }

    /// <summary>
    /// Asks dav1d which input packet the last decoding error belonged to, and describes it.
    /// </summary>
    /// <returns>
    /// A phrase naming the packet's timestamp and stream offset, or null when dav1d has nothing to say.
    /// </returns>
    /// <remarks>
    /// dav1d carries the metadata of an input packet through to the picture decoded from it, and keeps the
    /// metadata of the packet that caused the last failure. In a long file "decoding failed" is not much to
    /// go on; "decoding failed at 00:04:12" points at the frame to look at.
    /// </remarks>
    private string DescribeFailingPacket()
    {
        if (context == IntPtr.Zero) return null;

        Dav1dDataProps properties = default;
        if (Dav1dNative.GetDecodeErrorDataProps(context, &properties) < 0) return null;

        long timestamp = properties.Timestamp;
        long offset = properties.Offset;
        Dav1dNative.DataPropsUnref(&properties);

        if (timestamp == long.MinValue && offset < 0) return null;
        if (timestamp == long.MinValue) return $"the packet at stream offset {offset}";

        string description = $"the packet at {new TimeSpan(timestamp)}";
        return offset >= 0 ? $"{description} (stream offset {offset})" : description;
    }

    private void ThrowIfDisposed()
    {
        if (disposed) throw new ObjectDisposedException(nameof(Dav1dVideoDecoder));
    }

    private void Cleanup()
    {
        while (true)
        {
            Dav1dPictureLease lease;
            lock (gate)
            {
                if (leases.Count == 0) break;
                lease = leases.Pop();
            }

            lease.FreeNative();
        }

        if (pending != null)
        {
            NativeMemory.Free(pending);
            pending = null;
        }

        inputBuffers.Dispose();
        allocator?.ReleaseDecoderReference();
        if (self.IsAllocated) self.Free();
    }
}
