using System;
using System.Buffers;
using System.Text.Unicode;

namespace GodotNodeExtension.Component.GodotSkia;

/// <summary>
/// The UTF-8, NUL-terminated entry-point names the Vulkan loader wants: <c>vkGetInstanceProcAddr</c> and
/// friends take a <c>char*</c>, and the two places that call them used to carry the same encoding step
/// (and the same "does it fit" question) on their own.
/// </summary>
internal static class VkProcName
{
    /// <summary>Bytes a name may take, leaving room for the NUL terminator.</summary>
    public const int MaxBytes = 127;

    /// <summary>
    /// Write <paramref name="name"/> as UTF-8 plus a NUL terminator into <paramref name="buffer"/>.
    /// The buffer stays owned by the caller, which is what keeps it alive across the native call
    /// (<c>fixed</c> at the call site) - returning a pointer from here would pin it only for the return.
    /// </summary>
    /// <exception cref="InvalidOperationException">The name does not fit the buffer.</exception>
    public static void Encode(string name, Span<byte> buffer)
    {
        if (Utf8.FromUtf16(name, buffer[..^1], out _, out int written) != OperationStatus.Done)
        {
            throw new InvalidOperationException(
                $"Vulkan entry point name '{name}' does not fit a {buffer.Length - 1}-byte UTF-8 buffer");
        }
        buffer[written] = 0;
    }
}
