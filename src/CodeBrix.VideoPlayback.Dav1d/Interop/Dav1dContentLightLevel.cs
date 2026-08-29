using System.Runtime.InteropServices;

namespace CodeBrix.VideoPlayback.Dav1d.Interop;

/// <summary>
/// High-dynamic-range content light level metadata - <c>Dav1dContentLightLevel</c> in <c>headers.h</c>, 4 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct Dav1dContentLightLevel
{
    /// <summary>The maximum content light level, in candelas per square metre.</summary>
    public ushort MaxContentLightLevel;

    /// <summary>The maximum frame-average light level, in candelas per square metre.</summary>
    public ushort MaxFrameAverageLightLevel;
}
