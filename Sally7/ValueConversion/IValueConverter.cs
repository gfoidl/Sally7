using System;
using System.Buffers.Binary;

namespace Sally7.ValueConversion;

#if NET7_0_OR_GREATER
public interface IValueConverter<TValue>
{
    static abstract int ToS7(TValue? value, int length, Span<byte> output);
    static abstract void FromS7(ref TValue? value, ReadOnlySpan<byte> input, int length);
}

internal readonly struct LongConverter : IValueConverter<long>
{
    public static int ToS7(long value, int length, Span<byte> output)
    {
        BinaryPrimitives.WriteInt64BigEndian(output, value);

        return sizeof(long);
    }

    public static void FromS7(ref long value, ReadOnlySpan<byte> input, int length)
        => value = BinaryPrimitives.ReadInt64BigEndian(input);
}

internal readonly struct IntConverter : IValueConverter<int>
{
    public static int ToS7(int value, int length, Span<byte> output)
    {
        BinaryPrimitives.WriteInt32BigEndian(output, value);

        return sizeof(int);
    }

    public static void FromS7(ref int value, ReadOnlySpan<byte> input, int length)
        => value = BinaryPrimitives.ReadInt32BigEndian(input);
}

internal readonly struct ShortConverter : IValueConverter<short>
{
    public static int ToS7(short value, int length, Span<byte> output)
    {
        BinaryPrimitives.WriteInt16BigEndian(output, value);

        return sizeof(short);
    }

    public static void FromS7(ref short value, ReadOnlySpan<byte> input, int length)
        => value = BinaryPrimitives.ReadInt16BigEndian(input);
}

internal readonly struct ByteConverter : IValueConverter<byte>
{
    public static int ToS7(byte value, int length, Span<byte> output)
    {
        output[0] = value;

        return sizeof(byte);
    }

    public static void FromS7(ref byte value, ReadOnlySpan<byte> input, int length)
        => value = input[0];
}
#endif