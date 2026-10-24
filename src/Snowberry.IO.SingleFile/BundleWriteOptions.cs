using System.IO.Compression;

namespace Snowberry.IO.SingleFile;

/// <summary>
/// Options for saving a <see cref="SingleFileBundle"/>.
/// </summary>
/// <remarks>
/// Values are validated when they are set. Derive variations from <see cref="Default"/> with a <see langword="with"/> expression.
/// </remarks>
public sealed record BundleWriteOptions
{
    private readonly int? _assemblyAlignment;
    private readonly BundleCompression _compression = BundleCompression.Preserve;
    private readonly CompressionLevel _compressionLevel = CompressionLevel.SmallestSize;
    private readonly double _compressionThreshold = 0.75;
    private readonly string? _macOSCodeSignIdentifier;
    private readonly long _maxCompressionMemory = 64L * 1024 * 1024;
    private readonly int _maxDegreeOfParallelism = -1;

    /// <summary>
    /// Gets or initializes the alignment, in bytes, of uncompressed <see cref="BundleFileType.Assembly"/> entries.
    /// </summary>
    /// <remarks>
    /// When <see langword="null"/> (the default), the alignment of the opened bundle's uncompressed assemblies is kept, so an unchanged bundle
    /// keeps its layout. A bundle without uncompressed assemblies uses the alignment the current .NET SDK uses for its application host:
    /// 4096 bytes for Windows (PE) and Arm64 hosts, 16384 bytes for LoongArch64 hosts, 64 bytes for other Linux and macOS hosts, and
    /// 4096 bytes when the host format is not recognized.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not <see langword="null"/> or a positive power of two.</exception>
    public int? AssemblyAlignment
    {
        get => _assemblyAlignment;
        init
        {
            if (value is int alignment && (alignment <= 0 || (alignment & (alignment - 1)) != 0))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The assembly alignment must be a positive power of two.");

            _assemblyAlignment = value;
        }
    }

    /// <summary>
    /// Gets or initializes how entries are stored. Defaults to <see cref="BundleCompression.Preserve"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="BundleCompression"/> value.</exception>
    public BundleCompression Compression
    {
        get => _compression;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, $"Unknown {nameof(BundleCompression)} value.");

            _compression = value;
        }
    }

    /// <summary>
    /// Gets or initializes the compression level used for entries that are compressed while saving.
    /// Defaults to <see cref="CompressionLevel.SmallestSize"/>, the level the .NET SDK uses.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="System.IO.Compression.CompressionLevel"/> value.</exception>
    public CompressionLevel CompressionLevel
    {
        get => _compressionLevel;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, $"Unknown {nameof(System.IO.Compression.CompressionLevel)} value.");

            _compressionLevel = value;
        }
    }

    /// <summary>
    /// Gets or initializes the ratio of compressed to uncompressed size below which compressed data is kept.
    /// Defaults to <c>0.75</c>, the threshold the .NET SDK uses.
    /// </summary>
    /// <remarks>
    /// Compressed data is kept only when it is smaller than the uncompressed size multiplied by this value; otherwise the entry is stored
    /// uncompressed. <see cref="double.PositiveInfinity"/> keeps every compressed entry. Empty entries are always stored uncompressed.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not greater than <c>0</c>.</exception>
    public double CompressionThreshold
    {
        get => _compressionThreshold;
        init
        {
            if (!(value > 0))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The compression threshold must be greater than zero.");

            _compressionThreshold = value;
        }
    }

    /// <summary>
    /// Gets the default options.
    /// </summary>
    public static BundleWriteOptions Default { get; } = new();

    /// <summary>
    /// Gets a value indicating whether a macOS executable gets an ad-hoc code signature when it is saved, as the .NET SDK signs
    /// it by default. Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Saving a macOS executable always removes its current code signature, which the changed bundle invalidates, and extends its
    /// <c>__LINKEDIT</c> segment over the bundle. When this is <see langword="true"/>, a new ad-hoc signature of SHA-256 page hashes is
    /// appended, which Apple Silicon Macs require to run the executable; when <see langword="false"/>, the executable is left unsigned, for
    /// example to sign it with <c>codesign</c> and a certificate afterwards.
    /// </remarks>
    public bool MacOSCodeSign { get; init; } = true;

    /// <summary>
    /// Gets or initializes the identifier of the ad-hoc code signature of a macOS executable.
    /// </summary>
    /// <remarks>
    /// When <see langword="null"/> (the default), saving to a file uses the file name, as the .NET SDK does, and saving an opened bundle to a
    /// stream keeps the identifier of the executable's current signature. Saving to a stream throws <see cref="InvalidOperationException"/>
    /// when the executable is not signed or the bundle was created, because the signature of a template names the template, not the
    /// application.
    /// </remarks>
    /// <exception cref="ArgumentException">The value is empty or contains a null character.</exception>
    public string? MacOSCodeSignIdentifier
    {
        get => _macOSCodeSignIdentifier;
        init
        {
            if (value != null && (value.Length == 0 || value.Contains('\0')))
                throw new ArgumentException("The code signing identifier must not be empty or contain null characters.", nameof(value));

            _macOSCodeSignIdentifier = value;
        }
    }

    /// <summary>
    /// Gets or initializes the number of bytes of compressed data that may be held in memory while saving.
    /// </summary>
    /// <remarks>
    /// Entries compressed while saving are compressed before the bundle is written. Their compressed data is kept in pooled memory blocks of
    /// 16 KiB to 1 MiB while the blocks fit in this many bytes; beyond it, compressed data is buffered in temporary files that are deleted when
    /// saving completes. Entries decompressed while saving are decompressed ahead of the writer, in parallel, while their data fits in the same
    /// memory, and otherwise when they are written. Defaults to 64 MiB; <c>0</c> buffers all compressed data in temporary files and decompresses
    /// entries only when they are written.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public long MaxCompressionMemory
    {
        get => _maxCompressionMemory;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _maxCompressionMemory = value;
        }
    }

    /// <summary>
    /// Gets or initializes the maximum number of entries that are compressed, decompressed or hashed concurrently while saving.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>-1</c>, which processes up to <see cref="Environment.ProcessorCount"/> entries at a time. Concurrent work runs on the thread
    /// pool, also for the synchronous save methods, alongside the thread that writes the bundle. With <c>1</c>, the save does one thing at a time.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is <c>0</c> or less than <c>-1</c>.</exception>
    public int MaxDegreeOfParallelism
    {
        get => _maxDegreeOfParallelism;
        init
        {
            if (value is 0 or < -1)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The maximum degree of parallelism must be -1 or greater than zero.");

            _maxDegreeOfParallelism = value;
        }
    }
}