using System;
using CodeBrix.VideoPlayback;

namespace CodeBrix.VideoPlayback.Dav1d;

/// <summary>
/// Thrown when the dav1d decoder refuses something: a bitstream it cannot decode, a frame larger than the
/// configured limit, a native library that is missing or is the wrong version.
/// </summary>
/// <remarks>
/// <para>
/// It derives from <see cref="VideoPlaybackException" />, so an application that already catches playback
/// failures catches these too and does not have to know which decoder package is installed.
/// </para>
/// <para>
/// Ordinary back-pressure is NOT an exception. When dav1d says "try again" - it wants frames drained before
/// it will take more data - the binding reports that as
/// <see cref="CodeBrix.VideoPlayback.Decoding.IVideoDecoder.SendPacket" /> returning false, which is the
/// contract's own way of saying the same thing. Every OTHER negative answer from the library becomes one of
/// these, carrying the C <c>errno</c> name dav1d returned.
/// </para>
/// </remarks>
public class Dav1dException : VideoPlaybackException
{
    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public Dav1dException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an underlying cause.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The underlying cause.</param>
    public Dav1dException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception describing a failed dav1d call.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="errorName">The C <c>errno</c> name dav1d returned, such as "EIO".</param>
    /// <param name="errorCode">The raw negative value dav1d returned.</param>
    public Dav1dException(string message, string errorName, int errorCode)
        : base(message)
    {
        ErrorName = errorName;
        ErrorCode = errorCode;
    }

    /// <summary>
    /// The C <c>errno</c> name dav1d returned - "EIO", "ENOMEM", "ERANGE", "ENOPROTOOPT" and so on - or null
    /// when this exception did not come from a dav1d return value.
    /// </summary>
    public string ErrorName { get; }

    /// <summary>The raw value dav1d returned, which is a negated <c>errno</c>, or zero when there was none.</summary>
    public int ErrorCode { get; }
}
