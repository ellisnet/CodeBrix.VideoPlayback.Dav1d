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
/// Installs the playback session's frame-buffer pool as dav1d's own picture allocator, so decoded samples
/// land straight in the memory the presenter will read - with no copy anywhere between the two.
/// </summary>
/// <remarks>
/// <para>
/// dav1d asks its allocator for a buffer whose plane pointers are 64-byte aligned, whose dimensions are
/// rounded up to a multiple of 128 samples, whose chroma planes share one stride, and which has 64 bytes of
/// slack after it for the vector code to over-read into. That is not a coincidence: the pool contract in
/// CodeBrix.VideoPlayback was written to be exactly this contract, so nothing is ever reformatted and the
/// answer to "can we use the host's memory" is always yes.
/// </para>
/// <para>
/// <b>Threading.</b> dav1d calls the allocation callback on the thread that calls
/// <c>dav1d_get_picture</c> - the decode thread - and the release callback on THAT thread or on any of its
/// frame threads, whichever drops the last reference to a picture. The release path here therefore does
/// nothing that is not thread-safe, and the pool it hands buffers back to promises the same.
/// </para>
/// <para>
/// <b>Lifetime.</b> dav1d copies the allocator into every picture it allocates, so a picture can outlive the
/// decoder that produced it and its release callback still has to work. The handle this class registers
/// itself under is therefore counted, not simply freed when the decoder closes: it goes away when the
/// decoder has closed AND the last picture it allocated has been released.
/// </para>
/// </remarks>
internal sealed unsafe class Dav1dFrameAllocator
{
    private readonly IVideoFrameBufferPool pool;
    private readonly object trackingGate = new object();
    private readonly HashSet<int> releaseThreadIds = new HashSet<int>();

    private GCHandle self;
    private int outstanding = 1;
    private long allocations;
    private long releases;

    /// <summary>Creates an allocator over a host pool.</summary>
    /// <param name="bufferPool">The pool decoded frames are written into.</param>
    /// <exception cref="ArgumentNullException"><paramref name="bufferPool" /> is null.</exception>
    public Dav1dFrameAllocator(IVideoFrameBufferPool bufferPool)
    {
        pool = bufferPool ?? throw new ArgumentNullException(nameof(bufferPool));
        self = GCHandle.Alloc(this);
    }

    /// <summary>The pool this allocator rents from.</summary>
    public IVideoFrameBufferPool Pool => pool;

    /// <summary>The pointer dav1d passes back to both callbacks.</summary>
    public IntPtr Cookie => GCHandle.ToIntPtr(self);

    /// <summary>How many pictures have been allocated.</summary>
    public long Allocations => Interlocked.Read(ref allocations);

    /// <summary>How many pictures have been released.</summary>
    public long Releases => Interlocked.Read(ref releases);

    /// <summary>
    /// True to record the managed thread identifier of every release, so a test can show that dav1d really
    /// does release pictures from its frame threads. Off by default: the decode path does no bookkeeping.
    /// </summary>
    public bool TrackReleaseThreads { get; set; }

    /// <summary>The distinct managed thread identifiers releases have arrived on, when tracking is on.</summary>
    /// <returns>The identifiers seen so far.</returns>
    public int[] GetReleaseThreadIds()
    {
        lock (trackingGate)
        {
            int[] ids = new int[releaseThreadIds.Count];
            releaseThreadIds.CopyTo(ids);
            return ids;
        }
    }

    /// <summary>The callback dav1d allocates pictures through.</summary>
    public static delegate* unmanaged[Cdecl]<Dav1dPicture*, IntPtr, int> AllocateCallback => &AllocatePicture;

    /// <summary>The callback dav1d releases pictures through.</summary>
    public static delegate* unmanaged[Cdecl]<Dav1dPicture*, IntPtr, void> ReleaseCallback => &ReleasePicture;

