using System;
using System.Runtime.InteropServices;

namespace CodeBrix.VideoPlayback.Dav1d.Decoding;

/// <summary>
/// One pinned block of managed memory holding a compressed packet while dav1d reads it.
/// </summary>
/// <remarks>
/// The array is allocated on the pinned object heap, so its address never moves and dav1d can read it
/// directly - no copy into native memory, and no long-lived pinning handle fragmenting the ordinary heap.
/// The handle here identifies the block to dav1d's free callback and nothing else.
/// </remarks>
internal sealed unsafe class Dav1dInputBuffer
{
    private byte[] storage;

    /// <summary>Creates a block with room for at least the given number of bytes.</summary>
    /// <param name="capacity">The number of bytes the block must hold.</param>
    /// <param name="pool">The pool that recycles this block.</param>
    public Dav1dInputBuffer(int capacity, Dav1dInputBufferPool pool)
    {
        Pool = pool;
        Handle = GCHandle.Alloc(this);
        Grow(capacity);
    }

    /// <summary>The pool that recycles this block.</summary>
    public Dav1dInputBufferPool Pool { get; }

    /// <summary>The handle dav1d's free callback receives as its cookie.</summary>
    public GCHandle Handle { get; private set; }

    /// <summary>The first byte of the block. Stable for the life of the block's current storage.</summary>
    public byte* Address { get; private set; }

    /// <summary>How many bytes the block can hold.</summary>
    public int Capacity => storage.Length;

    /// <summary>Makes sure the block can hold at least the given number of bytes.</summary>
    /// <param name="capacity">The number of bytes needed.</param>
    public void Grow(int capacity)
    {
        if (storage != null && storage.Length >= capacity) return;

        int size = 4096;
        while (size < capacity) size *= 2;

        storage = GC.AllocateUninitializedArray<byte>(size, pinned: true);
        Address = (byte*)Unsafe_AsPointer(storage);
    }

    /// <summary>Copies a packet into the block.</summary>
    /// <param name="source">The bytes to copy.</param>
    public void Fill(ReadOnlySpan<byte> source)
    {
        Grow(source.Length);
        source.CopyTo(new Span<byte>(Address, source.Length));
    }

    /// <summary>Frees the handle that identifies this block to dav1d.</summary>
    public void FreeHandle()
    {
        if (Handle.IsAllocated) Handle.Free();
        Handle = default;
    }

    private static byte* Unsafe_AsPointer(byte[] pinnedArray) =>
        (byte*)System.Runtime.CompilerServices.Unsafe.AsPointer(
            ref MemoryMarshal.GetArrayDataReference(pinnedArray));
}
