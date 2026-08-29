using System.Runtime.InteropServices;

namespace CodeBrix.VideoPlayback.Dav1d.Interop;

/// <summary>
/// The parts of dav1d's <c>Dav1dSequenceHeader</c> this binding reads, at their exact native offsets.
/// </summary>
/// <remarks>
/// <para>
/// The native structure is 808 bytes and most of it - the 32 operating points, the timing information, the
/// per-tool flags - is of no interest here. Declaring only the fields that are read, at explicit offsets,
/// says plainly which bytes the binding depends on; declaring the whole thing would say the same and be
/// three hundred lines longer, with three hundred more chances of a mistake.
/// </para>
/// <para>
/// The <see cref="StructLayoutAttribute.Size" /> is the full native size, because
/// <c>dav1d_parse_sequence_header</c> writes a whole structure into memory the caller supplies. Every offset
/// and the size were taken from the vendored headers in <c>dav1d-native-tools/dav1d/include/dav1d</c> and
/// are restated as constants on <see cref="Dav1dNativeLayout" />, which the test suite checks this
/// declaration against.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = Dav1dNativeLayout.SequenceHeaderSize)]
internal struct Dav1dSequenceHeader
{
    /// <summary>The stream profile.</summary>
    [FieldOffset(0)] public byte Profile;

    /// <summary>The largest width the stream declares.</summary>
    [FieldOffset(4)] public int MaxWidth;

    /// <summary>The largest height the stream declares.</summary>
    [FieldOffset(8)] public int MaxHeight;

    /// <summary>The plane layout.</summary>
    [FieldOffset(12)] public Dav1dPixelLayout Layout;

    /// <summary>The colour primaries, as the AV1 specification numbers them.</summary>
    [FieldOffset(16)] public int ColorPrimaries;

    /// <summary>The transfer characteristic, as the AV1 specification numbers it.</summary>
    [FieldOffset(20)] public int TransferCharacteristics;

    /// <summary>The matrix coefficients, as the AV1 specification numbers them.</summary>
    [FieldOffset(24)] public int MatrixCoefficients;

    /// <summary>The chroma sample position, as the AV1 specification numbers it.</summary>
    [FieldOffset(28)] public int ChromaSamplePosition;

    /// <summary>0, 1 or 2 for 8, 10 or 12 bits per component.</summary>
    [FieldOffset(32)] public byte HighBitDepth;

    /// <summary>Non-zero when the samples use the full numeric range rather than the studio range.</summary>
    [FieldOffset(33)] public byte ColorRange;

    /// <summary>How many operating points the stream declares.</summary>
    [FieldOffset(34)] public byte OperatingPointCount;

    /// <summary>Non-zero when chroma is subsampled horizontally.</summary>
    [FieldOffset(416)] public byte SubsamplingHorizontal;

    /// <summary>Non-zero when chroma is subsampled vertically.</summary>
    [FieldOffset(417)] public byte SubsamplingVertical;

    /// <summary>Non-zero when the stream carries no chroma at all.</summary>
    [FieldOffset(418)] public byte Monochrome;

    /// <summary>Non-zero when the stream stated its colour description rather than leaving it unspecified.</summary>
    [FieldOffset(419)] public byte ColorDescriptionPresent;

    /// <summary>Non-zero when U and V carry separate delta-q values.</summary>
    [FieldOffset(420)] public byte SeparateUvDeltaQ;

    /// <summary>Non-zero when the stream carries film-grain parameters.</summary>
    [FieldOffset(421)] public byte FilmGrainPresent;

    /// <summary>Bits per component: 8, 10 or 12.</summary>
    public readonly int BitDepth => 8 + (HighBitDepth * 2);
}
