using System;
using CodeBrix.VideoPlayback.Dav1d.Interop;
using CodeBrix.VideoPlayback.Decoding;

namespace CodeBrix.VideoPlayback.Dav1d;

/// <summary>
/// The one call an application makes to teach CodeBrix.VideoPlayback how to decode AV1.
/// </summary>
/// <remarks>
/// <para>
/// CodeBrix.VideoPlayback ships no video decoder at all, because a decoder brings a licence and a set of
/// native binaries with it and not every application wants them. This package brings AV1, and it announces
/// itself the same way the Opus audio package does - explicitly, at start-up:
/// </para>
/// <code>
/// CodeBrixVideoPlaybackDav1d.Register();
/// </code>
/// <para>
/// There is deliberately no module initializer. An initializer would run whenever the assembly was touched,
/// which would keep the whole decoder - and every native library beside it - alive through a trimmed
/// publish even in an application that never plays a video. An explicit call is one line, and it is a line
/// the trimmer and the reader can both see.
/// </para>
/// <para>
/// Registration is idempotent: a start-up path that runs twice registers once, because the registry
/// de-duplicates on the factory instance and this class holds exactly one.
/// </para>
/// <para>
/// Registering a decoder does not give you a picture on screen. An application also needs something to
/// PRESENT frames with - the CodeBrix.VideoPlayback.Skia companion for a SkiaSharp application, or the
/// CodeBrix.Platform video player element - and, if its files carry Opus audio, the Opus audio package.
/// </para>
/// </remarks>
public static class CodeBrixVideoPlaybackDav1d
{
    private static readonly object Gate = new object();
    private static readonly Dav1dDecoderFactory Instance = new Dav1dDecoderFactory();

    private static bool registered;

    /// <summary>The single decoder factory this package registers.</summary>
    public static Dav1dDecoderFactory Factory => Instance;

    /// <summary>True once <see cref="Register()" /> has added the factory to the process-wide registry.</summary>
    public static bool IsRegistered
    {
        get
        {
            lock (Gate) return registered;
        }
    }

    /// <summary>
    /// Makes AV1 decoding available to every playback session in this process.
    /// </summary>
    /// <remarks>
    /// Safe to call more than once and from any thread. The native library is loaded and its API version
    /// checked here, so a missing or mismatched native fails at start-up with a message that says which
    /// paths were looked at - rather than at the moment a video is opened.
    /// </remarks>
    /// <exception cref="Dav1dException">
    /// The native dav1d library could not be loaded, or reports an API version this package was not built
    /// against.
    /// </exception>
    public static void Register()
    {
        lock (Gate)
        {
            if (registered) return;

            Dav1dLibrary.EnsureLoaded();
            VideoDecoders.Register(Instance);
            registered = true;
        }
    }

    /// <summary>
    /// Makes AV1 decoding available to ONE playback session, without touching the process-wide registry.
    /// </summary>
    /// <param name="session">The session to register with.</param>
    /// <remarks>
    /// Useful when an application plays several things at once with different needs, and in tests, where
    /// leaving a process-wide registration behind would reach into the next test.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="session" /> is null.</exception>
    /// <exception cref="Dav1dException">
    /// The native dav1d library could not be loaded, or reports an API version this package was not built
    /// against.
    /// </exception>
    public static void Register(VideoPlaybackSession session)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));

        Dav1dLibrary.EnsureLoaded();
        session.RegisterDecoderFactory(Instance);
    }

    /// <summary>
    /// Removes the factory from the process-wide registry.
    /// </summary>
    /// <returns>True when it was registered and has now been removed.</returns>
    /// <remarks>An application has no reason to call this; it is here so a test can undo itself.</remarks>
    public static bool Unregister()
    {
        lock (Gate)
        {
            bool removed = VideoDecoders.Unregister(Instance);
            registered = false;
            return removed;
        }
    }

    /// <summary>
    /// The version of the native dav1d library that was loaded, for example "1.5.4".
    /// </summary>
    /// <remarks>Loads the native library if it is not loaded yet.</remarks>
    /// <exception cref="Dav1dException">The native library could not be loaded.</exception>
    public static string NativeVersion => Dav1dLibrary.Version;

    /// <summary>
    /// The full path of the native dav1d library that was loaded.
    /// </summary>
    /// <remarks>
    /// Reads "libdav1d.so (operating-system search path)", or the platform's equivalent, when the library
    /// was found by the platform loader rather than in the package's own runtimes folder. Null before
    /// anything has been loaded.
    /// </remarks>
    public static string NativeLibraryPath => Dav1dLibrary.LoadedPath;

    /// <summary>
    /// The native library's API version as "major.minor.patch".
    /// </summary>
    /// <remarks>Loads the native library if it is not loaded yet.</remarks>
    /// <exception cref="Dav1dException">The native library could not be loaded.</exception>
    public static string NativeApiVersion =>
        $"{Dav1dLibrary.ApiVersionMajor}.{Dav1dLibrary.ApiVersionMinor}.{Dav1dLibrary.ApiVersionPatch}";
}
