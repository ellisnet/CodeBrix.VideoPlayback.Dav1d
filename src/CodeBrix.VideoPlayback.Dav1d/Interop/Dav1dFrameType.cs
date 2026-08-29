namespace CodeBrix.VideoPlayback.Dav1d.Interop;

/// <summary>The frame types dav1d reports - <c>enum Dav1dFrameType</c> in <c>headers.h</c>.</summary>
internal enum Dav1dFrameType
{
    /// <summary>A key intra frame: decoding may start here.</summary>
    Key = 0,

    /// <summary>An inter frame.</summary>
    Inter = 1,

    /// <summary>A non-key intra frame.</summary>
    Intra = 2,

    /// <summary>A switch inter frame.</summary>
    Switch = 3,
}
