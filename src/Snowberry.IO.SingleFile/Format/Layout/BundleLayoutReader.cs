using System.Buffers;
using Snowberry.IO.Common;
using Snowberry.IO.SingleFile.Format.Hosts;
using Snowberry.IO.SingleFile.Format.Manifests;
using Snowberry.IO.SingleFile.Sources;

namespace Snowberry.IO.SingleFile.Format.Layout;

/// <summary>
/// Locates and validates the bundle inside an application file.
/// </summary>
internal static class BundleLayoutReader
{
    // Deflate cannot expand data by more than about 1032:1, so a larger declared size is invalid.
    private const long c_MaxDeflateRatio = 1032;

    // Sized so the rented buffer (chunk plus overlap) stays in the 1 MiB pool bucket.
    private const int c_SearchChunkSize = (1024 * 1024) - (BundleFormat.c_SignatureLength - 1);

    /// <summary>
    /// Reads and validates the bundle layout of <paramref name="source"/>.
    /// </summary>
    /// <param name="useAsync"><see langword="true"/> to use asynchronous I/O; <see langword="false"/> to complete synchronously.</param>
    /// <param name="source">The source to read the bundle from.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The layout of the bundle.</returns>
    /// <exception cref="InvalidDataException">The source is not a valid single-file bundle.</exception>
    public static async ValueTask<BundleLayout> ReadAsync(bool useAsync, BundleSource source, CancellationToken cancellationToken)
    {
        var (host, signatureOffset, manifestOffset) = await ReadMarkerAsync(
            useAsync, source, "The file is not a single-file bundle: the bundle signature was not found.", cancellationToken).ConfigureAwait(false);

        if (manifestOffset == 0)
            throw new InvalidDataException("The file is an application host without a bundle.");

        long dataStart = signatureOffset + BundleFormat.c_SignatureLength;

        if (manifestOffset < dataStart || manifestOffset >= source.Length)
            throw new InvalidDataException($"The bundle manifest offset {manifestOffset} is outside of the file.");

        var manifest = await ManifestSerializer.ReadAsync(useAsync, source, manifestOffset, cancellationToken).ConfigureAwait(false);

        // An image end beyond the manifest means the host headers could not be interpreted; ignore it then.
        long hostEnd = host.ImageEnd <= manifestOffset ? Math.Max(host.ImageEnd, dataStart) : dataStart;
        long bundleOffset = manifestOffset;

        foreach (var entry in manifest.Entries)
        {
            if (entry.Offset < hostEnd || entry.Size < 0 || entry.CompressedSize < 0)
                throw new InvalidDataException($"The bundle entry '{entry.RelativePath}' has an invalid location.");

            if (entry.CompressedSize > 0 && entry.Size > (entry.CompressedSize * c_MaxDeflateRatio) + 64)
                throw new InvalidDataException($"The bundle entry '{entry.RelativePath}' declares a size that its compressed data cannot produce.");

            long storedLength = entry.CompressedSize > 0 ? entry.CompressedSize : entry.Size;

            if (entry.Offset > manifestOffset - storedLength)
                throw new InvalidDataException($"The data of bundle entry '{entry.RelativePath}' extends past the start of the manifest.");

            bundleOffset = Math.Min(bundleOffset, entry.Offset);
        }

        EnsureNoOverlap(manifest.Entries);

        return new BundleLayout(signatureOffset, manifestOffset, bundleOffset, InferAssemblyAlignment(manifest, host), host, manifest);
    }

    /// <summary>
    /// Reads the layout of an application host that does not contain a bundle yet, as the .NET SDK bundler expects it: the bundle signature is
    /// present and the manifest offset before it is <c>0</c>.
    /// </summary>
    /// <remarks>
    /// The bundle starts at the end of the source, where the .NET SDK starts it, or at the code signature of a signed host, which saving then
    /// removes like it does for an opened bundle: the Authenticode certificate table of a PE host, or the code signature of a macOS host.
    /// </remarks>
    /// <param name="useAsync"><see langword="true"/> to use asynchronous I/O; <see langword="false"/> to complete synchronously.</param>
    /// <param name="source">The source to read the application host from.</param>
    /// <param name="manifest">The manifest of the new bundle, without entries.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The layout of an empty bundle after the application host.</returns>
    /// <exception cref="InvalidDataException">
    /// The source is not an application host without a bundle, or the host is not supported: a universal macOS host, or a macOS host whose load
    /// commands do not have the layout the .NET SDK requires.
    /// </exception>
    public static async ValueTask<BundleLayout> ReadHostTemplateAsync(bool useAsync, BundleSource source, Manifest manifest, CancellationToken cancellationToken)
    {
        var (host, signatureOffset, manifestOffset) = await ReadMarkerAsync(
            useAsync, source, "The file is not an application host for a single-file bundle: the bundle signature was not found.", cancellationToken).ConfigureAwait(false);

        if (manifestOffset != 0)
            throw new InvalidDataException("The application host already contains a bundle.");

        long hostEnd = Math.Max(signatureOffset + BundleFormat.c_SignatureLength, host.ImageEnd);
        long bundleOffset;

        if (host.Kind == HostKind.MachO)
        {
            var machO = host.MachO ?? throw new InvalidDataException("The load commands of the macOS application host do not have the layout the .NET SDK requires.");
            bundleOffset = machO.CodeSignatureCommandOffset >= 0 ? machO.UnsignedEnd : source.Length;

            if (bundleOffset < hostEnd || bundleOffset > source.Length)
                throw new InvalidDataException("The code signature of the macOS application host is located at an invalid offset.");
        }
        else
        {
            // A certificate table inside the image is invalid; the bundle then follows the whole host.
            bundleOffset = host.SecurityOffset >= hostEnd ? Math.Min(host.SecurityOffset, source.Length) : source.Length;
        }

        return new BundleLayout(signatureOffset, 0, bundleOffset, host.AssemblyAlignment, host, manifest);
    }

