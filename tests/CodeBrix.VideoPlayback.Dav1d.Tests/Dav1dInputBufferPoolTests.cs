using CodeBrix.VideoPlayback.Dav1d.Decoding;
using SilverAssertions;
using Xunit;

namespace CodeBrix.VideoPlayback.Dav1d.Tests;

/// <summary>
/// Fences the check on the address dav1d hands back when it releases a compressed packet's block.
/// </summary>
public unsafe class Dav1dInputBufferPoolTests
{
    [Fact]
    public void TryRecycleReleased_takes_a_block_back_at_its_own_address()
    {
        //Arrange
        Dav1dInputBufferPool pool = new Dav1dInputBufferPool();
        Dav1dInputBuffer buffer = pool.Rent(new byte[] { 1, 2, 3, 4 });

        //Act
        bool recycled = pool.TryRecycleReleased(buffer, buffer.Address);

        //Assert
        recycled.Should().BeTrue();
        pool.CheckedOut.Should().Be(0);
        pool.Dispose();
    }

    [Fact]
    public void TryRecycleReleased_keeps_a_block_released_at_a_different_address_out_of_circulation()
    {
        //Arrange
        // If the native side were confused about WHICH block it had finished with, recycling this one would
        // let the next packet overwrite memory dav1d may still be parsing. The block stays checked out.
        Dav1dInputBufferPool pool = new Dav1dInputBufferPool();
        Dav1dInputBuffer buffer = pool.Rent(new byte[] { 1, 2, 3, 4 });

        //Act
        bool recycled = pool.TryRecycleReleased(buffer, buffer.Address + 1);

        //Assert
        recycled.Should().BeFalse();
        pool.CheckedOut.Should().Be(1);
        pool.Rent(new byte[] { 5, 6 }).Should().NotBeSameAs(buffer);
        pool.Dispose();
    }
}
