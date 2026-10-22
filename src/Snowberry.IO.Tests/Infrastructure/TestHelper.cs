using System.Text;
using AwesomeAssertions;
using Snowberry.IO.Common.Reader;
using Snowberry.IO.Common.Reader.Interfaces;
using Snowberry.IO.Common.Writer.Interfaces;
using Snowberry.IO.Reader;
using Snowberry.IO.Writer;

namespace Snowberry.IO.Tests.Infrastructure;

/// <summary>
/// Creates readers and writers over in-memory data for the tests.
/// </summary>
internal static class TestHelper
{
    /// <summary>
    /// Creates an <see cref="EndianStreamReader"/> over a copy of <paramref name="data"/>.
    /// </summary>
    /// <param name="data">The bytes to read.</param>
    /// <param name="encoding">The text encoding; the reader's default encoding when <see langword="null"/>.</param>
    /// <param name="analyzer">The analyzer to attach, if any.</param>
    /// <returns>A reader positioned at the start of <paramref name="data"/>.</returns>
    public static EndianStreamReader CreateReader(byte[] data, Encoding? encoding = null, Analyzer? analyzer = null)
    {
        return CreateReader(new MemoryStream(data), encoding, analyzer);
    }

    /// <summary>
    /// Creates an <see cref="EndianStreamReader"/> over <paramref name="stream"/>.
    /// </summary>
    /// <param name="stream">The stream to read.</param>
    /// <param name="encoding">The text encoding; the reader's default encoding when <see langword="null"/>.</param>
    /// <param name="analyzer">The analyzer to attach, if any.</param>
    /// <returns>A reader that owns <paramref name="stream"/>.</returns>
    public static EndianStreamReader CreateReader(Stream stream, Encoding? encoding = null, Analyzer? analyzer = null)
    {
        return encoding is null
            ? new EndianStreamReader(stream, analyzer)
            : new EndianStreamReader(stream, analyzer, 0, encoding);
    }

    /// <summary>
    /// Creates <paramref name="length"/> random bytes from a fixed seed.
    /// </summary>
    /// <param name="length">The number of bytes.</param>
    /// <param name="seed">The seed of the random number generator.</param>
    /// <returns>The same bytes for the same arguments.</returns>
    public static byte[] RandomBytes(int length, int seed = 42)
    {
        byte[] data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    /// <summary>
    /// Creates <paramref name="length"/> bytes whose values are their index, wrapped at 256.
    /// </summary>
    /// <param name="length">The number of bytes.</param>
    /// <returns>The bytes <c>0, 1, 2, ...</c>.</returns>
    public static byte[] SequentialBytes(int length)
    {
        byte[] data = new byte[length];

        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)i;

        return data;
    }

    /// <summary>
    /// Writes data with a <see cref="BinaryWriter"/> and returns the bytes it produced.
    /// </summary>
    /// <param name="write">Writes the data.</param>
    /// <param name="encoding">The text encoding; UTF-8 when <see langword="null"/>.</param>
    /// <returns>The written bytes.</returns>
    public static byte[] WriteBinaryWriterBytes(Action<BinaryWriter> write, Encoding? encoding = null)
    {
        var stream = new MemoryStream();

        using (var writer = new BinaryWriter(stream, encoding ?? Encoding.UTF8, leaveOpen: true))
            write(writer);

        return stream.ToArray();
    }

    /// <summary>
    /// Writes data with an <see cref="EndianStreamWriter"/> and returns the bytes it produced.
    /// </summary>
    /// <param name="write">Writes the data.</param>
    /// <param name="encoding">The text encoding; the writer's default encoding when <see langword="null"/>.</param>
    /// <returns>The written bytes.</returns>
    public static byte[] WriteBytes(Action<IEndianWriter> write, Encoding? encoding = null)
    {
        var stream = new MemoryStream();

        using (var writer = encoding is null ? new EndianStreamWriter(stream, true) : new EndianStreamWriter(stream, true, encoding))
            write(writer);

        return stream.ToArray();
    }

    /// <summary>
    /// Writes data through the members of the concrete <see cref="EndianStreamWriter"/>, including the ones it inherits from
    /// <see cref="BinaryWriter"/>, and returns the bytes it produced.
    /// </summary>
    /// <param name="write">Writes the data.</param>
    /// <returns>The written bytes.</returns>
    public static byte[] WriteEndianStreamWriterBytes(Action<EndianStreamWriter> write)
    {
        var stream = new MemoryStream();

        using (var writer = new EndianStreamWriter(stream, true, Encoding.UTF8))
            write(writer);

        return stream.ToArray();
    }

    /// <summary>
    /// Writes data with an <see cref="EndianStreamWriter"/>, reads it back with an <see cref="EndianStreamReader"/> and asserts that
    /// <paramref name="read"/> consumed everything that was written.
    /// </summary>
    /// <param name="write">Writes the data.</param>
    /// <param name="read">Reads the data back and asserts on it.</param>
    /// <param name="encoding">The text encoding of the writer and the reader; UTF-8 when <see langword="null"/>.</param>
    public static void WriteThenRead(Action<IEndianWriter> write, Action<IEndianReader> read, Encoding? encoding = null)
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true, encoding ?? Encoding.UTF8);

        write(writer);

        stream.Position = 0;

        using var reader = new EndianStreamReader(stream, null, 0, encoding ?? Encoding.UTF8);

        read(reader);
        reader.ActualPosition.Should().Be(reader.Length);
    }
}