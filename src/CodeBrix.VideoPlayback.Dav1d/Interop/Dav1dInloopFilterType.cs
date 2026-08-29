using System;

namespace CodeBrix.VideoPlayback.Dav1d.Interop;

/// <summary>The in-loop filters dav1d can be asked to run - <c>enum Dav1dInloopFilterType</c> in <c>dav1d.h</c>.</summary>
[Flags]
internal enum Dav1dInloopFilterType
{
    /// <summary>No in-loop filtering at all.</summary>
    None = 0,

    /// <summary>The deblocking filter.</summary>
    Deblock = 1 << 0,

    /// <summary>Constrained directional enhancement.</summary>
    Cdef = 1 << 1,

    /// <summary>Loop restoration.</summary>
    Restoration = 1 << 2,

    /// <summary>Every filter - dav1d's default.</summary>
    All = Deblock | Cdef | Restoration,
}
