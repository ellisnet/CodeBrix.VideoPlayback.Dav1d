using System;
using System.Runtime.InteropServices;

namespace CodeBrix.VideoPlayback.Dav1d.Interop;

/// <summary>
/// Turns dav1d's negative POSIX return values into names, and answers the one question the decode loop asks
/// of them: is this "try again", or is it a failure?
/// </summary>
/// <remarks>
/// <para>
/// dav1d returns <c>DAV1D_ERR(e)</c>, which is the negation of a C <c>errno.h</c> constant taken from the
/// platform it was COMPILED for. Most of those constants happen to agree across platforms, but
/// <c>EAGAIN</c> - the one this binding cares about most, because it is the whole back-pressure
/// protocol - does not: it is 11 on Linux and on the Windows C runtime, and 35 on macOS. Getting that wrong
/// would turn ordinary back-pressure into an exception on one platform and nothing else, so the table below
/// is per-platform and the test suite checks the value for the platform it is running on.
/// </para>
/// <para>
/// Only the seven codes dav1d 1.5.4 actually returns are listed. Anything else is reported by its number.
/// </para>
/// </remarks>
internal static class Dav1dErrorCodes
{
    /// <summary>dav1d's "try again": the caller must drain or supply data before repeating the call.</summary>
    public static readonly int TryAgain = -ErrnoAgain;

    private const int ErrnoPermission = 1;
    private const int ErrnoNoEntry = 2;
    private const int ErrnoIo = 5;
    private const int ErrnoNoMemory = 12;
    private const int ErrnoInvalid = 22;
    private const int ErrnoRange = 34;

    private static int ErrnoAgain => RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? 35 : 11;

    private static int ErrnoNoProtocolOption
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return 42;
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? 123 : 92;
        }
    }

    /// <summary>True when the value is dav1d's "try again" answer rather than a failure.</summary>
    /// <param name="result">A dav1d return value.</param>
    /// <returns>True for <c>DAV1D_ERR(EAGAIN)</c>.</returns>
    public static bool IsTryAgain(int result) => result == TryAgain;

    /// <summary>Names a dav1d return value.</summary>
    /// <param name="result">A dav1d return value, normally negative.</param>
    /// <returns>
    /// The C <c>errno</c> name - "EAGAIN", "ENOMEM" and so on - or a description of the raw number when the
    /// value is not one dav1d is documented to return.
    /// </returns>
    public static string Describe(int result)
    {
        if (result >= 0) return $"success ({result})";

        int errno = -result;
        if (errno == ErrnoAgain) return "EAGAIN";
        if (errno == ErrnoNoProtocolOption) return "ENOPROTOOPT";

        return errno switch
        {
            ErrnoPermission => "EPERM",
            ErrnoNoEntry => "ENOENT",
            ErrnoIo => "EIO",
            ErrnoNoMemory => "ENOMEM",
            ErrnoInvalid => "EINVAL",
            ErrnoRange => "ERANGE",
            _ => $"error {result}",
        };
    }

    /// <summary>Explains, in a sentence, what a dav1d return value means for a player.</summary>
    /// <param name="result">A dav1d return value.</param>
    /// <returns>A short explanation, or an empty string when there is nothing useful to add.</returns>
    public static string Explain(int result)
    {
        if (result >= 0) return string.Empty;

        int errno = -result;
        if (errno == ErrnoAgain) return "the decoder needs frames drained or more data before it can go on";
        if (errno == ErrnoNoProtocolOption) return "the bitstream uses something this decoder build does not support";

        return errno switch
        {
            ErrnoNoEntry => "no sequence header was found in the data",
            ErrnoIo => "the bitstream is malformed",
            ErrnoNoMemory => "a frame buffer could not be allocated",
            ErrnoInvalid => "the arguments or the bitstream are invalid",
            ErrnoRange => "the frame is larger than the configured frame-size limit",
            _ => string.Empty,
        };
    }

    /// <summary>dav1d's out-of-memory answer for the platform this is running on.</summary>
    public static int OutOfMemory => -ErrnoNoMemory;

    /// <summary>dav1d's invalid-argument answer for the platform this is running on.</summary>
    public static int Invalid => -ErrnoInvalid;

    /// <summary>Reports whether this platform's <c>EAGAIN</c> is the macOS value.</summary>
    /// <remarks>Exists so a test can state which branch of the table it checked.</remarks>
    public static bool UsesMacErrnoTable => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
}
