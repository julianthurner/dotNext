using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using static System.Runtime.CompilerServices.Unsafe;

namespace DotNext.Buffers.Text;

using Buffers;

public static partial class Hex
{
    /// <summary>
    /// Converts set of bytes into hexadecimal representation.
    /// </summary>
    /// <param name="bytes">The bytes to convert.</param>
    /// <param name="output">The buffer used to write hexadecimal representation of bytes, in UTF-8 encoding.</param>
    /// <param name="lowercased"><see langword="true"/> to return lowercased hex string; <see langword="false"/> to return uppercased hex string.</param>
    /// <returns>The actual number of characters in <paramref name="output"/> written by the method.</returns>
    public static unsafe int EncodeToUtf8(ReadOnlySpan<byte> bytes, Span<byte> output, bool lowercased = false)
    {
        if (bytes.IsEmpty || output.IsEmpty)
            return 0;

        int bytesCount = Math.Min(bytes.Length, output.Length >> 1), offset;
        ref byte bytePtr = ref MemoryMarshal.GetReference(bytes);
        ref byte charPtr = ref MemoryMarshal.GetReference(output);

        // use hardware intrinsics when possible
        if (Ssse3.IsSupported && bytesCount >= Vector128<short>.Count)
        {
            offset = bytesCount;

            // encode 8 bytes at a time using 128-bit vector (SSSE3 only intructions)
            const int bytesCountPerIteration = sizeof(long);
            const int charsCountPerIteration = bytesCountPerIteration * 2;

            // converts saturated vector back to normal:
            // 1, 0, 2, 0, 3, 0, 4, 0, 5, 0, 6, 0, 7, 0, 8, 0 => 1, 2, 3, 4, 5, 6, 7, 8
            var compressionMask = Vector128.Create(
                0,
                2,
                4,
                6,
                8,
                10,
                12,
                14,
                byte.MaxValue,
                byte.MaxValue,
                byte.MaxValue,
                byte.MaxValue,
                byte.MaxValue,
                byte.MaxValue,
                byte.MaxValue,
                byte.MaxValue);

            var nimbles = NimbleToUtf8CharLookupTable(lowercased);

            var lowBitsMask = Vector128.Create(
                NimbleMaxValue,
                NimbleMaxValue,
                NimbleMaxValue,
                NimbleMaxValue,
                NimbleMaxValue,
                NimbleMaxValue,
                NimbleMaxValue,
                NimbleMaxValue,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0);

            for (Vector128<byte> input; offset >= Vector128<short>.Count; offset -= Vector128<short>.Count, bytePtr = ref Add(ref bytePtr, bytesCountPerIteration), charPtr = ref Add(ref charPtr, charsCountPerIteration))
            {
                input = Vector128.CreateScalarUnsafe(ReadUnaligned<long>(ref bytePtr)).AsByte();

                // apply x & 0B1111 for each vector component to get the lower nibbles;
                // then do table lookup
                var lowNibbles = Ssse3.Shuffle(nimbles, Ssse3.And(input, lowBitsMask).AsByte());

                // apply (x & 0B1111_0000) >> 4 for each vector component to get the higher nibbles
                // then do table lookup
                var highNibbles = Ssse3.Shuffle(nimbles, Ssse3.ShiftRightLogical(Ssse3.And(Ssse3.Shuffle(input, SaturationMask).AsInt16(), HighBitsMask), 4).AsByte());

                // restore natural ordering
                highNibbles = Ssse3.Shuffle(highNibbles, compressionMask);

                // encode to hex
                var result = Ssse3.UnpackLow(highNibbles, lowNibbles);

                fixed (byte* ptr = &charPtr)
                {
                    Ssse3.Store(ptr, result);
                }
            }

            offset = bytesCount - offset;
        }
        else
        {
            offset = 0;
        }

        ref char hexTable = ref MemoryMarshal.GetArrayDataReference(NimbleToUtf16CharLookupTable);
        if (!lowercased)
            hexTable = ref Unsafe.Add(ref hexTable, 16);

        for (; offset < bytesCount; offset++, charPtr = ref Add(ref charPtr, 1), bytePtr = ref Add(ref bytePtr, 1))
        {
            var value = bytePtr;
            charPtr = (byte)Add(ref hexTable, value >> 4);
            charPtr = ref Add(ref charPtr, 1);
            charPtr = (byte)Add(ref hexTable, value & NimbleMaxValue);
        }

        return bytesCount << 1;
    }

    /// <summary>
    /// Converts set of bytes into hexadecimal representation.
    /// </summary>
    /// <param name="bytes">The bytes to convert.</param>
    /// <param name="lowercased"><see langword="true"/> to return lowercased hex string; <see langword="false"/> to return uppercased hex string.</param>
    /// <returns>The hexadecimal representation of bytes.</returns>
    public static byte[] EncodeToUtf8(ReadOnlySpan<byte> bytes, bool lowercased = false)
    {
        var count = bytes.Length << 1;
        if (count is 0)
            return Array.Empty<byte>();

        using MemoryRental<byte> buffer = (uint)count <= (uint)MemoryRental<byte>.StackallocThreshold ? stackalloc byte[count] : new MemoryRental<byte>(count);
        count = EncodeToUtf8(bytes, buffer.Span, lowercased);
        return buffer.Span.Slice(0, count).ToArray();
    }

    /// <summary>
    /// Decodes hexadecimal representation of bytes.
    /// </summary>
    /// <param name="chars">The hexadecimal representation of bytes.</param>
    /// <param name="output">The output buffer used to write decoded bytes.</param>
    /// <returns>The actual number of bytes in <paramref name="output"/> written by the method.</returns>
    /// <exception cref="FormatException"><paramref name="chars"/> contain invalid hexadecimal symbol.</exception>
    public static int DecodeFromUtf8(this ReadOnlySpan<byte> chars, Span<byte> output)
    {
        if (chars.IsEmpty || output.IsEmpty)
            return 0;
        var charCount = Math.Min(chars.Length, output.Length << 1);
        charCount &= -2; // convert even to odd integer
        ref byte bytePtr = ref MemoryMarshal.GetReference(output);
        for (var i = 0; i < charCount; i += 2, bytePtr = ref Add(ref bytePtr, 1))
        {
            var high = Unsafe.Add(ref MemoryMarshal.GetReference(chars), i);
            var low = Unsafe.Add(ref MemoryMarshal.GetReference(chars), i + 1);

            bytePtr = (byte)(ToNimble(low) | (ToNimble(high) << 4));
        }

        return charCount >> 1;
    }
}