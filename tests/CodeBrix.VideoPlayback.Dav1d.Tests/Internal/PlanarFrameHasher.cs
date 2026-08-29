using System;
using System.Security.Cryptography;
using CodeBrix.VideoPlayback.Decoding;
using CodeBrix.VideoPlayback.Frames;

namespace CodeBrix.VideoPlayback.Dav1d.Tests.Internal;

/// <summary>
/// Hashes decoded frames exactly the way dav1d's own md5 muxer does, so the numbers in
/// <c>dav1d-native-tools/test-vectors/EXPECTED.md5</c> are the numbers these tests compare against.
/// </summary>
/// <remarks>
/// <para>
/// The rule, taken from <c>tools/output/md5.c</c>: walk the planes in order - Y, then U, then V, and skip
/// the chroma planes entirely for monochrome content - and for each plane hash its VISIBLE rows, one row at
/// a time, taking only the visible samples from each. Stride padding is never hashed. High bit depth is
/// hashed as little-endian 16-bit words, which on every platform this package supports is simply the bytes
/// as they lie in memory.
/// </para>
/// <para>
/// The chroma dimensions are rounded UP - <c>(w + ss_hor) &gt;&gt; ss_hor</c> - so an odd-sized frame hashes
/// the half-covered edge sample rather than dropping it. Vector 06 is 322 by 182 precisely to make that
/// arithmetic matter.
/// </para>
/// <para>
/// One hash covers a whole stream: every frame is fed to the same incremental hash, in output order.
/// </para>
/// </remarks>
internal sealed class PlanarFrameHasher : IDisposable
{
    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

    /// <summary>How many frames have been hashed.</summary>
    public int FrameCount { get; private set; }

    /// <summary>Adds one frame to the hash.</summary>
    /// <param name="frame">The frame to hash.</param>
    /// <exception cref="ArgumentNullException"><paramref name="frame" /> is null.</exception>
    public void Add(VideoFrame frame)
    {
        if (frame == null) throw new ArgumentNullException(nameof(frame));

        int bytesPerSample = frame.BitDepth > 8 ? 2 : 1;
        AddPlane(frame.Y, frame.Width, frame.Height, bytesPerSample);

        if (frame.Layout != VideoPixelLayout.Gray)
        {
            int shiftX = frame.ChromaShiftX;
            int shiftY = frame.ChromaShiftY;
            int chromaWidth = (frame.Width + shiftX) >> shiftX;
            int chromaHeight = (frame.Height + shiftY) >> shiftY;

            AddPlane(frame.U, chromaWidth, chromaHeight, bytesPerSample);
            AddPlane(frame.V, chromaWidth, chromaHeight, bytesPerSample);
        }

        FrameCount++;
    }

    /// <summary>Finishes the hash and returns it in dav1d's lower-case hexadecimal form.</summary>
    /// <returns>The 32-character hash.</returns>
    public string Finish() => Convert.ToHexStringLower(hash.GetHashAndReset());

    /// <inheritdoc />
    public void Dispose() => hash.Dispose();

    private unsafe void AddPlane(VideoFramePlane plane, int width, int height, int bytesPerSample)
    {
        if (plane.Data == IntPtr.Zero) return;

        int rowBytes = width * bytesPerSample;
        byte* start = (byte*)plane.Data;

        for (int row = 0; row < height; row++)
        {
            hash.AppendData(new ReadOnlySpan<byte>(start + ((long)row * plane.Stride), rowBytes));
        }
    }
}
