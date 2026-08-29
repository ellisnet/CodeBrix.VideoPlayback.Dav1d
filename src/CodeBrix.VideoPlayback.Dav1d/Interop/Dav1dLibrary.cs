using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace CodeBrix.VideoPlayback.Dav1d.Interop;

/// <summary>
/// Finds and loads the native dav1d library, and checks that the one it found speaks the API this binding
/// was written against.
/// </summary>
/// <remarks>
/// <para>
/// The package ships seven natives in the standard NuGet <c>runtimes/&lt;rid&gt;/native/</c> layout. When an
/// application publishes for one runtime identifier, the build system copies the right one beside the
/// application and the operating system finds it without help. When an application publishes without a
/// runtime identifier - which is the ordinary case for a library test run, and common for desktop
/// applications - the natives stay in their <c>runtimes/&lt;rid&gt;/native/</c> folders and nothing looks
/// there. The resolver installed here does.
/// </para>
/// <para>
/// If nothing can be loaded, the exception lists every path that was tried. A missing native is nearly
/// always a packaging or publishing question - the wrong runtime identifier, a trimmed output folder, a
/// single-file bundle that did not extract - and the list of paths is what answers it.
/// </para>
/// </remarks>
internal static class Dav1dLibrary
{
    /// <summary>The name the <c>LibraryImport</c> declarations ask for.</summary>
    public const string LibraryName = "dav1d";

    private static readonly object Gate = new object();

    private static IntPtr handle;
    private static string loadedPath;
    private static bool resolverInstalled;
    private static string versionString;
    private static int apiVersion;

    /// <summary>The full path of the native library that was loaded, once one has been.</summary>
    /// <remarks>
    /// Reads "dav1d (operating-system search path)" when the library was found by the platform loader rather
    /// than beside the assembly, because in that case there is no path this code ever saw.
    /// </remarks>
    public static string LoadedPath
    {
        get
        {
            lock (Gate) return loadedPath;
        }
    }

    /// <summary>The version string the loaded library reports, for example "1.5.4".</summary>
    public static string Version
    {
        get
        {
            EnsureLoaded();
            lock (Gate) return versionString;
        }
    }

    /// <summary>The API version the loaded library reports, as dav1d's packed <c>0x00XXYYZZ</c> value.</summary>
    public static int ApiVersion
    {
        get
        {
            EnsureLoaded();
            lock (Gate) return apiVersion;
        }
    }

    /// <summary>The major part of <see cref="ApiVersion" />.</summary>
    public static int ApiVersionMajor => (ApiVersion >> 16) & 0xFF;

    /// <summary>The minor part of <see cref="ApiVersion" />.</summary>
    public static int ApiVersionMinor => (ApiVersion >> 8) & 0xFF;

    /// <summary>The patch part of <see cref="ApiVersion" />.</summary>
    public static int ApiVersionPatch => ApiVersion & 0xFF;

    /// <summary>True once the native library has been loaded and its API version accepted.</summary>
    public static bool IsLoaded
    {
        get
        {
            lock (Gate) return handle != IntPtr.Zero && versionString != null;
        }
    }

    /// <summary>The runtime identifier folder this platform's native library lives in.</summary>
    /// <exception cref="Dav1dException">This package ships no native for the current platform.</exception>
    public static string RuntimeIdentifier
    {
        get
        {
            string os = OperatingSystemMoniker();
            string architecture = ArchitectureMoniker();

            if (os == null || architecture == null)
            {
                throw new Dav1dException(
                    "CodeBrix.VideoPlayback.Dav1d ships native dav1d libraries for Windows (x64, ARM64), macOS "
                    + "(x64, ARM64) and Linux (x64, ARM64, RISC-V 64); this process is running on "
                    + $"{RuntimeInformation.OSDescription} / {RuntimeInformation.ProcessArchitecture}, which is "
                    + "none of them.");
            }

            return os + "-" + architecture;
        }
    }

