using System.Runtime.InteropServices;

namespace CodeBrix.VideoPlayback.Dav1d.Interop;

/// <summary>
/// The shape of a picture - <c>Dav1dPictureParameters</c> in <c>picture.h</c>, 16 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct Dav1dPictureParameters
{
    /// <summary>Width in pixels.</summary>
    public int Width;

    /// <summary>Height in pixels.</summary>
    public int Height;

    /// <summary>The plane layout.</summary>
    public Dav1dPixelLayout Layout;

    /// <summary>Bits per component: 8, 10 or 12.</summary>
    public int BitsPerComponent;
}
