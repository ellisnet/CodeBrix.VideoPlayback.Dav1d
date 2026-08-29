using System;
using System.Runtime.InteropServices;

namespace CodeBrix.VideoPlayback.Dav1d.Interop;

/// <summary>
/// The dav1d entry points this binding uses, as source-generated <c>LibraryImport</c> declarations.
/// </summary>
/// <remarks>
/// <para>
/// Every signature is written in blittable types - pointers, integers and the structures in this
/// namespace - so the generator emits no marshalling code at all and a call is a direct transition into
/// native code. That matters here: <c>dav1d_get_picture</c> runs once per frame and
/// <c>dav1d_send_data</c> once per packet.
/// </para>
/// <para>
/// The static constructor installs the library resolver, so the very first call through any of these
/// declarations finds the native library in the package's <c>runtimes/</c> layout rather than only on the
/// operating system's search path.
/// </para>
/// </remarks>
internal static unsafe partial class Dav1dNative
{
    static Dav1dNative()
    {
        Dav1dLibrary.EnsureResolverInstalled();
    }

    /// <summary>Returns the library's version string, for example "1.5.4". The string is owned by the library.</summary>
    /// <returns>A pointer to a null-terminated UTF-8 string.</returns>
    [LibraryImport(Dav1dLibrary.LibraryName, EntryPoint = "dav1d_version")]
    internal static partial IntPtr GetVersion();

    /// <summary>Returns the library's API version, packed as <c>0x00XXYYZZ</c>.</summary>
    /// <returns>The packed API version.</returns>
    [LibraryImport(Dav1dLibrary.LibraryName, EntryPoint = "dav1d_version_api")]
    internal static partial int GetApiVersion();

    /// <summary>Fills in a settings structure with the library's defaults.</summary>
    /// <param name="settings">The settings to fill in.</param>
    [LibraryImport(Dav1dLibrary.LibraryName, EntryPoint = "dav1d_default_settings")]
    internal static partial void DefaultSettings(Dav1dSettings* settings);

    /// <summary>Opens a decoder instance.</summary>
    /// <param name="context">Receives the new decoder instance.</param>
    /// <param name="settings">The settings to open it with.</param>
    /// <returns>0, or a negative dav1d error code.</returns>
    [LibraryImport(Dav1dLibrary.LibraryName, EntryPoint = "dav1d_open")]
    internal static partial int Open(IntPtr* context, Dav1dSettings* settings);

    /// <summary>Parses a sequence header out of a block of bitstream data.</summary>
    /// <param name="header">Receives the parsed sequence header.</param>
    /// <param name="data">The data to parse.</param>
    /// <param name="size">How many bytes there are.</param>
    /// <returns>
    /// 0 on success, <c>DAV1D_ERR(ENOENT)</c> when the data carries no sequence header, or another negative
    /// dav1d error code.
    /// </returns>
    [LibraryImport(Dav1dLibrary.LibraryName, EntryPoint = "dav1d_parse_sequence_header")]
    internal static partial int ParseSequenceHeader(Dav1dSequenceHeader* header, byte* data, UIntPtr size);

    /// <summary>Hands the decoder a block of bitstream data.</summary>
    /// <param name="context">The decoder instance.</param>
    /// <param name="data">
    /// The data. On success the library takes the reference and zeroes this structure; on
    /// <c>DAV1D_ERR(EAGAIN)</c> it is left exactly as it was and the same value should be offered again.
    /// </param>
    /// <returns>0, <c>DAV1D_ERR(EAGAIN)</c>, or another negative dav1d error code.</returns>
    [LibraryImport(Dav1dLibrary.LibraryName, EntryPoint = "dav1d_send_data")]
    internal static partial int SendData(IntPtr context, Dav1dData* data);

    /// <summary>Takes the next decoded picture.</summary>
    /// <param name="context">The decoder instance.</param>
    /// <param name="picture">Receives the picture; the caller owns the reference.</param>
    /// <returns>0, <c>DAV1D_ERR(EAGAIN)</c> when nothing is ready, or another negative dav1d error code.</returns>
    [LibraryImport(Dav1dLibrary.LibraryName, EntryPoint = "dav1d_get_picture")]
    internal static partial int GetPicture(IntPtr context, Dav1dPicture* picture);

