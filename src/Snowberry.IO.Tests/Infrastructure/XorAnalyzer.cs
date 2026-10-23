using Snowberry.IO.Common.Reader;
using Snowberry.IO.Common.Reader.Interfaces;

namespace Snowberry.IO.Tests.Infrastructure;

/// <summary>
/// An <see cref="Analyzer"/> that decrypts every read byte with a single-byte XOR key.
/// </summary>
/// <param name="key">The XOR key.</param>
internal sealed class XorAnalyzer(byte key) : Analyzer
{
    public override void AnalyzeReadBytes(IEndianReader reader, Span<byte> buffer, int amount)
    {
        for (int i = 0; i < amount; i++)
            buffer[i] ^= key;
    }

    public override void Initialize(IEndianReader reader)
    {
    }
}