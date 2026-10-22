using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Snowberry.IO.Benchmarks.Infrastructure;
using Snowberry.IO.Common;

namespace Snowberry.IO.Benchmarks.Common;

/// <summary>
/// Measures <see cref="BinaryEndianConverter"/> against <see cref="BinaryPrimitives"/> by converting a buffer of
/// <typeparamref name="T"/> values in the byte order given by <see cref="Endian"/>.
/// </summary>
/// <typeparam name="T">
/// The converted type: <see cref="short"/>, <see cref="int"/>, <see cref="long"/>, <see cref="float"/>, <see cref="double"/> or <see cref="Guid"/>.
/// </typeparam>
[GenericTypeArguments(typeof(short))]
[GenericTypeArguments(typeof(int))]
[GenericTypeArguments(typeof(long))]
[GenericTypeArguments(typeof(float))]
[GenericTypeArguments(typeof(double))]
[GenericTypeArguments(typeof(Guid))]
public class ConvertBenchmarks<T>
    where T : struct
{
    private const int c_ValueCount = 1024;

    private static readonly int s_ValueSize = Unsafe.SizeOf<T>();

    private byte[] _data = [];

    /// <summary>
    /// Converts every value with <see cref="BinaryPrimitives"/>.
    /// </summary>
    /// <returns>A checksum of the converted values.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    public long Bcl()
    {
        if (typeof(T) == typeof(short))
            return Reference.Int16(_data, Endian);

        if (typeof(T) == typeof(int))
            return Reference.Int32(_data, Endian);

        if (typeof(T) == typeof(long))
            return Reference.Int64(_data, Endian);

        if (typeof(T) == typeof(float))
            return Reference.Single(_data, Endian);

        if (typeof(T) == typeof(double))
            return Reference.Double(_data, Endian);

        return Reference.Guid(_data, Endian);
    }

    /// <summary>
    /// Converts every value with the <see cref="BinaryEndianConverter"/> overloads that take a slice of the buffer.
    /// </summary>
    /// <remarks><see cref="Guid"/> has no slice overload, so it uses <see cref="BinaryEndianConverter.ToGuid(ReadOnlySpan{byte}, int, EndianType)"/> with an offset of 0.</remarks>
    /// <returns>A checksum of the converted values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    public long Converter()
    {
        if (typeof(T) == typeof(short))
            return Library.Int16(_data, Endian);

        if (typeof(T) == typeof(int))
            return Library.Int32(_data, Endian);

        if (typeof(T) == typeof(long))
            return Library.Int64(_data, Endian);

        if (typeof(T) == typeof(float))
            return Library.Single(_data, Endian);

        if (typeof(T) == typeof(double))
            return Library.Double(_data, Endian);

        return Library.Guid(_data, Endian);
    }

    /// <summary>
    /// Converts every value with the <see cref="BinaryEndianConverter"/> overloads that take an offset into the whole buffer.
    /// </summary>
    /// <returns>A checksum of the converted values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    public long ConverterOffset()
    {
        if (typeof(T) == typeof(short))
            return LibraryOffset.Int16(_data, Endian);

        if (typeof(T) == typeof(int))
            return LibraryOffset.Int32(_data, Endian);

        if (typeof(T) == typeof(long))
            return LibraryOffset.Int64(_data, Endian);

        if (typeof(T) == typeof(float))
            return LibraryOffset.Single(_data, Endian);

        if (typeof(T) == typeof(double))
            return LibraryOffset.Double(_data, Endian);

        return LibraryOffset.Guid(_data, Endian);
    }

    /// <summary>
    /// Fills the source buffer with random values.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _data = BenchmarkData.RandomBytes(c_ValueCount * s_ValueSize);
    }

    /// <summary>
    /// Gets or sets the byte order of the converted values.
    /// </summary>
    [Params(EndianType.LITTLE, EndianType.BIG)]
    public EndianType Endian { get; set; }

    private static class Library
    {
        public static long Double(ReadOnlySpan<byte> data, EndianType endian)
        {
            double sum = 0;

            for (int i = 0; i < data.Length; i += sizeof(double))
                sum += BinaryEndianConverter.ToDouble(data[i..], endian);

            return BitConverter.DoubleToInt64Bits(sum);
        }

        public static long Guid(ReadOnlySpan<byte> data, EndianType endian)
        {
            long sum = 0;

            for (int i = 0; i < data.Length; i += 16)
                sum += BinaryEndianConverter.ToGuid(data.Slice(i, 16), 0, endian).GetHashCode();

            return sum;
        }

        public static long Int16(ReadOnlySpan<byte> data, EndianType endian)
        {
            long sum = 0;

            for (int i = 0; i < data.Length; i += sizeof(short))
                sum += BinaryEndianConverter.ToInt16(data[i..], endian);

            return sum;
        }

        public static long Int32(ReadOnlySpan<byte> data, EndianType endian)
        {
            long sum = 0;

            for (int i = 0; i < data.Length; i += sizeof(int))
                sum += BinaryEndianConverter.ToInt32(data[i..], endian);

            return sum;
        }

        public static long Int64(ReadOnlySpan<byte> data, EndianType endian)
        {
            long sum = 0;

            for (int i = 0; i < data.Length; i += sizeof(long))
                sum += BinaryEndianConverter.ToInt64(data[i..], endian);

            return sum;
        }

        public static long Single(ReadOnlySpan<byte> data, EndianType endian)
        {
            double sum = 0;

            for (int i = 0; i < data.Length; i += sizeof(float))
                sum += BinaryEndianConverter.ToFloat(data[i..], endian);

            return BitConverter.DoubleToInt64Bits(sum);
        }
    }

    private static class LibraryOffset
    {
        public static long Double(ReadOnlySpan<byte> data, EndianType endian)
        {
            double sum = 0;

            for (int i = 0; i < data.Length; i += sizeof(double))
                sum += BinaryEndianConverter.ToDouble(data, i, endian);

            return BitConverter.DoubleToInt64Bits(sum);
        }

        public static long Guid(ReadOnlySpan<byte> data, EndianType endian)
        {
            long sum = 0;

            for (int i = 0; i < data.Length; i += 16)
                sum += BinaryEndianConverter.ToGuid(data, i, endian).GetHashCode();

            return sum;
        }

        public static long Int16(ReadOnlySpan<byte> data, EndianType endian)
        {
            long sum = 0;

            for (int i = 0; i < data.Length; i += sizeof(short))
                sum += BinaryEndianConverter.ToInt16(data, i, endian);

            return sum;
        }

        public static long Int32(ReadOnlySpan<byte> data, EndianType endian)
        {
            long sum = 0;

            for (int i = 0; i < data.Length; i += sizeof(int))
                sum += BinaryEndianConverter.ToInt32(data, i, endian);

            return sum;
        }

        public static long Int64(ReadOnlySpan<byte> data, EndianType endian)
        {
            long sum = 0;

            for (int i = 0; i < data.Length; i += sizeof(long))
                sum += BinaryEndianConverter.ToInt64(data, i, endian);

            return sum;
        }

        public static long Single(ReadOnlySpan<byte> data, EndianType endian)
        {
            double sum = 0;

            for (int i = 0; i < data.Length; i += sizeof(float))
                sum += BinaryEndianConverter.ToFloat(data, i, endian);

            return BitConverter.DoubleToInt64Bits(sum);
        }
    }

    private static class Reference
    {
        public static long Double(ReadOnlySpan<byte> data, EndianType endian)
        {
            double sum = 0;

            for (int i = 0; i < data.Length; i += sizeof(double))
            {
#if NET5_0_OR_GREATER
                sum += endian == EndianType.LITTLE ? BinaryPrimitives.ReadDoubleLittleEndian(data[i..]) : BinaryPrimitives.ReadDoubleBigEndian(data[i..]);
#else
                long bits = endian == EndianType.LITTLE ? BinaryPrimitives.ReadInt64LittleEndian(data[i..]) : BinaryPrimitives.ReadInt64BigEndian(data[i..]);
                sum += BitConverter.Int64BitsToDouble(bits);
#endif
            }

            return BitConverter.DoubleToInt64Bits(sum);
        }

        public static long Guid(ReadOnlySpan<byte> data, EndianType endian)
        {
            long sum = 0;

            for (int i = 0; i < data.Length; i += 16)
                sum += BenchmarkData.ReadGuid(data.Slice(i, 16), endian).GetHashCode();

            return sum;
        }

        public static long Int16(ReadOnlySpan<byte> data, EndianType endian)
        {
            long sum = 0;

            for (int i = 0; i < data.Length; i += sizeof(short))
                sum += endian == EndianType.LITTLE ? BinaryPrimitives.ReadInt16LittleEndian(data[i..]) : BinaryPrimitives.ReadInt16BigEndian(data[i..]);

            return sum;
        }

        public static long Int32(ReadOnlySpan<byte> data, EndianType endian)
        {
            long sum = 0;

            for (int i = 0; i < data.Length; i += sizeof(int))
                sum += endian == EndianType.LITTLE ? BinaryPrimitives.ReadInt32LittleEndian(data[i..]) : BinaryPrimitives.ReadInt32BigEndian(data[i..]);

            return sum;
        }

        public static long Int64(ReadOnlySpan<byte> data, EndianType endian)
        {
            long sum = 0;

            for (int i = 0; i < data.Length; i += sizeof(long))
                sum += endian == EndianType.LITTLE ? BinaryPrimitives.ReadInt64LittleEndian(data[i..]) : BinaryPrimitives.ReadInt64BigEndian(data[i..]);

            return sum;
        }

        public static long Single(ReadOnlySpan<byte> data, EndianType endian)
        {
            double sum = 0;

            for (int i = 0; i < data.Length; i += sizeof(float))
            {
#if NET5_0_OR_GREATER
                sum += endian == EndianType.LITTLE ? BinaryPrimitives.ReadSingleLittleEndian(data[i..]) : BinaryPrimitives.ReadSingleBigEndian(data[i..]);
#else
                int bits = endian == EndianType.LITTLE ? BinaryPrimitives.ReadInt32LittleEndian(data[i..]) : BinaryPrimitives.ReadInt32BigEndian(data[i..]);
                sum += Unsafe.As<int, float>(ref bits);
#endif
            }

            return BitConverter.DoubleToInt64Bits(sum);
        }
    }
}