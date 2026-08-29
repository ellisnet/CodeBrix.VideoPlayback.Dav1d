using System;
using System.Runtime.InteropServices;
using System.Threading;
using CodeBrix.VideoPlayback.Dav1d.Interop;
using CodeBrix.VideoPlayback.Frames;

namespace CodeBrix.VideoPlayback.Dav1d.Decoding;

/// <summary>
/// The bridge between a managed frame's reference count and dav1d's own: one lease holds one
/// <c>Dav1dPicture</c> reference and releases it when the last <see cref="VideoFrame" /> over that picture
/// is disposed.
/// </summary>
/// <remarks>
/// <para>
/// A frame's buffer must go back to the pool when NOBODY is reading it, and there are two parties who might
/// be: the application, through <see cref="VideoFrame" />, and dav1d itself, which keeps decoded pictures
/// alive as prediction references for later frames. Neither count knows about the other, so the two are
/// stacked: the managed count sits over exactly one <c>dav1d_picture_ref</c>, and dav1d's own count sits
/// under it. When the managed count reaches zero this lease calls <c>dav1d_picture_unref</c>; if that was
/// dav1d's last reference too, dav1d calls the allocator's release callback and the buffer goes home. If it
/// was not, the buffer stays out - correctly - until dav1d is finished with it.
/// </para>
/// <para>
/// A lease is therefore an <see cref="IVideoFrameBufferPool" /> that only ever answers the "give it back"
/// half of the contract: <see cref="VideoFrame" /> calls <see cref="Return" /> on the pool it was created
/// with, and this is that pool. Leases are recycled by the decoder that made them, so a decode loop creates
/// no garbage here.
/// </para>
/// <para>
/// Standing between the frame and the session's pool has one consequence worth stating: the frame OBJECT is
/// asked for through this lease as well, not through the session's pool directly. So the lease forwards
/// <see cref="TakeFrame" /> and <see cref="ReturnFrame" /> straight on to the session's pool, which is
/// where they belong - one free list shared by every lease and every frame of the session, rather than one
/// per picture. Without that forwarding a frame object would be allocated for every decoded picture, which
/// is the one thing the whole pooled path exists to avoid.
/// </para>
/// </remarks>
internal sealed unsafe class Dav1dPictureLease : IVideoFrameBufferPool
{
    private readonly Dav1dVideoDecoder owner;
    private readonly IVideoFrameBufferPool hostPool;
    private Dav1dPicture* picture;
    private int released;

    /// <summary>Creates a lease owned by a decoder.</summary>
    /// <param name="decoder">The decoder that recycles this lease.</param>
    /// <param name="pool">The session's own pool, which frame objects are borrowed from and given back to.</param>
    public Dav1dPictureLease(Dav1dVideoDecoder decoder, IVideoFrameBufferPool pool)
    {
        owner = decoder ?? throw new ArgumentNullException(nameof(decoder));
        hostPool = pool ?? throw new ArgumentNullException(nameof(pool));
        picture = (Dav1dPicture*)NativeMemory.AllocZeroed((nuint)sizeof(Dav1dPicture));
    }

    /// <summary>The picture reference this lease holds.</summary>
    public Dav1dPicture* Picture => picture;

    /// <summary>Prepares a recycled lease for its next picture.</summary>
    public void Reset()
    {
        Volatile.Write(ref released, 0);
        if (picture != null) NativeMemory.Clear(picture, (nuint)sizeof(Dav1dPicture));
    }

    /// <summary>Not supported: a lease hands buffers back, it never gives them out.</summary>
    /// <param name="descriptor">Ignored.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public VideoFrameBuffer Rent(VideoFrameBufferDescriptor descriptor) =>
        throw new NotSupportedException(
            "A dav1d picture lease only releases frames; buffers are rented from the session's own pool by "
            + "dav1d's picture allocator.");

    /// <inheritdoc />
    /// <remarks>
    /// Forwarded to the session's pool, so every lease of a session shares one free list of frame objects
    /// rather than keeping one each. Safe to call after this lease has been recycled or freed: the pool it
    /// forwards to is fixed when the lease is created and never changes.
    /// </remarks>
    public VideoFrame TakeFrame() => hostPool.TakeFrame();

    /// <inheritdoc />
    /// <remarks>
    /// Forwarded to the session's pool, for the same reason as <see cref="TakeFrame" />. This arrives just
    /// after <see cref="Return" /> has already handed the lease back to the decoder, so it must not touch
    /// any of the lease's own state - and it does not.
    /// </remarks>
    public void ReturnFrame(VideoFrame frame) => hostPool.ReturnFrame(frame);

    /// <summary>
    /// Releases this lease's dav1d picture reference, which returns the buffer to the session's pool once
    /// dav1d has no references of its own left.
    /// </summary>
    /// <param name="buffer">The buffer the frame was over. Not used: dav1d knows which buffer this is.</param>
    /// <remarks>Called from whichever thread drops the last reference to the frame.</remarks>
    public void Return(VideoFrameBuffer buffer)
    {
        if (Interlocked.Exchange(ref released, 1) != 0) return;
        if (picture != null) Dav1dNative.PictureUnref(picture);
        owner.RecycleLease(this);
    }

    /// <summary>Releases the picture reference without going through the recycling path.</summary>
    /// <remarks>Used when a lease was taken but no frame was built over it.</remarks>
    public void ReleasePictureOnly()
    {
        if (Interlocked.Exchange(ref released, 1) != 0) return;
        if (picture != null) Dav1dNative.PictureUnref(picture);
    }

    /// <summary>Frees the native picture structure this lease owns.</summary>
    public void FreeNative()
    {
        Dav1dPicture* owned = picture;
        picture = null;
        if (owned != null) NativeMemory.Free(owned);
    }
}
