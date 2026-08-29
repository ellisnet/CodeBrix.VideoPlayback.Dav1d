using System;
using System.Linq;
using CodeBrix.VideoPlayback.Decoding;
using SilverAssertions;
using Xunit;

namespace CodeBrix.VideoPlayback.Dav1d.Tests;

/// <summary>
/// Checks the one call an application makes, and the promises made about it: idempotent, thread-safe,
/// available per session as well as per process, and never a module initializer.
/// </summary>
[Collection("Process-wide registries")]
public class CodeBrixVideoPlaybackDav1dTests
{
    [Fact]
    public void Nothing_is_registered_until_something_asks_for_it()
    {
        //Arrange
        CodeBrixVideoPlaybackDav1d.Unregister();
        VideoDecoders.Clear();

        //Act
        bool supported = VideoDecoders.IsCodecSupported(VideoCodecIds.Av1);

        //Assert
        supported.Should().BeFalse();
        CodeBrixVideoPlaybackDav1d.IsRegistered.Should().BeFalse();
    }

    [Fact]
    public void Registering_makes_av1_available_and_registering_twice_registers_once()
    {
        //Arrange
        CodeBrixVideoPlaybackDav1d.Unregister();
        VideoDecoders.Clear();

        //Act
        CodeBrixVideoPlaybackDav1d.Register();
        CodeBrixVideoPlaybackDav1d.Register();

        try
        {
            //Assert
            CodeBrixVideoPlaybackDav1d.IsRegistered.Should().BeTrue();
            VideoDecoders.IsCodecSupported(VideoCodecIds.Av1).Should().BeTrue();
            VideoDecoders.RegisteredFactories.Count(factory =>
                ReferenceEquals(factory, CodeBrixVideoPlaybackDav1d.Factory)).Should().Be(1);
        }
        finally
        {
            CodeBrixVideoPlaybackDav1d.Unregister();
            VideoDecoders.Clear();
        }
    }

    [Fact]
    public void A_session_can_be_given_the_decoder_without_disturbing_the_process()
    {
        //Arrange
        CodeBrixVideoPlaybackDav1d.Unregister();
        VideoDecoders.Clear();
        using VideoPlaybackSession session = new VideoPlaybackSession();

        //Act
        CodeBrixVideoPlaybackDav1d.Register(session);

        //Assert
        CodeBrixVideoPlaybackDav1d.IsRegistered.Should().BeFalse();
        VideoDecoders.IsCodecSupported(VideoCodecIds.Av1).Should().BeFalse();
    }

    [Fact]
    public void Registering_with_a_null_session_is_refused()
    {
        //Arrange
        Action act = () => CodeBrixVideoPlaybackDav1d.Register(null);

        //Act & Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void The_factory_is_one_instance_that_never_changes()
    {
        //Arrange & Act
        Dav1dDecoderFactory first = CodeBrixVideoPlaybackDav1d.Factory;
        Dav1dDecoderFactory second = CodeBrixVideoPlaybackDav1d.Factory;

        //Assert
        first.Should().BeSameAs(second);
    }

    [Fact]
    public void The_assembly_carries_no_module_initializer_so_a_trimmer_can_drop_it_all()
    {
        //Arrange
        Type[] types = typeof(CodeBrixVideoPlaybackDav1d).Assembly.GetTypes();

        //Act
        bool hasModuleInitializer = types.SelectMany(type =>
                type.GetMethods(System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic))
            .Any(method => method.GetCustomAttributes(
                typeof(System.Runtime.CompilerServices.ModuleInitializerAttribute), false).Length > 0);

        //Assert
        hasModuleInitializer.Should().BeFalse();
    }
}
