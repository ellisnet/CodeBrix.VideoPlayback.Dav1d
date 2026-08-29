using System;
using CodeBrix.VideoPlayback.Decoding;

namespace CodeBrix.VideoPlayback.Dav1d;

/// <summary>
/// The dav1d-specific decoder settings, on top of the ones every video decoder understands.
/// </summary>
/// <remarks>
/// <para>
/// Set an instance of this type as
/// <see cref="CodeBrix.VideoPlayback.VideoPlaybackOptions.DecoderOptions" /> before opening a session, and
/// the dav1d decoder will use it. A session handed the plain
/// <see cref="VideoDecoderOptions" /> gets dav1d's own defaults for everything here.
/// </para>
/// <para>
/// Four settings a dav1d user might look for here are on the base type instead, because every video decoder
/// has them and a player should be able to set them without knowing which decoder it is talking to:
/// </para>
/// <list type="table">
///   <listheader><term>Base property</term><description>dav1d setting</description></listheader>
///   <item>
///     <term><see cref="VideoDecoderOptions.Threads" /></term>
///     <description><c>n_threads</c> - 0 lets dav1d count the logical cores.</description>
///   </item>
///   <item>
///     <term><see cref="VideoDecoderOptions.MaxFrameDelay" /></term>
///     <description>
///       <c>max_frame_delay</c> - 1 gives the first frame as soon as it is decoded, which is what a short
///       preloaded clip wants; 0 lets dav1d choose for throughput.
///     </description>
///   </item>
///   <item>
///     <term><see cref="VideoDecoderOptions.ApplyFilmGrain" /></term>
///     <description>
///       <c>apply_grain</c> - true by default. Grain synthesis is part of the picture the stream asks for, so
///       turning it off changes what you see (and what a checksum of the output comes to).
///     </description>
///   </item>
///   <item>
///     <term><see cref="VideoDecoderOptions.FrameSizeLimit" /></term>
///     <description>
///       <c>frame_size_limit</c> - the guard against a hostile file, 8192 by 8192 luma samples by default.
///     </description>
///   </item>
/// </list>
/// </remarks>
public sealed class Dav1dDecoderOptions : VideoDecoderOptions
{
    private int operatingPoint;

    /// <summary>
    /// Which operating point of a scalable stream to decode, from 0 to 31. Defaults to 0.
    /// </summary>
    /// <remarks>
    /// A scalable AV1 stream carries several qualities or resolutions in one bitstream; an operating point
    /// selects one of them. Ordinary single-layer content has exactly one, so this only matters for content
    /// that was deliberately authored as scalable.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside 0 to 31.</exception>
    public int OperatingPoint
    {
        get => operatingPoint;
        set
        {
            if (value < 0 || value > 31)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "An AV1 operating point is a number from 0 to 31.");
            }

            operatingPoint = value;
        }
    }

    /// <summary>
    /// True to output every spatial layer of a scalable stream rather than only the selected operating
    /// point's. Defaults to true, which is dav1d's own default.
    /// </summary>
    public bool AllLayers { get; set; } = true;

    /// <summary>
    /// True to refuse a stream over compliance violations that do not affect decoding - inconsistent or
    /// invalid metadata, for instance. Defaults to false.
    /// </summary>
    /// <remarks>
    /// Turning this on makes the decoder pickier than a player usually wants to be: a file that decodes
    /// perfectly well but states something contradictory in its metadata will fail instead of playing. It is
    /// there for a tool that is checking files rather than showing them.
    /// </remarks>
    public bool StrictStdCompliance { get; set; }

    /// <summary>
    /// True to emit frames the stream codes but does not show, in coding order, as well as the visible ones.
    /// Defaults to false.
    /// </summary>
    /// <remarks>
    /// This is an analysis setting, not a playback one. With it on, some pictures appear twice - once when
    /// they are coded and once when a later frame header shows them - and the timestamps do not tell a
    /// player what to do about that.
    /// </remarks>
    public bool OutputInvisibleFrames { get; set; }

    /// <summary>
    /// Called with each diagnostic message the native library produces, or null - the default - to discard
    /// them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The decoder always installs a logging hook whether or not this is set: with no hook, dav1d writes its
    /// own messages to standard error, which a library has no business doing on an application's behalf. When
    /// this is null the messages are captured and thrown away, except that the most recent one is added to a
    /// <see cref="Dav1dException" /> when decoding fails, because that is usually the sentence that explains
    /// the error code.
    /// </para>
    /// <para>
    /// dav1d's logging is a C <c>printf</c>-style pair - a format string and a variadic argument list - and
    /// expanding such an argument list from managed code is not portable across the architectures this
    /// package supports. The format string is therefore passed on AS IT IS: a message that carries values
    /// arrives with its <c>%d</c> and <c>%s</c> conversions still in it. The wording still identifies the
    /// problem, which is what a log line is for.
    /// </para>
    /// <para>The callback may run on any of dav1d's threads, and must not throw.</para>
    /// </remarks>
    public Action<string> Logger { get; set; }

    /// <inheritdoc />
    public override VideoDecoderOptions Clone() => (Dav1dDecoderOptions)MemberwiseClone();
}
