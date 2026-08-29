using System;

namespace CodeBrix.VideoPlayback.Dav1d.Interop;

/// <summary>The event flags dav1d raises during decoding - <c>enum Dav1dEventFlags</c> in <c>dav1d.h</c>.</summary>
[Flags]
internal enum Dav1dEventFlags
{
    /// <summary>Nothing happened.</summary>
    None = 0,

    /// <summary>The last picture returned refers to a new sequence header.</summary>
    NewSequence = 1 << 0,

    /// <summary>The last picture returned refers to a sequence header with new operating parameters.</summary>
    NewOperatingParametersInfo = 1 << 1,
}