    /// <summary>
    /// Verifies that no two entries share stored bytes, so a small file cannot make extraction write the same data many times.
    /// </summary>
    /// <remarks>
    /// Empty entries are skipped: the .NET SDK places them at the offset of the next entry. Bundles written by the SDK store their entries in
    /// offset order, which is checked without sorting.
    /// </remarks>
    /// <exception cref="InvalidDataException">The stored data of two entries overlaps.</exception>
    private static void EnsureNoOverlap(List<ManifestEntry> entries)
    {
        if (IsInOffsetOrderWithoutOverlap(entries))
            return;

        var ranges = new List<(long Offset, long Length, string Path)>(entries.Count);

        foreach (var entry in entries)
        {
            long storedLength = entry.CompressedSize > 0 ? entry.CompressedSize : entry.Size;

            if (storedLength > 0)
                ranges.Add((entry.Offset, storedLength, entry.RelativePath));
        }

        ranges.Sort((a, b) => a.Offset.CompareTo(b.Offset));

        // The ranges end before the manifest, so the sums cannot overflow.
        for (int i = 1; i < ranges.Count; i++)
        {
            var previous = ranges[i - 1];

            if (previous.Offset + previous.Length > ranges[i].Offset)
                throw new InvalidDataException($"The data of bundle entries '{previous.Path}' and '{ranges[i].Path}' overlaps.");
        }
    }