    /// <summary>Applies film grain to an already decoded picture.</summary>
    /// <param name="context">The decoder instance.</param>
    /// <param name="output">Receives the grained picture; the caller owns the reference.</param>
    /// <param name="input">The picture to apply grain to; no ownership is transferred.</param>
    /// <returns>0, or a negative dav1d error code.</returns>
    /// <remarks>
    /// Only useful when the decoder was opened with grain synthesis switched off, which is what
    /// <see cref="CodeBrix.VideoPlayback.Decoding.VideoDecoderOptions.ApplyFilmGrain" /> set to false does.
    /// Calling it on a picture that already has grain applies grain twice.
    /// </remarks>
    [LibraryImport(Dav1dLibrary.LibraryName, EntryPoint = "dav1d_apply_grain")]
    internal static partial int ApplyGrain(IntPtr context, Dav1dPicture* output, Dav1dPicture* input);

    /// <summary>Throws away every delayed frame and clears the decoder's state, for a seek.</summary>
    /// <param name="context">The decoder instance.</param>
    [LibraryImport(Dav1dLibrary.LibraryName, EntryPoint = "dav1d_flush")]
    internal static partial void Flush(IntPtr context);

    /// <summary>Closes a decoder instance and frees everything it holds.</summary>
    /// <param name="context">The decoder instance; set to null on return.</param>
    [LibraryImport(Dav1dLibrary.LibraryName, EntryPoint = "dav1d_close")]
    internal static partial void Close(IntPtr* context);

    /// <summary>Reads and clears the decoder's event flags.</summary>
    /// <param name="context">The decoder instance.</param>
    /// <param name="flags">Receives the flags.</param>
    /// <returns>0, or a negative dav1d error code.</returns>
    [LibraryImport(Dav1dLibrary.LibraryName, EntryPoint = "dav1d_get_event_flags")]
    internal static partial int GetEventFlags(IntPtr context, Dav1dEventFlags* flags);

    /// <summary>Reads the packet metadata belonging to the last decoding error the decoder reported.</summary>
    /// <param name="context">The decoder instance.</param>
    /// <param name="properties">Receives the metadata; the caller owns the reference.</param>
    /// <returns>0, or a negative dav1d error code.</returns>
    [LibraryImport(Dav1dLibrary.LibraryName, EntryPoint = "dav1d_get_decode_error_data_props")]
    internal static partial int GetDecodeErrorDataProps(IntPtr context, Dav1dDataProps* properties);

    /// <summary>Reports how many frames a decoder opened with these settings would buffer internally.</summary>
    /// <param name="settings">The settings that would be used.</param>
    /// <returns>The frame delay, at least 1, or a negative dav1d error code.</returns>
    [LibraryImport(Dav1dLibrary.LibraryName, EntryPoint = "dav1d_get_frame_delay")]
    internal static partial int GetFrameDelay(Dav1dSettings* settings);

    /// <summary>Wraps a block of memory the caller owns in a reference-counted data structure.</summary>
    /// <param name="data">Receives the reference.</param>
    /// <param name="buffer">The first byte of the memory to wrap.</param>
    /// <param name="size">How many bytes there are.</param>
    /// <param name="freeCallback">
    /// Called when the library releases its last reference to the memory. It may run on any thread, so it
    /// must be thread-safe.
    /// </param>
    /// <param name="cookie">The pointer passed back to the callback.</param>
    /// <returns>0, or a negative dav1d error code.</returns>
    [LibraryImport(Dav1dLibrary.LibraryName, EntryPoint = "dav1d_data_wrap")]
    internal static partial int DataWrap(
        Dav1dData* data,
        byte* buffer,
        UIntPtr size,
        delegate* unmanaged[Cdecl]<byte*, IntPtr, void> freeCallback,
        IntPtr cookie);

    /// <summary>Releases a data reference and zeroes the structure.</summary>
    /// <param name="data">The reference to release.</param>
    [LibraryImport(Dav1dLibrary.LibraryName, EntryPoint = "dav1d_data_unref")]
    internal static partial void DataUnref(Dav1dData* data);

    /// <summary>Releases a picture reference and zeroes the structure.</summary>
    /// <param name="picture">The reference to release.</param>
    [LibraryImport(Dav1dLibrary.LibraryName, EntryPoint = "dav1d_picture_unref")]
    internal static partial void PictureUnref(Dav1dPicture* picture);

    /// <summary>Releases a packet-metadata reference.</summary>
    /// <param name="properties">The reference to release.</param>
    [LibraryImport(Dav1dLibrary.LibraryName, EntryPoint = "dav1d_data_props_unref")]
    internal static partial void DataPropsUnref(Dav1dDataProps* properties);
}
