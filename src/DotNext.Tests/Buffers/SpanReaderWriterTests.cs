using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using DotNext.IO;
using DotNext.Text;

namespace DotNext.Buffers;

using Binary;

public sealed class SpanReaderTests : Test
{
    [Fact]
    public static unsafe void WriteAndGet()
    {
        var writer = new SpanWriter<int>(stackalloc int[5]);
        Equal(0, writer.WrittenCount);
        Equal(5, writer.FreeCapacity);
        ref int current = ref writer.Current;

        writer.Add(10);
        Equal(1, writer.WrittenCount);
        Equal(4, writer.FreeCapacity);
        Equal(10, current);

        var segment = writer.Slide(4);
        segment[0] = 20;
        segment[1] = 30;
        segment[2] = 40;
        segment[3] = 50;
        Equal(5, writer.WrittenCount);
        Equal(0, writer.FreeCapacity);
        Equal(new int[] { 10, 20, 30, 40, 50 }, writer.WrittenSpan.ToArray());

        var exceptionThrown = false;
        try
        {
            writer.Add(42);
        }
        catch (InternalBufferOverflowException)
        {
            exceptionThrown = true;
        }

        True(exceptionThrown);

        writer.Reset();
        Equal(0, writer.WrittenCount);
    }

    [Fact]
    public static void ReadWrite()
    {
        var writer = new SpanWriter<byte>(stackalloc byte[3]);
        var expected = new byte[] { 10, 20, 30 };
        Equal(3, writer.Write(expected));

        var reader = new SpanReader<byte>(writer.Span);
        Equal(3, reader.RemainingCount);
        Equal(0, reader.ConsumedCount);
        True(reader.ConsumedSpan.IsEmpty);
        Equal(10, reader.Current);

        Equal(10, reader.Read());
        Equal(20, reader.Current);
        Equal(2, reader.RemainingCount);
        Equal(1, reader.ConsumedCount);

        Equal(new byte[] { 10 }, reader.ConsumedSpan.ToArray());
        Equal(new byte[] { 20, 30 }, reader.Read(2).ToArray());

        Equal(0, reader.Read(new byte[2]));

        reader.Reset();
        Equal(0, reader.ConsumedCount);

        var actual = new byte[3];
        Equal(3, reader.Read(actual));
        Equal(expected, actual);
    }

    [Fact]
    public static unsafe void EncodingDecodingBlittableType()
    {
        var writer = new SpanWriter<byte>(stackalloc byte[sizeof(Guid)]);
        var expected = Guid.NewGuid();
        True(writer.TryWrite(new Blittable<Guid> { Value = expected }));

        var reader = new SpanReader<byte>(writer.Span);
        True(reader.TryRead(out Blittable<Guid> actual));
        Equal(expected, actual.Value);

        writer.Reset();
        reader.Reset();
        writer.Write(new Blittable<Guid> { Value = expected });
        Equal(expected, reader.Read<Blittable<Guid>>().Value);
    }

    [Fact]
    public static void EmptyReader()
    {
        var reader = new SpanReader<byte>();
        Equal(0, reader.RemainingCount);
        Equal(0, reader.ConsumedCount);
        Equal([], reader.ReadToEnd().ToArray());

        var exceptionThrown = false;
        try
        {
            reader.Current.ToString();
        }
        catch (InvalidOperationException)
        {
            exceptionThrown = true;
        }

        True(exceptionThrown);
        False(reader.TryRead(new byte[1]));
        False(reader.TryRead(1, out _));
        False(reader.TryRead(out _));
        False(reader.TryRead(out Blittable<Guid> _));

        Equal(0, reader.Read(new byte[1]));

        exceptionThrown = false;
        try
        {
            reader.Read(10);
        }
        catch (InternalBufferOverflowException)
        {
            exceptionThrown = true;
        }

        True(exceptionThrown);
    }

    [Fact]
    public static void EmptyWriter()
    {
        var writer = new SpanWriter<byte>();
        Equal(0, writer.WrittenCount);
        Equal(0, writer.FreeCapacity);

        var exceptionThrown = false;
        try
        {
            writer.Current.ToString();
        }
        catch (InvalidOperationException)
        {
            exceptionThrown = true;
        }

        True(exceptionThrown);
        False(writer.TryWrite(new byte[1]));
        False(writer.TryWrite(1));
        False(writer.TrySlide(2, out _));
        False(writer.TryAdd(1));

        Equal(0, writer.Write(new byte[1]));

        exceptionThrown = false;

        try
        {
            writer.Slide(2);
        }
        catch (ArgumentOutOfRangeException)
        {
            exceptionThrown = true;
        }

        True(exceptionThrown);
    }

    [Fact]
    public static void ReadToEnd()
    {
        var reader = new SpanReader<int>(new[] { 10, 20, 30 });
        Equal(new[] { 10, 20, 30 }, reader.ReadToEnd().ToArray());
        reader.Reset();
        Equal(10, reader.Read());
        Equal(new[] { 20, 30 }, reader.ReadToEnd().ToArray());
    }