    /// <summary>The file name the native library has on this platform.</summary>
    public static string NativeFileName
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "dav1d.dll";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "libdav1d.dylib";
            return "libdav1d.so";
        }
    }

    /// <summary>
    /// Loads the native library if it is not loaded already, and checks its API version.
    /// </summary>
    /// <exception cref="Dav1dException">
    /// The library could not be found - the message lists every path that was tried - or it reports an API
    /// version this binding was not written against.
    /// </exception>
    public static void EnsureLoaded()
    {
        lock (Gate)
        {
            if (handle != IntPtr.Zero && versionString != null) return;

            InstallResolverNoLock();

            if (handle == IntPtr.Zero)
            {
                IntPtr loaded = LoadNoLock();
                if (loaded == IntPtr.Zero)
                {
                    throw new Dav1dException(DescribeLoadFailure(BaseDirectories(), RuntimeIdentifier, NativeFileName));
                }
            }
        }

        // Outside the lock: these call into the native library, which loads through the resolver above.
        string reportedVersion = ReadVersionString();
        int reportedApi = Dav1dNative.GetApiVersion();

        lock (Gate)
        {
            versionString = reportedVersion;
            apiVersion = reportedApi;
        }

        int major = (reportedApi >> 16) & 0xFF;
        if (major == Dav1dNativeLayout.ApiVersionMajor) return;

        string path = LoadedPath;
        lock (Gate)
        {
            versionString = null;
        }

        throw new Dav1dException(
            $"The native dav1d library at '{path}' reports API version {major}."
            + $"{(reportedApi >> 8) & 0xFF}.{reportedApi & 0xFF} (library version "
            + $"'{reportedVersion}'), but this package's binding is written against API version "
            + $"{Dav1dNativeLayout.ApiVersionMajor} and its structure layouts would be wrong against any other "
            + "major version. Replace the native library with one built from the dav1d source vendored in this "
            + "package's repository, or use a version of this package built for the library you have.");
    }

    /// <summary>Builds the "nothing could be loaded" message, listing every path that would be tried.</summary>
    /// <param name="baseDirectories">The directories to probe, in order.</param>
    /// <param name="runtimeIdentifier">The runtime identifier folder to look under.</param>
    /// <param name="fileName">The native file name to look for.</param>
    /// <returns>The message.</returns>
    /// <remarks>Separated out so a test can read it without having to hide the real library first.</remarks>
    internal static string DescribeLoadFailure(
        IReadOnlyList<string> baseDirectories,
        string runtimeIdentifier,
        string fileName)
    {
        StringBuilder message = new StringBuilder();
        message.Append("The native dav1d library could not be loaded. ");
        message.Append($"CodeBrix.VideoPlayback.Dav1d looked for '{fileName}' at:");

        foreach (string candidate in EnumerateProbePaths(baseDirectories, runtimeIdentifier, fileName))
        {
            message.Append(Environment.NewLine);
            message.Append("    ");
            message.Append(candidate);
        }

        message.Append(Environment.NewLine);
        message.Append("    ");
        message.Append(fileName);
        message.Append("  (the operating system's own search path)");
        message.Append(Environment.NewLine);
        message.Append(
            "The package ships this library under runtimes/" + runtimeIdentifier + "/native/. If it is missing, "
            + "the application was probably published in a way that dropped it - a trimmed or single-file "
            + "publish, a manual copy of the managed assembly alone, or a runtime identifier the package has no "
            + "native for.");
        return message.ToString();
    }

    /// <summary>Lists, in order, every path the loader would try.</summary>
    /// <param name="baseDirectories">The directories to probe.</param>
    /// <param name="runtimeIdentifier">The runtime identifier folder to look under.</param>
    /// <param name="fileName">The native file name to look for.</param>
    /// <returns>The candidate paths.</returns>
    internal static IReadOnlyList<string> EnumerateProbePaths(
        IReadOnlyList<string> baseDirectories,
        string runtimeIdentifier,
        string fileName)
    {
        List<string> paths = new List<string>();

        foreach (string directory in baseDirectories)
        {
            if (string.IsNullOrEmpty(directory)) continue;
            Add(paths, Path.Combine(directory, fileName));
            Add(paths, Path.Combine(directory, "runtimes", runtimeIdentifier, "native", fileName));
        }

        return paths;
    }

    /// <summary>The directories the loader probes, most likely first.</summary>
    /// <returns>The directories, without duplicates.</returns>
    internal static IReadOnlyList<string> BaseDirectories()
    {
        List<string> directories = new List<string>();
        Add(directories, AppContext.BaseDirectory);

        string assemblyDirectory = null;
        try
        {
            string location = typeof(Dav1dLibrary).Assembly.Location;
            if (!string.IsNullOrEmpty(location)) assemblyDirectory = Path.GetDirectoryName(location);
        }
        catch (NotSupportedException)
        {
            // A single-file or in-memory assembly has no location; the base directory is all there is.
        }

        Add(directories, assemblyDirectory);
        return directories;
    }

    private static void InstallResolverNoLock()
    {
        if (resolverInstalled) return;
        NativeLibrary.SetDllImportResolver(typeof(Dav1dLibrary).Assembly, Resolve);
        resolverInstalled = true;
    }

    /// <summary>Installs the resolver, without loading anything.</summary>
    /// <remarks>
    /// Called from the static constructor of the type that carries the imports, so the resolver is in place
    /// before any of them can run - including when something calls an import without going through
    /// <see cref="EnsureLoaded" /> first.
    /// </remarks>
    public static void EnsureResolverInstalled()
    {
        lock (Gate) InstallResolverNoLock();
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal)) return IntPtr.Zero;

        lock (Gate)
        {
            if (handle != IntPtr.Zero) return handle;
            return LoadNoLock();
        }
    }

    private static IntPtr LoadNoLock()
    {
        string fileName = NativeFileName;
        string runtimeIdentifier;

        try
        {
            runtimeIdentifier = RuntimeIdentifier;
        }
        catch (Dav1dException)
        {
            runtimeIdentifier = null;
        }

        if (runtimeIdentifier != null)
        {
            foreach (string candidate in EnumerateProbePaths(BaseDirectories(), runtimeIdentifier, fileName))
            {
                if (!File.Exists(candidate)) continue;
                if (!NativeLibrary.TryLoad(candidate, out IntPtr found)) continue;

                handle = found;
                loadedPath = candidate;
                return found;
            }
        }

        if (NativeLibrary.TryLoad(LibraryName, typeof(Dav1dLibrary).Assembly, null, out IntPtr system))
        {
            handle = system;
            loadedPath = fileName + " (operating-system search path)";
            return system;
        }

        return IntPtr.Zero;
    }

    private static unsafe string ReadVersionString()
    {
        IntPtr text = Dav1dNative.GetVersion();
        return text == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(text);
    }

    private static string OperatingSystemMoniker()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "win";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "osx";
        return RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" : null;
    }

    private static string ArchitectureMoniker() =>
        RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.RiscV64 => "riscv64",
            _ => null,
        };

    private static void Add(List<string> target, string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        foreach (string existing in target)
        {
            if (string.Equals(existing, value, StringComparison.Ordinal)) return;
        }

        target.Add(value);
    }
}
