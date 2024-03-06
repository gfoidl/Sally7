using System;
using Sally7.Protocol;
using Sally7.Protocol.S7;
using Sally7.ValueConversion;

namespace Sally7
{
    public interface IDataItem
    {
        Area Area { get; }
        BigEndianShort DbNumber { get; }
        Address Address { get; }
        BigEndianShort ReadCount { get; }

        TransportSize TransportSize { get; }
        VariableType VariableType { get; }

        int WriteValue(Span<byte> output);
        void ReadValue(ReadOnlySpan<byte> input);
    }

    public interface IDataItem<TValue> : IDataItem
    {
        TValue? Value { get; set; }
    }

#if NET7_0_OR_GREATER
    public interface IDataItem<TValue, TValueConverter> : IDataItem<TValue>
        where TValueConverter : IValueConverter<TValue>
    {
    }
#endif
}