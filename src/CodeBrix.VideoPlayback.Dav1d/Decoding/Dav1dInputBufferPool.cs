using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace CodeBrix.VideoPlayback.Dav1d.Decoding;

/// <summary>
/// Recycles the pinned blocks compressed packets are handed to dav1d in.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="CodeBrix.VideoPlayback.Decoding.VideoPacket" />'s memory only has to stay valid for the
/// duration of the call that receives it, but dav1d keeps a reference to bitstream data until it has finished
/// parsing it - which may be after several more calls. So the bytes are copied once, into a block that stays
/// put, and dav1d reads that block directly rather than copying it again itself.
/// </para>
/// <para>
/// Blocks come back through dav1d's free callback, which may run on any thread, so every member here is
/// thread-safe. In the steady state the pool holds a handful of blocks sized to the largest packet seen and
/// allocates nothing more.
/// </para>
/// </remarks>
internal sealed unsafe class Dav1dInputBufferPool
{
    private readonly object gate = new object();
    private readonly Stack<Dav1dInputBuffer> free = new Stack<Dav1dInputBuffer>();
    private readonly List<Dav1dInputBuffer> all = new List<Dav1dInputBuffer>();

    private int checkedOut;
    private bool disposed;
    private long allocations;

    /// <summary>How many blocks have actually been allocated - the number that must stop rising.</summary>
    public long Allocations => Interlocked.Read(ref allocations);

    /// <summary>How many blocks dav1d is holding right now.</summary>
    public int CheckedOut
    {
        get
        {
            lock (gate) return checkedOut;
        }
    }

    /// <summary>The callback dav1d releases blocks through.</summary>
    public static delegate* unmanaged[Cdecl]<byte*, IntPtr, void> FreeCallback => &FreeBuffer;

    /// <summary>Takes a block holding a copy of the packet.</summary>
    /// <param name="packet">The bytes to copy in.</param>
    /// <returns>The block, which dav1d releases when it is finished with it.</returns>
    /// <exception cref="ObjectDisposedException">The pool has been disposed.</exception>
    public Dav1dInputBuffer Rent(ReadOnlySpan<byte> packet)
    {
        Dav1dInputBuffer buffer;

        lock (gate)
        {
            if (disposed) throw new ObjectDisposedException(nameof(Dav1dInputBufferPool));

            if (free.Count > 0)
            {
                buffer = free.Pop();
            }
            else
            {
                buffer = new Dav1dInputBuffer(packet.Length, this);
                all.Add(buffer);
                allocations++;
            }

            checkedOut++;
        }

        buffer.Fill(packet);
        return buffer;
    }

    /// <summary>Gives back a block that was taken but never handed to dav1d.</summary>
    /// <param name="buffer">The block to give back.</param>
    public void ReturnUnused(Dav1dInputBuffer buffer)
    {
        if (buffer != null) Recycle(buffer);
    }

    /// <summary>
    /// Marks the pool finished. Blocks dav1d still holds are released as they come back; the pool's handles
    /// go away when the last one does.
    /// </summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            if (checkedOut == 0) FreeEverythingNoLock();
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void FreeBuffer(byte* data, IntPtr cookie)
    {
        try
        {
            if (GCHandle.FromIntPtr(cookie).Target is not Dav1dInputBuffer buffer) return;
            buffer.Pool.Recycle(buffer);
        }
        catch (Exception)
        {
            // Nothing may propagate into native code; dav1d may be releasing this from a frame thread.
        }
    }

    private void Recycle(Dav1dInputBuffer buffer)
    {
        lock (gate)
        {
            if (checkedOut > 0) checkedOut--;

            if (disposed)
            {
                if (checkedOut == 0) FreeEverythingNoLock();
                return;
            }

            free.Push(buffer);
        }
    }

    private void FreeEverythingNoLock()
    {
        foreach (Dav1dInputBuffer buffer in all) buffer.FreeHandle();
        all.Clear();
        free.Clear();
    }
}
