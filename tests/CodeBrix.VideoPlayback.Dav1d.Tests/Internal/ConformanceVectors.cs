using System;
using System.Collections.Generic;
using System.IO;

namespace CodeBrix.VideoPlayback.Dav1d.Tests.Internal;

/// <summary>
/// The six conformance streams and the hashes their decoded pictures must produce.
/// </summary>
/// <remarks>
/// Both the streams and <c>EXPECTED.md5</c> are read from the copies the build links out of
/// <c>dav1d-native-tools/test-vectors</c>, so the managed binding is checked against exactly the file the
/// native builds check themselves against. Nothing is downloaded, here or anywhere else in this repository.
/// </remarks>
internal static class ConformanceVectors
{
    /// <summary>One line of EXPECTED.md5: a stream, a grain setting, and the hash that combination gives.</summary>
    /// <param name="FileName">The stream's file name.</param>
    /// <param name="ApplyFilmGrain">True when the hash is for the picture WITH film grain synthesised.</param>
    /// <param name="Md5">The expected hash, lower-case hexadecimal.</param>
    internal readonly record struct Expectation(string FileName, bool ApplyFilmGrain, string Md5);

    /// <summary>The folder the linked streams are copied into beside the test assembly.</summary>
    public static string Directory => Path.Combine(AppContext.BaseDirectory, "test-vectors");

    /// <summary>The full path of one stream.</summary>
    /// <param name="fileName">The stream's file name.</param>
    /// <returns>The path.</returns>
    public static string PathOf(string fileName) => Path.Combine(Directory, fileName);

    /// <summary>Reads every expectation out of EXPECTED.md5.</summary>
    /// <returns>The expectations, in the order the file lists them.</returns>
    /// <exception cref="FileNotFoundException">EXPECTED.md5 was not copied beside the test assembly.</exception>
    /// <exception cref="InvalidDataException">A line of EXPECTED.md5 is not in the documented format.</exception>
    public static IReadOnlyList<Expectation> ReadExpectations()
    {
        string path = Path.Combine(Directory, "EXPECTED.md5");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "EXPECTED.md5 was not found beside the test assembly. It is linked in from "
                + "dav1d-native-tools/test-vectors by the test project.",
                path);
        }

        List<Expectation> expectations = new List<Expectation>();

        foreach (string rawLine in File.ReadAllLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            string[] fields = line.Split('|');
            if (fields.Length != 3)
            {
                throw new InvalidDataException(
                    $"EXPECTED.md5 line '{line}' does not have the three pipe-separated fields the file's own "
                    + "header documents.");
            }

            bool applyGrain = ParseGrainFlag(fields[1].Trim(), line);
            expectations.Add(new Expectation(fields[0].Trim(), applyGrain, fields[2].Trim()));
        }

        return expectations;
    }

    private static bool ParseGrainFlag(string flags, string line)
    {
        if (flags.Contains("--filmgrain 1", StringComparison.Ordinal)) return true;
        if (flags.Contains("--filmgrain 0", StringComparison.Ordinal)) return false;

        throw new InvalidDataException(
            $"EXPECTED.md5 line '{line}' does not state --filmgrain 0 or --filmgrain 1. The file's own header "
            + "requires every line to say which it means, so that the gate never depends on a command-line "
            + "default.");
    }
}
