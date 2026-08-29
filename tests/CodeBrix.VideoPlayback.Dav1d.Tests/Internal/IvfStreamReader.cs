using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace CodeBrix.VideoPlayback.Dav1d.Tests.Internal;

/// <summary>
/// Reads the IVF container the conformance vectors are stored in.
/// </summary>
/// <remarks>
/// IVF is about as simple as a container gets - a 32-byte header and then a length and a timestamp in front
/// of every frame - which is why the dav1d project uses it for its own test data. This reader exists so the
/// tests can feed the vectors to the binding without borrowing the container code from
/// CodeBrix.VideoPlayback: a decoder test that depended on the reader it is meant to be independent of
/// would prove less than it looks.
/// </remarks>
internal static class IvfStreamReader
{
    /// <summary>One frame out of an IVF file.</summary>
    /// <param name="Data">The compressed bytes.</param>
    /// <param name="Timestamp">The frame's timestamp, in the file's own time base units.</param>
    internal readonly record struct IvfFrame(byte[] Data, ulong Timestamp);

    /// <summary>Everything an IVF file says about itself, plus its frames.</summary>
    /// <param name="FourCharacterCode">The codec identifier, "AV01" for these vectors.</param>
    /// <param name="Width">The width the header states.</param>
    /// <param name="Height">The height the header states.</param>
    /// <param name="TimeBaseNumerator">The time base numerator - the frame rate's numerator, in practice.</param>
    /// <param name="TimeBaseDenominator">The time base denominator.</param>
    /// <param name="DeclaredFrameCount">The frame count the header states, which is not always the truth.</param>
    /// <param name="Frames">The frames actually present.</param>
    internal sealed record IvfStream(
        string FourCharacterCode,
        int Width,
        int Height,
        uint TimeBaseNumerator,
        uint TimeBaseDenominator,
        uint DeclaredFrameCount,
        IReadOnlyList<IvfFrame> Frames);

    /// <summary>Reads a whole IVF file.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The stream.</returns>
    /// <exception cref="InvalidDataException">The file is not IVF, or ends in the middle of a frame.</exception>
    public static IvfStream Read(string path) => Parse(File.ReadAllBytes(path));

    /// <summary>Reads an IVF file that has already been loaded.</summary>
    /// <param name="bytes">The file's contents.</param>
    /// <returns>The stream.</returns>
    /// <exception cref="InvalidDataException">The data is not IVF, or ends in the middle of a frame.</exception>
    public static IvfStream Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 32) throw new InvalidDataException("An IVF file is at least 32 bytes long.");
        if (bytes[0] != (byte)'D' || bytes[1] != (byte)'K' || bytes[2] != (byte)'I' || bytes[3] != (byte)'F')
        {
            throw new InvalidDataException("The file does not start with the IVF signature 'DKIF'.");
        }

        int headerLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]);
        if (headerLength < 32 || headerLength > bytes.Length)
        {
            throw new InvalidDataException($"The IVF header states a length of {headerLength} bytes.");
        }

        string fourCharacterCode = System.Text.Encoding.ASCII.GetString(bytes[8..12]);
        int width = BinaryPrimitives.ReadUInt16LittleEndian(bytes[12..]);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(bytes[14..]);
        uint numerator = BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]);
        uint denominator = BinaryPrimitives.ReadUInt32LittleEndian(bytes[20..]);
        uint declaredFrames = BinaryPrimitives.ReadUInt32LittleEndian(bytes[24..]);

        List<IvfFrame> frames = new List<IvfFrame>();
        int offset = headerLength;

        while (offset + 12 <= bytes.Length)
        {
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
            ulong timestamp = BinaryPrimitives.ReadUInt64LittleEndian(bytes[(offset + 4)..]);
            offset += 12;

            if (size > int.MaxValue || offset + (int)size > bytes.Length)
            {
                throw new InvalidDataException(
                    $"The IVF frame at offset {offset - 12} states {size} bytes, but only "
                    + $"{bytes.Length - offset} remain.");
            }

            frames.Add(new IvfFrame(bytes.Slice(offset, (int)size).ToArray(), timestamp));
            offset += (int)size;
        }

        return new IvfStream(fourCharacterCode, width, height, numerator, denominator, declaredFrames, frames);
    }
}
