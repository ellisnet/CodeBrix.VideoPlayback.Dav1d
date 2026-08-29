namespace CodeBrix.VideoPlayback.Dav1d.Interop;

/// <summary>
/// The native sizes and field offsets this binding's structures are pinned to.
/// </summary>
/// <remarks>
/// <para>
/// Every value here was read out of a C program compiled against the exact headers vendored in
/// <c>dav1d-native-tools/dav1d/include/dav1d</c> - dav1d 1.5.4, API 7.0.0 - with <c>sizeof</c> and
/// <c>offsetof</c>. They are the same on every platform this package ships a native for: all seven use the
/// LP64 or LLVM Windows 64-bit model in which <c>int</c> and <c>enum</c> are four bytes and a pointer is
/// eight, and none of the declarations here contain a <c>long</c>.
/// </para>
/// <para>
/// The point of restating them as constants is that the test suite can compare them against what the
/// managed declarations actually produce, so a mistyped offset fails a test rather than corrupting memory.
/// If dav1d is ever re-vendored at a different version, re-run the probe described in MAINTAINER-README.txt
/// and update these values and the declarations together.
/// </para>
/// </remarks>
internal static class Dav1dNativeLayout
{
    /// <summary>The API major version this binding is written against.</summary>
    public const int ApiVersionMajor = 7;

    /// <summary>The upstream version string the vendored source produces.</summary>
    public const string ExpectedVersion = "1.5.4";

    /// <summary>The alignment and tail padding dav1d requires of every picture plane, in bytes.</summary>
    public const int PictureAlignment = 64;

    /// <summary>The multiple dav1d requires both picture dimensions to be rounded up to, in samples.</summary>
    public const int PictureDimensionMultiple = 128;

    /// <summary>The size of <c>Dav1dSettings</c>, in bytes.</summary>
    public const int SettingsSize = 96;

    /// <summary>The size of <c>Dav1dPicAllocator</c>, in bytes.</summary>
    public const int PicAllocatorSize = 24;

    /// <summary>The size of <c>Dav1dLogger</c>, in bytes.</summary>
    public const int LoggerSize = 16;

    /// <summary>The size of <c>Dav1dPicture</c>, in bytes.</summary>
    public const int PictureSize = 272;

    /// <summary>The size of <c>Dav1dPictureParameters</c>, in bytes.</summary>
    public const int PictureParametersSize = 16;

    /// <summary>The size of <c>Dav1dData</c>, in bytes.</summary>
    public const int DataSize = 72;

    /// <summary>The size of <c>Dav1dDataProps</c>, in bytes.</summary>
    public const int DataPropsSize = 48;

    /// <summary>The size of <c>Dav1dUserData</c>, in bytes.</summary>
    public const int UserDataSize = 16;

    /// <summary>The size of <c>Dav1dSequenceHeader</c>, in bytes.</summary>
    public const int SequenceHeaderSize = 808;

    /// <summary>The size of <c>Dav1dFrameHeader</c>, in bytes.</summary>
    public const int FrameHeaderSize = 1152;

    /// <summary>The size of <c>Dav1dContentLightLevel</c>, in bytes.</summary>
    public const int ContentLightLevelSize = 4;

    /// <summary>The size of <c>Dav1dMasteringDisplay</c>, in bytes.</summary>
    public const int MasteringDisplaySize = 24;

    /// <summary>The offset of <c>Dav1dSettings.allocator</c>.</summary>
    public const int SettingsAllocatorOffset = 24;

    /// <summary>The offset of <c>Dav1dSettings.logger</c>.</summary>
    public const int SettingsLoggerOffset = 48;

    /// <summary>The offset of <c>Dav1dSettings.reserved</c>.</summary>
    public const int SettingsReservedOffset = 80;

    /// <summary>The offset of <c>Dav1dPicture.data</c>, the first of the three plane pointers.</summary>
    public const int PictureDataOffset = 16;

    /// <summary>The offset of <c>Dav1dPicture.stride</c>, the first of the two stride values.</summary>
    public const int PictureStrideOffset = 40;

    /// <summary>The offset of <c>Dav1dPicture.p</c>.</summary>
    public const int PictureParametersOffset = 56;

    /// <summary>The offset of <c>Dav1dPicture.m</c>.</summary>
    public const int PicturePropertiesOffset = 72;

    /// <summary>The offset of <c>Dav1dPicture.ref</c>.</summary>
    public const int PictureReferenceOffset = 256;

    /// <summary>The offset of <c>Dav1dPicture.allocator_data</c>.</summary>
    public const int PictureAllocatorDataOffset = 264;

    /// <summary>The offset of <c>Dav1dData.m</c>.</summary>
    public const int DataPropertiesOffset = 24;

    /// <summary>The offset of <c>Dav1dSequenceHeader.max_width</c>.</summary>
    public const int SequenceHeaderMaxWidthOffset = 4;

    /// <summary>The offset of <c>Dav1dSequenceHeader.layout</c>.</summary>
    public const int SequenceHeaderLayoutOffset = 12;

    /// <summary>The offset of <c>Dav1dSequenceHeader.hbd</c>.</summary>
    public const int SequenceHeaderHighBitDepthOffset = 32;

    /// <summary>The offset of <c>Dav1dSequenceHeader.color_range</c>.</summary>
    public const int SequenceHeaderColorRangeOffset = 33;

    /// <summary>The offset of <c>Dav1dSequenceHeader.ss_hor</c>.</summary>
    public const int SequenceHeaderSubsamplingHorizontalOffset = 416;

    /// <summary>The offset of <c>Dav1dSequenceHeader.film_grain_present</c>.</summary>
    public const int SequenceHeaderFilmGrainPresentOffset = 421;

    /// <summary>The offset of <c>Dav1dFrameHeader.frame_type</c>.</summary>
    public const int FrameHeaderFrameTypeOffset = 232;

    /// <summary>The offset of <c>Dav1dFrameHeader.width</c>, the first of its two values.</summary>
    public const int FrameHeaderWidthOffset = 236;

    /// <summary>The offset of <c>Dav1dFrameHeader.height</c>.</summary>
    public const int FrameHeaderHeightOffset = 244;

    /// <summary>The offset of <c>Dav1dFrameHeader.show_frame</c>.</summary>
    public const int FrameHeaderShowFrameOffset = 264;

    /// <summary>The offset of <c>Dav1dFrameHeader.render_width</c>.</summary>
    public const int FrameHeaderRenderWidthOffset = 408;

    /// <summary>The offset of <c>Dav1dFrameHeader.render_height</c>.</summary>
    public const int FrameHeaderRenderHeightOffset = 412;
}
