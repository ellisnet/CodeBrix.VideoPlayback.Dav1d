namespace CodeBrix.VideoPlayback.Dav1d.Interop;

/// <summary>Which frames dav1d should decode and return - <c>enum Dav1dDecodeFrameType</c> in <c>dav1d.h</c>.</summary>
internal enum Dav1dDecodeFrameType
{
    /// <summary>Decode and return every frame - dav1d's default.</summary>
    All = 0,

    /// <summary>Decode and return only frames other frames refer to.</summary>
    Reference = 1,

    /// <summary>Decode and return only intra frames, key frames included.</summary>
    Intra = 2,

    /// <summary>Decode and return only key frames.</summary>
    Key = 3,
}