    /// <summary>
    /// Drops the decoder's own share of the allocator's lifetime. The handle survives until every picture
    /// this allocator produced has been released as well.
    /// </summary>
    public void ReleaseDecoderReference() => DropReference();

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int AllocatePicture(Dav1dPicture* picture, IntPtr cookie)
    {
        try
        {
            if (GCHandle.FromIntPtr(cookie).Target is not Dav1dFrameAllocator allocator)
            {
                return Dav1dErrorCodes.Invalid;
            }

            return allocator.Allocate(picture);
        }
        catch (Exception)
        {
            // Nothing may propagate into native code. dav1d treats a negative answer as an allocation
            // failure and unwinds the frame cleanly, which is the right outcome for any exception here.
            return Dav1dErrorCodes.OutOfMemory;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void ReleasePicture(Dav1dPicture* picture, IntPtr cookie)
    {
        try
        {
            IntPtr allocatorData = picture->AllocatorData;
            if (allocatorData == IntPtr.Zero) return;

            picture->AllocatorData = IntPtr.Zero;
            GCHandle bufferHandle = GCHandle.FromIntPtr(allocatorData);
            VideoFrameBuffer buffer = bufferHandle.Target as VideoFrameBuffer;
            bufferHandle.Free();

            if (GCHandle.FromIntPtr(cookie).Target is not Dav1dFrameAllocator allocator) return;
            allocator.Release(buffer);
        }
        catch (Exception)
        {
            // Same rule as above: a release callback that threw into dav1d's frame thread would take the
            // process with it. Losing a buffer back to the pool is bad; crashing is worse.
        }
    }

    private int Allocate(Dav1dPicture* picture)
    {
        VideoPixelLayout layout = MapLayout(picture->Parameters.Layout);
        if (layout == VideoPixelLayout.Unknown) return Dav1dErrorCodes.Invalid;

        int width = picture->Parameters.Width;
        int height = picture->Parameters.Height;
        int bitDepth = picture->Parameters.BitsPerComponent;

        if (width <= 0 || height <= 0) return Dav1dErrorCodes.Invalid;
        if (bitDepth != 8 && bitDepth != 10 && bitDepth != 12) return Dav1dErrorCodes.Invalid;

        VideoFrameBufferDescriptor descriptor = new VideoFrameBufferDescriptor(width, height, layout, bitDepth);
        VideoFrameBuffer buffer = pool.Rent(descriptor);
        if (buffer == null) return Dav1dErrorCodes.OutOfMemory;

        if (buffer.Y.Data == IntPtr.Zero)
        {
            pool.Return(buffer);
            return Dav1dErrorCodes.OutOfMemory;
        }

        picture->Data0 = buffer.Y.Data;
        picture->Data1 = buffer.U.Data;
        picture->Data2 = buffer.V.Data;
        picture->Stride0 = buffer.Y.Stride;
        picture->Stride1 = buffer.U.Stride;
        picture->AllocatorData = GCHandle.ToIntPtr(GCHandle.Alloc(buffer));

        Interlocked.Increment(ref outstanding);
        Interlocked.Increment(ref allocations);
        return 0;
    }

    private void Release(VideoFrameBuffer buffer)
    {
        if (TrackReleaseThreads)
        {
            lock (trackingGate) releaseThreadIds.Add(Environment.CurrentManagedThreadId);
        }

        Interlocked.Increment(ref releases);
        if (buffer != null) pool.Return(buffer);
        DropReference();
    }

    private void DropReference()
    {
        if (Interlocked.Decrement(ref outstanding) != 0) return;
        if (self.IsAllocated) self.Free();
    }

    /// <summary>Maps dav1d's plane layout onto the library's.</summary>
    /// <param name="layout">The dav1d layout.</param>
    /// <returns>The matching layout, or <see cref="VideoPixelLayout.Unknown" /> for a value dav1d should never produce.</returns>
    internal static VideoPixelLayout MapLayout(Dav1dPixelLayout layout) =>
        layout switch
        {
            Dav1dPixelLayout.I400 => VideoPixelLayout.Gray,
            Dav1dPixelLayout.I420 => VideoPixelLayout.I420,
            Dav1dPixelLayout.I422 => VideoPixelLayout.I422,
            Dav1dPixelLayout.I444 => VideoPixelLayout.I444,
            _ => VideoPixelLayout.Unknown,
        };
}
