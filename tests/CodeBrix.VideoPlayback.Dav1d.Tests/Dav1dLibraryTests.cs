using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.VideoPlayback.Dav1d.Interop;
using SilverAssertions;
using Xunit;

namespace CodeBrix.VideoPlayback.Dav1d.Tests;

/// <summary>
/// Checks that the native library this package ships is the one it thinks it ships, that it is found in the
/// package's own runtimes layout rather than by luck, and that a failure to find it says where it looked.
/// </summary>
public class Dav1dLibraryTests
{
    [Fact]
    public void The_loaded_library_is_dav1d_one_five_four_speaking_api_seven()
    {
        //Arrange
        Dav1dLibrary.EnsureLoaded();

        //Act
        string version = Dav1dLibrary.Version;
        int major = Dav1dLibrary.ApiVersionMajor;
        int minor = Dav1dLibrary.ApiVersionMinor;
        int patch = Dav1dLibrary.ApiVersionPatch;

        //Assert
        version.Should().Be(Dav1dNativeLayout.ExpectedVersion);
        major.Should().Be(Dav1dNativeLayout.ApiVersionMajor);
        minor.Should().Be(0);
        patch.Should().Be(0);
        Dav1dLibrary.IsLoaded.Should().BeTrue();
    }

    [Fact]
    public void The_registration_front_door_reports_the_same_version_the_loader_does()
    {
        //Arrange & Act
        string version = CodeBrixVideoPlaybackDav1d.NativeVersion;
        string api = CodeBrixVideoPlaybackDav1d.NativeApiVersion;

        //Assert
        version.Should().Be(Dav1dNativeLayout.ExpectedVersion);
        api.Should().Be("7.0.0");
    }

    [Fact]
    public void The_resolver_finds_the_native_in_the_packages_own_runtimes_folder()
    {
        //Arrange
        Dav1dLibrary.EnsureLoaded();
        string expectedFragment = Path.Combine("runtimes", Dav1dLibrary.RuntimeIdentifier, "native");

        //Act
        string path = Dav1dLibrary.LoadedPath;

        //Assert
        path.Should().NotBeNullOrEmpty();
        File.Exists(path).Should().BeTrue();
        path.Contains(expectedFragment, StringComparison.Ordinal).Should().BeTrue();
        Path.GetFileName(path).Should().Be(Dav1dLibrary.NativeFileName);
    }

    [Fact]
    public void The_runtime_identifier_is_the_one_the_package_ships_a_native_for()
    {
        //Arrange
        string[] shipped =
        {
            "win-x64", "win-arm64", "osx-x64", "osx-arm64", "linux-x64", "linux-arm64", "linux-riscv64",
        };

        //Act
        string identifier = Dav1dLibrary.RuntimeIdentifier;

        //Assert
        shipped.Should().Contain(identifier);
    }

    [Fact]
    public void The_loader_probes_the_application_folder_and_then_the_runtimes_folder()
    {
        //Arrange
        string[] directories = { "/somewhere/app", "/somewhere/else" };

        //Act
        IReadOnlyList<string> probed =
            Dav1dLibrary.EnumerateProbePaths(directories, "linux-x64", "libdav1d.so");

        //Assert
        probed.Count.Should().Be(4);
        probed[0].Should().Be(Path.Combine("/somewhere/app", "libdav1d.so"));
        probed[1].Should().Be(Path.Combine("/somewhere/app", "runtimes", "linux-x64", "native", "libdav1d.so"));
        probed[2].Should().Be(Path.Combine("/somewhere/else", "libdav1d.so"));
        probed[3].Should().Be(Path.Combine("/somewhere/else", "runtimes", "linux-x64", "native", "libdav1d.so"));
    }

    [Fact]
    public void A_missing_native_is_reported_with_every_path_that_was_tried()
    {
        //Arrange
        string[] directories = { "/no/such/application/folder" };

        //Act
        string message = Dav1dLibrary.DescribeLoadFailure(directories, "linux-riscv64", "libdav1d.so");

        //Assert
        message.Should().Contain("/no/such/application/folder/libdav1d.so");
        message.Should().Contain(
            Path.Combine("/no/such/application/folder", "runtimes", "linux-riscv64", "native", "libdav1d.so"));
        message.Should().Contain("operating system's own search path");
        message.Should().Contain("runtimes/linux-riscv64/native/");
    }

    [Fact]
    public void The_base_directories_start_with_the_folder_the_application_is_running_from()
    {
        //Arrange & Act
        IReadOnlyList<string> directories = Dav1dLibrary.BaseDirectories();

        //Assert
        directories.Count.Should().BeGreaterThan(0);
        directories[0].Should().Be(AppContext.BaseDirectory);
    }

    [Fact]
    public void The_native_file_name_matches_the_platform_the_test_is_running_on()
    {
        //Arrange
        string expected = OperatingSystem.IsWindows()
            ? "dav1d.dll"
            : OperatingSystem.IsMacOS() ? "libdav1d.dylib" : "libdav1d.so";

        //Act
        string fileName = Dav1dLibrary.NativeFileName;

        //Assert
        fileName.Should().Be(expected);
    }
}