    [Fact]
    public static void SlideToEnd()
    {
        Span<byte> expected = stackalloc byte[3];
        var writer = new SpanWriter<byte>(expected);
        writer.Add() = 10;

        var remaining = writer.SlideToEnd();
        True(expected[1..] == remaining);
        
        Random.Shared.NextBytes(remaining);
        Equal(expected, writer.WrittenSpan);
    }

    [Fact]
    public static void ReadWritePrimitives()
    {
        var buffer = new byte[1024];
        var writer = new SpanWriter<byte>(buffer);
        writer.WriteLittleEndian(short.MinValue);
        writer.WriteBigEndian(short.MaxValue);
        writer.WriteLittleEndian<ushort>(42);
        writer.WriteBigEndian(ushort.MaxValue);
        writer.WriteLittleEndian(int.MaxValue);
        writer.WriteBigEndian(int.MinValue);
        writer.WriteLittleEndian(42U);
        writer.WriteBigEndian(uint.MaxValue);
        writer.WriteLittleEndian(long.MaxValue);
        writer.WriteBigEndian(long.MinValue);
        writer.WriteLittleEndian(42UL);
        writer.WriteBigEndian(ulong.MaxValue);

        var reader = new SpanReader<byte>(buffer);
        Equal(short.MinValue, reader.ReadLittleEndian<short>());
        Equal(short.MaxValue, reader.ReadBigEndian<short>());
        Equal(42U, reader.ReadLittleEndian<ushort>());
        Equal(ushort.MaxValue, reader.ReadBigEndian<ushort>());
        Equal(int.MaxValue, reader.ReadLittleEndian<int>());
        Equal(int.MinValue, reader.ReadBigEndian<int>());
        Equal(42U, reader.ReadLittleEndian<uint>());
        Equal(uint.MaxValue, reader.ReadBigEndian<uint>());
        Equal(long.MaxValue, reader.ReadLittleEndian<long>());
        Equal(long.MinValue, reader.ReadBigEndian<long>());
        Equal(42UL, reader.ReadLittleEndian<ulong>());
        Equal(ulong.MaxValue, reader.ReadBigEndian<ulong>());
    }

    [Fact]
    public static unsafe void TryWrite()
    {
        Span<byte> bytes = stackalloc byte[128];
        var writer = new SpanWriter<byte>(bytes);
        BigInteger value = 10L;
        True(writer.TryWrite(&WriteBigInt, value));
        Equal(value, new BigInteger(bytes.Slice(0, writer.WrittenCount)));

        static bool WriteBigInt(BigInteger value, Span<byte> destination, out int count)
            => value.TryWriteBytes(destination, out count);
    }

    [Fact]
    public static unsafe void WriteUsingFunctionPointer()
    {
        Span<byte> bytes = stackalloc byte[128];
        var writer = new SpanWriter<byte>(bytes);
        Guid value = Guid.NewGuid();
        writer.Write(&WriteGuid, value, sizeof(Guid));
        Equal(value, new Guid(bytes.Slice(0, writer.WrittenCount)));

        static void WriteGuid(Guid value, Span<byte> destination)
            => value.TryWriteBytes(destination);
    }

    [Fact]
    public static unsafe void ReadUsingFunctionPointer()
    {
        Span<byte> bytes = stackalloc byte[128];
        var writer = new SpanWriter<byte>(bytes);
        Guid expected = Guid.NewGuid();
        writer.Write(&WriteGuid, expected, sizeof(Guid));

        var reader = new SpanReader<byte>(bytes);
        Equal(expected, reader.Read(&ParseGuid, sizeof(Guid)));

        reader.Rewind(sizeof(Guid));
        True(reader.TryRead(&ParseGuid, sizeof(Guid), out var actual));
        Equal(expected, actual);

        static void WriteGuid(Guid value, Span<byte> destination)
            => value.TryWriteBytes(destination);

        static Guid ParseGuid(ReadOnlySpan<byte> input) => new(input);
    }

    [Fact]
    public static void AdvanceWriter()
    {
        var writer = new SpanWriter<byte>(stackalloc byte[4])
        {
            Current = 10
        };

        writer.Advance(1);

        writer.Current = 20;
        writer.Advance(1);

        writer.Current = 30;
        writer.Advance(2);

        True(writer.RemainingSpan.IsEmpty);

        writer.Rewind(2);
        Equal(30, writer.Current);

        writer.Rewind(1);
        Equal(20, writer.Current);
    }

    [Fact]
    public static void AdvanceReader()
    {
        var reader = new SpanReader<byte>(new byte[] { 10, 20, 30 });
        Equal(10, reader.Current);

        reader.Advance(2);
        Equal(30, reader.Current);

        reader.Rewind(2);
        Equal(10, reader.Current);
    }

