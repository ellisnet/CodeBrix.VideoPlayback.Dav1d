using System;
using System.Collections.Generic;
using CodeBrix.VideoPlayback.Dav1d.Decoding;
using CodeBrix.VideoPlayback.Dav1d.Interop;
using CodeBrix.VideoPlayback.Decoding;

namespace CodeBrix.VideoPlayback.Dav1d;

/// <summary>
/// Builds AV1 decoders. This is the object that gets registered with CodeBrix.VideoPlayback.
/// </summary>
/// <remarks>
/// There is no reason to create one: <see cref="CodeBrixVideoPlaybackDav1d.Register()" /> holds the single
/// instance the library uses and registers that. The type is public because the registry's contract is
/// public, and because an application that manages its own decoder list may want to hand
/// <see cref="CodeBrixVideoPlaybackDav1d.Factory" /> to a session directly.
/// </remarks>
public sealed class Dav1dDecoderFactory : IVideoDecoderFactory
{
    private static readonly string[] Codecs = { VideoCodecIds.Av1 };

    /// <inheritdoc />
    /// <remarks>Always "CodeBrix.VideoPlayback.Dav1d".</remarks>
    public string FactoryId => "CodeBrix.VideoPlayback.Dav1d";

    /// <inheritdoc />
    /// <remarks>AV1 - the "av01" identifier - and nothing else.</remarks>
    public IReadOnlyCollection<string> SupportedCodecIds => Codecs;

    /// <inheritdoc />
    /// <remarks>
    /// Zero, the ordinary level. dav1d is the reference software AV1 decoder and there is nothing to defer
    /// to and nothing to override; a hardware-backed decoder added later would register above it.
    /// </remarks>
    public int Priority => 0;

    /// <inheritdoc />
    /// <exception cref="Dav1dException">
    /// The codec is AV1 but the decoder could not be opened - most often because the native library is
    /// missing, in which case the message lists everywhere it was looked for.
    /// </exception>
    public IVideoDecoder CreateDecoder(
        string codecId,
        ReadOnlyMemory<byte> codecPrivate,
        VideoDecoderOptions options)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (!string.Equals(codecId, VideoCodecIds.Av1, StringComparison.OrdinalIgnoreCase)) return null;

        return new Dav1dVideoDecoder(VideoCodecIds.Av1, codecPrivate, options);
    }

    /// <summary>
    /// Reads an AV1 sequence header out of a packet and describes the stream it belongs to, without
    /// decoding anything.
    /// </summary>
    /// <param name="packet">
    /// A packet from the stream, or the track's codec-private data. The first packet of a track works,
    /// because every AV1 key frame carries a sequence header, and so does an <c>av1C</c> record.
    /// </param>
    /// <param name="info">The stream's dimensions, layout, bit depth and colour; never null.</param>
    /// <returns>True when a sequence header was found and parsed.</returns>
    /// <remarks>
    /// This is what a host uses to size a surface, choose a texture format or decide whether it wants to
    /// play a file at all, before a single frame has been decoded.
    /// </remarks>
    public static bool TryProbe(VideoPacket packet, out VideoStreamInfo info) =>
        TryProbe(packet.Data.Span, out info);

    /// <summary>
    /// Reads an AV1 sequence header out of a block of bitstream data and describes the stream it belongs to.
    /// </summary>
    /// <param name="data">
    /// Open Bitstream Units, or an <c>av1C</c> configuration record - the four-byte record header is
    /// recognised and stepped over. Data carrying no sequence header is not an error; the method answers
    /// false.
    /// </param>
    /// <param name="info">The stream's dimensions, layout, bit depth and colour; never null.</param>
    /// <returns>True when a sequence header was found and parsed.</returns>
    /// <exception cref="Dav1dException">The native library could not be loaded.</exception>
    public static unsafe bool TryProbe(ReadOnlySpan<byte> data, out VideoStreamInfo info)
    {
        info = VideoStreamInfo.Unknown;
        if (data.IsEmpty) return false;

        Dav1dLibrary.EnsureLoaded();

        ReadOnlySpan<byte> obus = StripAv1ConfigurationRecord(data);
        if (obus.IsEmpty) return false;

        Dav1dSequenceHeader header = default;
        int result;

        fixed (byte* bytes = obus)
        {
            result = Dav1dNative.ParseSequenceHeader(&header, bytes, (UIntPtr)(uint)obus.Length);
        }

        if (result != 0) return false;

        VideoPixelLayout layout = Dav1dFrameAllocator.MapLayout(header.Layout);
        if (layout == VideoPixelLayout.Unknown) return false;

        info = new VideoStreamInfo(
            header.MaxWidth,
            header.MaxHeight,
            header.MaxWidth,
            header.MaxHeight,
            layout,
            header.BitDepth,
            Dav1dVideoDecoder.ReadColor(&header));

        return info.IsKnown;
    }

    /// <summary>
    /// Steps over an <c>av1C</c> configuration record header, leaving the configuration OBUs behind it.
    /// </summary>
    /// <param name="data">The codec-private data, or a raw packet.</param>
    /// <returns>The bytes to parse.</returns>
    /// <remarks>
    /// An <c>av1C</c> record - what Matroska and the bespoke container both store as codec-private data for
    /// an AV1 track - is four bytes of its own followed by the configuration OBUs. The first byte is a set
    /// marker bit and a version of 1, which is 0x81 and cannot begin a valid OBU header, so recognising it
    /// is unambiguous.
    /// </remarks>
    internal static ReadOnlySpan<byte> StripAv1ConfigurationRecord(ReadOnlySpan<byte> data)
    {
        if (data.Length <= 4) return data;
        return data[0] == 0x81 ? data[4..] : data;
    }
}