    /// <summary>
    /// Finds the bundle signature, searching the writable data of the host first.
    /// </summary>
    /// <remarks>
    /// The .NET host keeps the signature and the manifest offset in a static variable that the SDK patches, so every SDK host stores them in
    /// writable, initialized data: a writable PE section, a writable ELF <c>PT_LOAD</c> segment or a writable Mach-O segment. The first occurrence
    /// in those ranges, in file order, is used; only if they do not contain the signature is the whole file searched for its first occurrence.
    /// The result differs from searching the whole file only for a file with another copy of the signature before the one in writable data.
    /// </remarks>
    /// <param name="useAsync"><see langword="true"/> to use asynchronous I/O; <see langword="false"/> to complete synchronously.</param>
    /// <param name="source">The source to search.</param>
    /// <param name="writableRanges">The writable data of the host, ordered by offset.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// The offset of the signature, or <c>-1</c> if it was not found, and the manifest offset stored before it when the search already read it.
    /// </returns>
    private static async ValueTask<(long Offset, long? ManifestOffset)> FindSignatureAsync(
        bool useAsync,
        BundleSource source,
        IReadOnlyList<(long Offset, long Length)> writableRanges,
        CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(c_SearchChunkSize + BundleFormat.c_SignatureLength - 1);

        try
        {
            foreach (var (offset, length) in writableRanges)
            {
                long start = Math.Clamp(offset, 0, source.Length);
                long end = length > source.Length - start ? source.Length : start + length;
                var found = await SearchAsync(useAsync, source, start, end, buffer, cancellationToken).ConfigureAwait(false);

                if (found.Offset >= 0)
                    return found;
            }

            return await SearchAsync(useAsync, source, 0, source.Length, buffer, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Uses the alignment of the existing uncompressed assemblies, so saving an unchanged bundle keeps its layout; falls back to the host's alignment.
    /// </summary>
    private static int InferAssemblyAlignment(Manifest manifest, HostImage host)
    {
        // The .NET SDK has used 16, 64, 4096 and 16384 bytes; a smaller common divisor means the assemblies were not aligned.
        const long c_MinInferredAlignment = 16;
        const long c_MaxInferredAlignment = 16 * 1024;

        long combined = 0;

        foreach (var entry in manifest.Entries)
        {
            if (entry.Type == BundleFileType.Assembly && entry.CompressedSize == 0)
                combined |= entry.Offset;
        }

        if (combined == 0)
            return host.AssemblyAlignment;

        long alignment = Math.Min(combined & -combined, c_MaxInferredAlignment);
        return alignment >= c_MinInferredAlignment ? (int)alignment : host.AssemblyAlignment;
    }

    /// <summary>
    /// Determines whether the stored data of the entries follows each other in manifest order without overlapping.
    /// </summary>
    /// <returns><see langword="true"/> if every entry starts at or after the end of the previous one; otherwise, <see langword="false"/>.</returns>
    private static bool IsInOffsetOrderWithoutOverlap(List<ManifestEntry> entries)
    {
        long previousEnd = 0;

        foreach (var entry in entries)
        {
            long storedLength = entry.CompressedSize > 0 ? entry.CompressedSize : entry.Size;

            if (storedLength == 0)
                continue;

            if (entry.Offset < previousEnd)
                return false;

            // The ranges end before the manifest, so the sum cannot overflow.
            previousEnd = entry.Offset + storedLength;
        }

        return true;
    }

    /// <summary>
    /// Reads and parses the start of the file, which holds the headers of the application host.
    /// </summary>
    private static async ValueTask<HostImage> ReadHostAsync(bool useAsync, BundleSource source, CancellationToken cancellationToken)
    {
        int length = (int)Math.Min(source.Length, HostImage.c_HeaderLength);
        byte[] header = ArrayPool<byte>.Shared.Rent(length);

        try
        {
            await source.ReadExactlyAsync(useAsync, 0, header.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
            return HostImage.Parse(header, length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(header);
        }
    }

    private static async ValueTask<long> ReadManifestOffsetAsync(bool useAsync, BundleSource source, long offset, CancellationToken cancellationToken)
    {
        byte[] bytes = new byte[BundleFormat.c_ManifestOffsetLength];
        await source.ReadExactlyAsync(useAsync, offset, bytes, cancellationToken).ConfigureAwait(false);
        return BinaryEndianConverter.ToInt64(bytes, EndianType.LITTLE);
    }

    /// <summary>
    /// Reads the application host and the bundle marker in it: the bundle signature and the manifest offset before it.
    /// </summary>
    /// <param name="useAsync"><see langword="true"/> to use asynchronous I/O; <see langword="false"/> to complete synchronously.</param>
    /// <param name="source">The source to read.</param>
    /// <param name="notFoundMessage">The message of the exception thrown when the signature is not found.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The parsed host, the offset of the signature and the stored manifest offset, which is <c>0</c> for a host without a bundle.</returns>
    /// <exception cref="InvalidDataException">The host is a universal macOS executable, or the signature is missing or at an invalid offset.</exception>
    private static async ValueTask<(HostImage Host, long SignatureOffset, long ManifestOffset)> ReadMarkerAsync(
        bool useAsync,
        BundleSource source,
        string notFoundMessage,
        CancellationToken cancellationToken)
    {
        var host = await ReadHostAsync(useAsync, source, cancellationToken).ConfigureAwait(false);

        if (host.Kind == HostKind.UniversalMachO)
            throw new InvalidDataException("Universal (fat) macOS executables are not supported.");

        var (signatureOffset, storedManifestOffset) = await FindSignatureAsync(useAsync, source, host.WritableRanges, cancellationToken).ConfigureAwait(false);

        if (signatureOffset < 0)
            throw new InvalidDataException(notFoundMessage);

        if (signatureOffset < BundleFormat.c_ManifestOffsetLength)
            throw new InvalidDataException("The bundle signature is located at an invalid offset.");

        long manifestOffset = storedManifestOffset
            ?? await ReadManifestOffsetAsync(useAsync, source, signatureOffset - BundleFormat.c_ManifestOffsetLength, cancellationToken).ConfigureAwait(false);

        return (host, signatureOffset, manifestOffset);
    }

    /// <summary>
    /// Finds the first occurrence of the bundle signature that lies entirely within a range of the source.
    /// </summary>
    /// <returns>
    /// The offset of the signature, or <c>-1</c> if it was not found, and the manifest offset stored before it if the chunk that contained the
    /// signature also contained it.
    /// </returns>
    private static async ValueTask<(long Offset, long? ManifestOffset)> SearchAsync(
        bool useAsync,
        BundleSource source,
        long start,
        long end,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        long position = start;
        int carried = 0;

        while (position < end)
        {
            int toRead = (int)Math.Min(c_SearchChunkSize, end - position);
            await source.ReadExactlyAsync(useAsync, position, buffer.AsMemory(carried, toRead), cancellationToken).ConfigureAwait(false);

            int available = carried + toRead;
            int index = buffer.AsSpan(0, available).IndexOf(BundleFormat.Signature);

            if (index >= 0)
            {
                long? manifestOffset = index >= BundleFormat.c_ManifestOffsetLength
                    ? BinaryEndianConverter.ToInt64(buffer, index - BundleFormat.c_ManifestOffsetLength, EndianType.LITTLE)
                    : null;

                return (position - carried + index, manifestOffset);
            }

            // Keep the last bytes so a signature that spans two chunks is still found.
            carried = Math.Min(BundleFormat.c_SignatureLength - 1, available);
            buffer.AsSpan(available - carried, carried).CopyTo(buffer);
            position += toRead;
        }

        return (-1, null);
    }
}