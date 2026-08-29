namespace CodeBrix.VideoPlayback.Dav1d.Interop;

/// <summary>The plane layout values dav1d uses - <c>enum Dav1dPixelLayout</c> in <c>headers.h</c>.</summary>
internal enum Dav1dPixelLayout
{
    /// <summary>Monochrome: luma only.</summary>
    I400 = 0,

    /// <summary>4:2:0 planar.</summary>
    I420 = 1,

    /// <summary>4:2:2 planar.</summary>
    I422 = 2,

    /// <summary>4:4:4 planar.</summary>
    I444 = 3,
}