    [Fact]
    public static void WriteFormattable()
    {
        var writer = new SpanWriter<char>(stackalloc char[32]);
        True(writer.TryFormat(42, format: "X"));

        Equal(2, writer.WrittenCount);
        Equal("2A", new string(writer.WrittenSpan));
    }

    [Fact]
    public static void WriteUtf8Formattable()
    {
        var writer = new SpanWriter<byte>(stackalloc byte[32]);
        True(writer.TryFormat(42, format: "X"));

        Equal(2, writer.WrittenCount);
        True(MemoryExtensions.SequenceEqual("2A"u8, writer.WrittenSpan));
    }

    [Fact]
    public static void ChangeWrittenCount()
    {
        var writer = new SpanWriter<char>(stackalloc char[32]);
        Equal(0, writer.WrittenCount);

        writer.WrittenCount = 10;
        Equal(10, writer.WrittenCount);

        writer.WrittenCount = 32;
        Equal(32, writer.WrittenCount);
        True(writer.RemainingSpan.IsEmpty);

        var raised = false;
        try
        {
            writer.WrittenCount = -1;
        }
        catch (ArgumentOutOfRangeException)
        {
            raised = true;
        }

        True(raised);
    }

    [Fact]
    public static void ChangeConsumedCount()
    {
        var writer = new SpanReader<char>(stackalloc char[32]);
        Equal(0, writer.ConsumedCount);

        writer.ConsumedCount = 10;
        Equal(10, writer.ConsumedCount);

        writer.ConsumedCount = 32;
        Equal(32, writer.ConsumedCount);
        True(writer.RemainingSpan.IsEmpty);

        var raised = false;
        try
        {
            writer.ConsumedCount = -1;
        }
        catch (ArgumentOutOfRangeException)
        {
            raised = true;
        }

        True(raised);
    }

    [Fact]
    public static void Rendering()
    {
        var writer = new SpanWriter<char>(stackalloc char[16]);
        True(writer.TryFormat(CompositeFormat.Parse("{0}, {1}!"), ["Hello", "world"]));
        Equal("Hello, world!", writer.WrittenSpan.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(128)]
    [InlineData(124)]
    public static void WriteStringBuilder(int stringLength)
    {
        var str = Random.Shared.NextString(Alphabet, stringLength);

        var builder = new StringBuilder();
        for (var i = 0; i < 3; i++)
        {
            builder.Append(str);
        }

        var chars = new char[builder.Length];
        var writer = new SpanWriter<char>(chars);

        writer.Write(builder);
        Equal(builder.ToString(), writer.WrittenSpan);
    }

    [InlineData(LengthFormat.BigEndian)]
    [InlineData(LengthFormat.LittleEndian)]
    [InlineData(LengthFormat.Compressed)]
    [Theory]
    public static void EncodeString(LengthFormat format)
    {
        ReadOnlySpan<char> expected = ['a', 'b', 'c'];
        var buffer = new byte[16];

        var writer = new SpanWriter<byte>(buffer);
        True(writer.Encode(expected, Encoding.UTF8, format) > 0);

        var reader = IAsyncBinaryReader.Create(buffer.AsMemory(0, writer.WrittenCount));

        using var actual = reader.Decode(Encoding.UTF8, lengthFormat: format);
        Equal(expected, actual.Span);
    }

    [InlineData(LengthFormat.BigEndian)]
    [InlineData(LengthFormat.LittleEndian)]
    [InlineData(LengthFormat.Compressed)]
    [Theory]
    public static void WriteLengthPrefixedBytes(LengthFormat format)
    {
        ReadOnlySpan<byte> expected = [1, 2, 3];
        var buffer = new byte[expected.Length + 5];
        
        var writer = new SpanWriter<byte>(buffer);
        True(writer.Write(expected, format) > 0);
        
        var reader = IAsyncBinaryReader.Create(buffer.AsMemory(0, writer.WrittenCount));
        Equal(expected, reader.ReadBlock(format).FirstSpan);
    }
    
    private static void EncodeDecodeLeb128<T>(ReadOnlySpan<T> values)
        where T : struct, IBinaryInteger<T>
    {
        Span<byte> buffer = stackalloc byte[Leb128<T>.MaxSizeInBytes];
        var writer = new SpanWriter<byte>(buffer);
        var reader = new SpanReader<byte>(buffer);

        foreach (var expected in values)
        {
            writer.Reset();
            reader.Reset();

            True(writer.WriteLeb128(expected) > 0);
            Equal(expected, reader.ReadLeb128<T>());
        }
    }
    
    [Fact]
    public static void EncodeDecodeInt32() => EncodeDecodeLeb128([0, int.MaxValue, int.MinValue, 0x80, -1]);
    
    [Fact]
    public static void EncodeDecodeInt64() => EncodeDecodeLeb128([0L, long.MaxValue, long.MinValue, 0x80L, -1L]);
}