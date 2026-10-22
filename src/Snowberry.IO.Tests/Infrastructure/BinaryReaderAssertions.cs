using System.Reflection;
using AwesomeAssertions;
using Snowberry.IO.Reader;

namespace Snowberry.IO.Tests.Infrastructure;

/// <summary>
/// Contains assertions about the <see cref="BinaryReader"/> base of the Snowberry readers.
/// </summary>
internal static class BinaryReaderAssertions
{
    /// <summary>
    /// Asserts that the only <see cref="Stream"/> that the <see cref="BinaryReader"/> base of <paramref name="reader"/> holds
    /// is the Snowberry reader stream, so every inherited read goes through the region view and the analyzer.
    /// </summary>
    /// <param name="reader">The reader to inspect.</param>
    public static void AssertReadsThroughReaderStream(BinaryReader reader)
    {
        var adapterType = typeof(BaseEndianReader).Assembly.GetType("Snowberry.IO.Reader.Streams.ReaderStream", throwOnError: true)!;

        var streamFields = typeof(BinaryReader)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(f => typeof(Stream).IsAssignableFrom(f.FieldType))
            .ToList();

        streamFields.Should().ContainSingle("BinaryReader is expected to read from exactly one stream field");

        object? value = streamFields[0].GetValue(reader);
        value.Should().NotBeNull();
        adapterType.IsInstanceOfType(value).Should().BeTrue($"the BinaryReader base must read from {adapterType.Name}, but reads from {value!.GetType().FullName}");
    }
}