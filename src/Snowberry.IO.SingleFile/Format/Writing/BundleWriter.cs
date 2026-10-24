using System.Buffers;
using System.IO.Compression;
using System.Security.Cryptography;
using Snowberry.IO.Common;
using Snowberry.IO.SingleFile.Content;
using Snowberry.IO.SingleFile.Format.Hosts;
using Snowberry.IO.SingleFile.Format.Layout;
using Snowberry.IO.SingleFile.Format.Manifests;
using Snowberry.IO.SingleFile.Sources;
using Snowberry.IO.SingleFile.Streams;
using Snowberry.IO.SingleFile.Threading;

namespace Snowberry.IO.SingleFile.Format.Writing;

/// <summary>
/// Writes a bundle in a single forward pass: the layout is planned first, so the destination does not need to be seekable.
/// </summary>
/// <remarks>
/// Entries that are compressed while saving are compressed up front, in parallel, into pooled buffers that stay in memory up to
/// <see cref="BundleWriteOptions.MaxCompressionMemory"/> and are moved to temporary files beyond it, which the buffers of one save share.
/// When content changed, the bundle identifier needs the hash of every entry, but only the manifest at the end contains it: entries that are
/// copied as stored are hashed in the background, in parallel, while the bundle is written. Entries written decompressed are decompressed
/// ahead of the writer, in parallel, within the same memory budget (see <see cref="DecompressionPipeline"/>). With a maximum degree of
/// parallelism of 1, compressed entries are hashed one after another before writing, and uncompressed ones are hashed and compressed ones
/// decompressed while they are written.
/// </remarks>
internal static class BundleWriter
{
    // The output is collected in a buffer of this size, so the destination receives few large writes. Larger writes are not faster, and the
    // shared pool drops its arrays on every full collection under memory pressure, so the buffer is often allocated anew.
    private const int c_StagingBufferSize = 512 * 1024;

    private static readonly Func<PlannedEntry, ManifestEntry> s_ToManifestEntry =
        static p => new ManifestEntry(p.Offset, p.Size, p.CompressedSize, p.Entry.Type, p.Entry.RelativePath);

    /// <summary>
    /// Disposes a plan, waiting for its background hashing asynchronously or synchronously.
    /// </summary>
    /// <param name="useAsync"><see langword="true"/> to wait asynchronously; <see langword="false"/> to block.</param>
    /// <param name="plan">The plan to dispose.</param>
    /// <returns>A task that completes when the plan is disposed.</returns>
    public static ValueTask DisposeAsync(bool useAsync, BundlePlan plan)
    {
        if (useAsync)
            return plan.DisposeAsync();

        plan.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Plans the layout of the bundle: decides how each entry is stored, compresses the entries that are compressed while saving and computes
    /// the offsets and the total length.
    /// </summary>
    /// <param name="useAsync"><see langword="true"/> to use asynchronous I/O; <see langword="false"/> to complete synchronously.</param>
    /// <param name="source">The source the original bundle was read from.</param>
    /// <param name="layout">The layout of the original bundle.</param>
    /// <param name="entries">The entries to write, in order.</param>
    /// <param name="contentChanged"><see langword="true"/> if the content of any entry changed, so the bundle identifier must be recomputed.</param>
    /// <param name="options">The options that control compression, alignment and code signing.</param>
    /// <param name="fileName">
    /// The file name of the destination, which signs a macOS executable when <see cref="BundleWriteOptions.MacOSCodeSignIdentifier"/> is not set,
    /// or <see langword="null"/> when writing to a stream.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the plan, which the caller must dispose.</returns>
    /// <exception cref="InvalidOperationException">
    /// The macOS executable cannot hold the bundle or its code signature, or its signing identifier is not known.
    /// </exception>
    /// <exception cref="NotSupportedException">The bundle of the macOS executable does not lie where the .NET SDK places it.</exception>
    public static async ValueTask<BundlePlan> PlanAsync(
        bool useAsync,
        BundleSource source,
        BundleLayout layout,
        IReadOnlyList<BundleEntry> entries,
        bool contentChanged,
        BundleWriteOptions options,
        string? fileName,
        CancellationToken cancellationToken)
    {
        var header = layout.Manifest.Header;
        bool bundleWasCompressed = layout.Manifest.Entries.Exists(e => e.CompressedSize > 0);
        var compression = options.Compression switch
        {
            BundleCompression.Auto => bundleWasCompressed ? BundleCompression.Compress : BundleCompression.Preserve,
            _ => options.Compression
        };
        bool canCompress = header.SupportsCompression && compression != BundleCompression.NoCompression;
        bool compressChanged = canCompress && (compression == BundleCompression.Compress || bundleWasCompressed);
        int alignment = options.AssemblyAlignment ?? layout.AssemblyAlignment;

        // Signing problems that do not depend on the layout are reported before any entry is compressed.
        var machO = layout.Host.MachO;
        string? identifier = machO == null ? null : await ResolveSigningIdentifierAsync(useAsync, source, layout, machO, options, fileName, cancellationToken).ConfigureAwait(false);

        var compressionMemory = new MemoryBudget(options.MaxCompressionMemory);
        var spillFiles = new SpillFilePool(useAsync);
        bool hashInBackground = options.MaxDegreeOfParallelism != 1;
        var plan = new BundlePlan(source, layout, contentChanged, new List<PlannedEntry>(entries.Count), spillFiles)
        {
            Memory = compressionMemory,
            MemoryLimit = options.MaxCompressionMemory,
            MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
            Workers = hashInBackground ? new SemaphoreSlim(GetDegree(options.MaxDegreeOfParallelism)) : null
        };
        var work = new List<(PlannedEntry Planned, bool Compress)>();
        var backgroundHashes = new List<PlannedEntry>();

        try
        {
            foreach (var entry in entries)
            {
                var planned = new PlannedEntry(entry) { Size = entry.Data.Size };
                plan.Entries.Add(planned);

                if (!entry.IsModified && entry.Data.Hash is { } cachedHash)
                    planned.Hash = cachedHash;

                if (PlanEntry(planned, canCompress, compressChanged, compression, options))
                {
                    work.Add((planned, true));
                }
                else if (contentChanged && planned.Hash == null && planned.Kind == PayloadKind.CopyStored)
                {
                    // An entry copied as stored needs its own pass to be hashed: a decompression pass if it is compressed. Content of the caller
                    // is hashed while it is copied instead, so the identifier always matches the bytes written.
                    if (hashInBackground && !entry.IsModified)
                    {
                        planned.HashInBackground = true;
                        backgroundHashes.Add(planned);
                    }
                    else if (entry.Data.IsCompressed)
                    {
                        work.Add((planned, false));
                    }
                }
            }

            // Largest first, so a big entry does not start last and leave the other workers idle.
            work.Sort((a, b) => b.Planned.Size.CompareTo(a.Planned.Size));

            if (backgroundHashes.Count > 0)
            {
                // Compressed entries cost more per byte than the copies they are hashed alongside, so they are hashed first.
                backgroundHashes.Sort((a, b) => (b.Entry.Data.IsCompressed, b.Size).CompareTo((a.Entry.Data.IsCompressed, a.Size)));
                await CompressWhileHashingAsync(useAsync, plan, work, backgroundHashes, options, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await ParallelWork.ForEachAsync(useAsync, work, options.MaxDegreeOfParallelism, async (item, token) =>
                {
                    if (item.Compress)
                        await CompressAsync(useAsync, item.Planned, options, compressionMemory, spillFiles, token).ConfigureAwait(false);
                    else
                        item.Planned.SetHash(await HashContentAsync(useAsync, item.Planned.Entry.Data, token).ConfigureAwait(false));
                }, cancellationToken).ConfigureAwait(false);
            }

            long position = layout.BundleOffset;

            foreach (var planned in plan.Entries)
            {
                if (planned.CompressedSize == 0 && planned.Entry.Type == BundleFileType.Assembly && position % alignment != 0)
                    position += alignment - (position % alignment);

                planned.Offset = position;
                position += planned.PayloadLength;
            }

            // A recomputed identifier always has the same length, so the manifest length is known before the hashes are.
            var manifestHeader = contentChanged ? header with { Id = new string('0', BundleFormat.c_BundleIdLength) } : header;
            plan.ManifestOffset = position;
            plan.ManifestLength = ManifestSerializer.GetLength(manifestHeader, plan.Entries, s_ToManifestEntry);

            if (machO != null)
                plan.MachO = PlanMachO(machO, identifier, position + plan.ManifestLength);

            return plan;
        }
        catch
        {
            await DisposeAsync(useAsync, plan).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Writes the bundle to <paramref name="destination"/>: the host, the entries, the manifest and, for a signed macOS executable, the code
    /// signature.
    /// </summary>
    /// <param name="useAsync"><see langword="true"/> to use asynchronous I/O; <see langword="false"/> to complete synchronously.</param>
    /// <param name="source">The source the original bundle was read from.</param>
    /// <param name="layout">The layout of the original bundle.</param>
    /// <param name="entries">The entries to write, in order.</param>
    /// <param name="contentChanged"><see langword="true"/> if the content of any entry changed, so the bundle identifier must be recomputed.</param>
    /// <param name="destination">The stream to write the bundle to.</param>
    /// <param name="options">The options that control compression, alignment and code signing.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous write operation.</returns>
    public static async ValueTask WriteAsync(
        bool useAsync,
        BundleSource source,
        BundleLayout layout,
        IReadOnlyList<BundleEntry> entries,
        bool contentChanged,
        Stream destination,
        BundleWriteOptions options,
        CancellationToken cancellationToken)
    {
        var plan = await PlanAsync(useAsync, source, layout, entries, contentChanged, options, fileName: null, cancellationToken).ConfigureAwait(false);

        try
        {
            await WriteAsync(useAsync, plan, destination, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await DisposeAsync(useAsync, plan).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes a planned bundle to <paramref name="destination"/>: the host, the entries, the manifest and, for a signed macOS executable, the
    /// code signature.
    /// </summary>
    /// <remarks>
    /// The output is collected in a pooled 512 KiB buffer that the host, stored entries and decompressed entries are read straight into, and
    /// consecutive entries copied as stored from the opened bundle at the same distances as in it are read as one range. The pages of a signed
    /// macOS executable are hashed as the buffer is written out.
    /// </remarks>
    /// <param name="useAsync"><see langword="true"/> to use asynchronous I/O; <see langword="false"/> to complete synchronously.</param>
    /// <param name="plan">The plan from <see cref="PlanAsync"/>; it can be written once.</param>
    /// <param name="destination">The stream to write the bundle to.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous write operation.</returns>
    public static async ValueTask WriteAsync(bool useAsync, BundlePlan plan, Stream destination, CancellationToken cancellationToken)
    {
        var layout = plan.Layout;
        var signing = plan.MachO is { Identifier: not null } machO ? new PageHashingStream(destination, machO.SignatureOffset, machO.Image.CodePageSize) : null;
        var staging = new StagingStream(signing ?? destination, c_StagingBufferSize);
        DecompressionPipeline? pipeline = null;

        try
        {
            pipeline = CreateDecompressionPipeline(useAsync, plan, cancellationToken);
            pipeline?.Start();
            await WriteHostAsync(useAsync, plan, staging, cancellationToken).ConfigureAwait(false);

            var entries = plan.Entries;
            long written = layout.BundleOffset;
            int index = 0;

            while (index < entries.Count)
            {
                // A failure of the background hashing fails the save, so writing stops as soon as it is known.
                if (plan.BackgroundWorkFailed)
                    await plan.WaitForBackgroundWorkAsync(useAsync).ConfigureAwait(false);

                var planned = entries[index];
                await staging.WriteZerosAsync(useAsync, planned.Offset - written, cancellationToken).ConfigureAwait(false);

                int runEnd = FindStoredRunEnd(plan, index);

                if (runEnd > index)
                {
                    await CopyStoredRunAsync(useAsync, plan.Source, entries, index, runEnd, staging, cancellationToken).ConfigureAwait(false);
                    index = runEnd;
                }
                else if (pipeline != null && planned.Kind == PayloadKind.Decompress)
                {
                    await WriteDecompressedAsync(useAsync, pipeline, planned, staging, plan.ContentChanged, cancellationToken).ConfigureAwait(false);
                    index++;
                }
                else
                {
                    await WritePayloadAsync(useAsync, planned, staging, plan.ContentChanged, cancellationToken).ConfigureAwait(false);
                    index++;

                    // A written compressed buffer returned its memory, which entries decompressed ahead may use.
                    if (planned.Kind == PayloadKind.Buffer)
                        pipeline?.Start();
                }

                var last = entries[index - 1];
                written = last.Offset + last.PayloadLength;
            }

            await WriteManifestAsync(useAsync, plan, staging, cancellationToken).ConfigureAwait(false);

            if (signing != null)
                await WriteCodeSignatureAsync(useAsync, plan.MachO!, signing, staging, cancellationToken).ConfigureAwait(false);

            await staging.FlushBufferAsync(useAsync, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            staging.Dispose();
            signing?.Dispose();

            if (pipeline != null)
            {
                if (useAsync)
                    await pipeline.DisposeAsync().ConfigureAwait(false);
                else
                    pipeline.Dispose();
            }
        }

        await StreamCopy.FlushAsync(useAsync, destination, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds the changes to the load commands of a macOS host, as the .NET SDK makes them: the code signature command is replaced or removed,
    /// the string table and the <c>__LINKEDIT</c> segment are extended over the bundle and, when signing, the code signature.
    /// </summary>
    private static void AddMachOPatches(MachOPlan machO, List<(long Offset, byte[] Bytes)> patches)
    {
        var image = machO.Image;
        bool wasSigned = image.CodeSignatureCommandOffset >= 0;
        bool sign = machO.Identifier != null;
        int commandDelta = (sign ? 1 : 0) - (wasSigned ? 1 : 0);

        byte[] commands = new byte[8];
        BinaryEndianConverter.WriteInt32(commands, image.CommandCount + commandDelta, EndianType.LITTLE);
        BinaryEndianConverter.WriteInt32(commands, 4, image.CommandsSize + (commandDelta * MachOImage.c_CodeSignatureCommandSize), EndianType.LITTLE);
        patches.Add((MachOImage.c_CommandCountField, commands));

        long linkEditSize = machO.End - image.LinkEditOffset;
        byte[] fileSize = new byte[8];
        byte[] virtualSize = new byte[8];
        BinaryEndianConverter.WriteInt64(fileSize, linkEditSize, EndianType.LITTLE);
        BinaryEndianConverter.WriteInt64(virtualSize, (linkEditSize + MachOImage.c_SegmentAlignment - 1) & ~(MachOImage.c_SegmentAlignment - 1), EndianType.LITTLE);
        patches.Add((image.LinkEditCommandOffset + MachOImage.c_SegmentFileSizeField, fileSize));
        patches.Add((image.LinkEditCommandOffset + MachOImage.c_SegmentVirtualSizeField, virtualSize));

        byte[] stringTableSize = new byte[4];
        BinaryEndianConverter.WriteUInt32(stringTableSize, (uint)(machO.BundleEnd - image.StringTableOffset), EndianType.LITTLE);
        patches.Add((image.SymbolTableCommandOffset + MachOImage.c_StringTableSizeField, stringTableSize));

        if (!sign && !wasSigned)
            return;

        // The signature command is the last one: it is replaced where it was, or added after the others.
        byte[] signatureCommand = new byte[MachOImage.c_CodeSignatureCommandSize];

        if (sign)
        {
            BinaryEndianConverter.WriteUInt32(signatureCommand, MachOImage.c_CodeSignatureCommand, EndianType.LITTLE);
            BinaryEndianConverter.WriteUInt32(signatureCommand, 4, MachOImage.c_CodeSignatureCommandSize, EndianType.LITTLE);
            BinaryEndianConverter.WriteUInt32(signatureCommand, 8, (uint)machO.SignatureOffset, EndianType.LITTLE);
            BinaryEndianConverter.WriteUInt32(signatureCommand, 12, (uint)machO.SignatureLength, EndianType.LITTLE);
        }

        patches.Add((wasSigned ? image.CodeSignatureCommandOffset : MachOImage.c_HeaderSize + image.CommandsSize, signatureCommand));
    }

    /// <summary>
    /// Compresses the entry's content into a buffer and keeps it if it beats the compression threshold.
    /// </summary>
    private static async ValueTask CompressAsync(
        bool useAsync,
        PlannedEntry planned,
        BundleWriteOptions options,
        MemoryBudget compressionMemory,
        SpillFilePool spillFiles,
        CancellationToken cancellationToken)
    {
        var buffer = new SpillBuffer(compressionMemory, spillFiles);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        try
        {
            var deflate = new DeflateStream(buffer, options.CompressionLevel, leaveOpen: true);

            try
            {
                var content = planned.Entry.Data.Open();

                try
                {
                    long read = await StreamCopy.CopyAsync(useAsync, content, deflate, hash, cancellationToken).ConfigureAwait(false);
                    planned.EnsureContentLength(read);
                }
                finally
                {
                    await StreamCopy.DisposeAsync(useAsync, content).ConfigureAwait(false);
                }
            }
            finally
            {
                // Disposing writes the final deflate block.
                await StreamCopy.DisposeAsync(useAsync, deflate).ConfigureAwait(false);
            }
        }
        catch
        {
            buffer.Dispose();
            throw;
        }

        planned.SetHash(hash.GetHashAndReset());

        if (buffer.Length < planned.Size * options.CompressionThreshold)
        {
            buffer.Complete();
            planned.Kind = PayloadKind.Buffer;
            planned.Buffer = buffer;
            planned.CompressedSize = buffer.Length;
            planned.PayloadLength = buffer.Length;
            return;
        }

        if (!planned.Entry.IsModified)
            planned.Entry.Data.DiscardedCompression = (options.CompressionLevel, buffer.Length);

        // Disposing before completing lets the next spilled buffer reuse the file region.
        buffer.Dispose();
        planned.Kind = PayloadKind.CopyStored;
        planned.PayloadLength = planned.Size;
    }

    /// <summary>
    /// Compresses the entries of <paramref name="work"/> and hashes <paramref name="hashes"/> in one parallel pass in the background and returns
    /// once the compressions are done; the hashing goes on while the plan is completed and the bundle is written.
    /// </summary>
    /// <remarks>
    /// The compressions decide the layout, so they are handed out first; the hashes keep the remaining workers busy meanwhile. Each hash takes
    /// a slot of <see cref="BundlePlan.Workers"/>, which the work beside the writer shares.
    /// </remarks>
    private static async ValueTask CompressWhileHashingAsync(
        bool useAsync,
        BundlePlan plan,
        List<(PlannedEntry Planned, bool Compress)> work,
        List<PlannedEntry> hashes,
        BundleWriteOptions options,
        CancellationToken cancellationToken)
    {
        var compressed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int pendingCompressions = work.Count(item => item.Compress);
        var workers = plan.Workers!;
        List<(PlannedEntry Planned, bool Compress)> items = [.. work, .. hashes.Select(planned => (planned, false))];

        if (pendingCompressions == 0)
            compressed.SetResult();

        async ValueTask ProcessAsync((PlannedEntry Planned, bool Compress) item, CancellationToken token)
        {
            if (!item.Compress)
            {
                await HashWithSlotAsync(useAsync, item.Planned, workers, token).ConfigureAwait(false);
                return;
            }

            try
            {
                await CompressAsync(useAsync, item.Planned, options, plan.Memory, plan.SpillFiles, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                compressed.TrySetException(ex);
                throw;
            }

            if (Interlocked.Decrement(ref pendingCompressions) == 0)
                compressed.TrySetResult();
        }

        plan.StartBackgroundWork(token => useAsync
            ? ParallelWork.ForEachAsync(useAsync: true, items, options.MaxDegreeOfParallelism, ProcessAsync, token).AsTask()
            : Task.Run(() => ParallelWork.ForEachAsync(useAsync: false, items, options.MaxDegreeOfParallelism, ProcessAsync, token).GetAwaiter().GetResult(), CancellationToken.None),
            cancellationToken);

        // The pass ends early, without completing the compressions, when an entry fails or the save is canceled.
        var background = plan.BackgroundWork!;

        if (useAsync)
            await Task.WhenAny(compressed.Task, background).ConfigureAwait(false);
        else
            Task.WaitAny(compressed.Task, background);

        await plan.WaitAsync(useAsync, compressed.Task.IsCompleted ? compressed.Task : background).ConfigureAwait(false);
    }

    /// <summary>
    /// Copies the stored bytes of the entries <paramref name="start"/> to <paramref name="end"/> as one range of the source, writing zeros where
    /// the output has padding between them.
    /// </summary>
    private static async ValueTask CopyStoredRunAsync(
        bool useAsync,
        BundleSource source,
        List<PlannedEntry> entries,
        int start,
        int end,
        StagingStream staging,
        CancellationToken cancellationToken)
    {
        var first = entries[start];
        var last = entries[end - 1];
        long outputStart = first.Offset;
        long sourceStart = first.Entry.Data.Offset;
        long length = last.Offset + last.PayloadLength - outputStart;
        long copied = 0;
        int gapEntry = start;

        while (copied < length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await staging.EnsureFreeAsync(useAsync, cancellationToken).ConfigureAwait(false);

            var free = staging.Free;
            int toRead = (int)Math.Min(free.Length, length - copied);
            int read = await source.ReadAsync(useAsync, sourceStart + copied, free[..toRead], cancellationToken).ConfigureAwait(false);

            if (read == 0)
                throw CreateStoredDataEndedException(entries, start, end, outputStart + copied);

            gapEntry = ZeroGaps(entries, gapEntry, end, outputStart + copied, free.Span[..read]);
            staging.Advance(read);
            copied += read;
        }
    }

    /// <summary>
    /// Creates the pipeline that decompresses entries ahead of the writer, if the save decompresses entries and may use more than one thread
    /// and some memory.
    /// </summary>
    private static DecompressionPipeline? CreateDecompressionPipeline(bool useAsync, BundlePlan plan, CancellationToken cancellationToken)
    {
        if (plan.MaxDegreeOfParallelism == 1 || plan.MemoryLimit == 0)
            return null;

        var decompressed = plan.Entries.FindAll(p => p.Kind == PayloadKind.Decompress);

        if (decompressed.Count == 0)
            return null;

        return new DecompressionPipeline(
            useAsync,
            decompressed,
            plan.ContentChanged,
            plan.Memory,
            plan.MemoryLimit,
            plan.SpillFiles,
            plan.Workers!,
            GetDegree(plan.MaxDegreeOfParallelism),
            cancellationToken);
    }

    private static InvalidDataException CreateStoredDataEndedException(List<PlannedEntry> entries, int start, int end, long outputOffset)
    {
        // The entry that was being copied, or the next one if the source ended in the padding before it.
        var entry = entries[end - 1];

        for (int i = start; i < end; i++)
        {
            if (entries[i].PayloadLength > 0 && outputOffset < entries[i].Offset + entries[i].PayloadLength)
            {
                entry = entries[i];
                break;
            }
        }

        long copied = Math.Clamp(outputOffset - entry.Offset, 0, entry.PayloadLength);
        return new InvalidDataException($"The bundle entry '{entry.Entry.RelativePath}' ended after {copied} of its {entry.PayloadLength} stored bytes.");
    }

    /// <summary>
    /// Finds the entries from <paramref name="start"/> on that can be copied as one range of the opened bundle: unchanged entries copied as stored
    /// without hashing them on the way, whose distances in the output equal their distances in the bundle.
    /// </summary>
    /// <remarks>Empty entries inside a run are skipped, since they have no bytes to copy and the SDK places them at the next entry's offset.</remarks>
    /// <returns>The index after the last entry of the run, or <paramref name="start"/> if the entry at <paramref name="start"/> cannot be part of one.</returns>
    private static int FindStoredRunEnd(BundlePlan plan, int start)
    {
        var entries = plan.Entries;

        if (!IsStoredRunCandidate(plan, entries[start]) || entries[start].PayloadLength == 0)
            return start;

        var previous = entries[start];
        int end = start + 1;

        for (int i = start + 1; i < entries.Count; i++)
        {
            var next = entries[i];

            if (!IsStoredRunCandidate(plan, next))
                break;

            if (next.PayloadLength == 0)
                continue;

            long outputDistance = next.Offset - previous.Offset;

            if (outputDistance != next.Entry.Data.Offset - previous.Entry.Data.Offset || outputDistance < previous.PayloadLength)
                break;

            previous = next;
            end = i + 1;
        }

        return end;
    }

    private static int GetDegree(int maxDegreeOfParallelism)
    {
        return maxDegreeOfParallelism == -1 ? Environment.ProcessorCount : maxDegreeOfParallelism;
    }

    /// <summary>
    /// Computes the SHA-256 hash of the content of <paramref name="data"/>.
    /// </summary>
    /// <remarks>
    /// The content is read in chunks of up to 64 KiB, which hashing does not need larger: a pool array of that size is allocated on the young
    /// heap when the shared pool has dropped its arrays, while the 80 KiB copy chunks need one of 128 KiB on the large object heap.
    /// </remarks>
    private static async ValueTask<byte[]> HashContentAsync(bool useAsync, EntryData data, CancellationToken cancellationToken)
    {
        const int c_MaxChunkSize = 64 * 1024;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var stream = data.Open();

        try
        {
            int chunkSize = (int)Math.Clamp(data.Size, 1, c_MaxChunkSize);
            await StreamCopy.CopyAsync(useAsync, stream, null, hash, cancellationToken, chunkSize).ConfigureAwait(false);
        }
        finally
        {
            await StreamCopy.DisposeAsync(useAsync, stream).ConfigureAwait(false);
        }

        return hash.GetHashAndReset();
    }

    /// <summary>
    /// Hashes the content of <paramref name="planned"/> while holding one of <paramref name="workers"/>.
    /// </summary>
    private static async ValueTask HashWithSlotAsync(bool useAsync, PlannedEntry planned, SemaphoreSlim workers, CancellationToken cancellationToken)
    {
        if (useAsync)
            await workers.WaitAsync(cancellationToken).ConfigureAwait(false);
        else
            workers.Wait(cancellationToken);

        try
        {
            planned.SetHash(await HashContentAsync(useAsync, planned.Entry.Data, cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            workers.Release();
        }
    }

    // https://github.com/dotnet/runtime/blob/main/src/installer/managed/Microsoft.NET.HostModel/Bundle/Bundler.cs
    private static bool IsCompressible(BundleFileType type)
    {
        return type is not (BundleFileType.DepsJson or BundleFileType.RuntimeConfigJson);
    }

    private static bool IsStoredRunCandidate(BundlePlan plan, PlannedEntry planned)
    {
        var data = planned.Entry.Data;

        // An uncompressed entry whose hash is still needed is hashed while it is copied, outside of a run.
        return planned.Kind == PayloadKind.CopyStored
            && !planned.Entry.IsModified
            && ReferenceEquals(data.Source, plan.Source)
            && (data.IsCompressed || !plan.ContentChanged || planned.Hash != null || planned.HashInBackground);
    }

    /// <summary>
    /// Decides how an entry is written.
    /// </summary>
    /// <returns><see langword="true"/> if the entry must be compressed before the layout can be computed.</returns>
    private static bool PlanEntry(PlannedEntry planned, bool canCompress, bool compressChanged, BundleCompression compression, BundleWriteOptions options)
    {
        var entry = planned.Entry;
        var data = entry.Data;

        if (data.IsCompressed)
        {
            // The host reads deps.json and runtimeconfig.json through their recorded locations, so they are always stored uncompressed.
            if (compression != BundleCompression.NoCompression && IsCompressible(entry.Type))
            {
                planned.Kind = PayloadKind.CopyStored;
                planned.CompressedSize = data.CompressedSize;
                planned.PayloadLength = data.StoredLength;
            }
            else
            {
                planned.Kind = PayloadKind.Decompress;
                planned.PayloadLength = planned.Size;
            }

            return false;
        }

        // Unchanged entries are only compressed on request; new content follows the bundle's existing compression.
        bool compress = canCompress && IsCompressible(entry.Type) && (entry.IsModified ? compressChanged : compression == BundleCompression.Compress);

        // An unchanged entry that an earlier save compressed at the same level without beating this threshold is stored as is right away.
        if (compress && !entry.IsModified && data.DiscardedCompression is { } discarded && discarded.Level == options.CompressionLevel
            && !(discarded.Length < planned.Size * options.CompressionThreshold))
        {
            compress = false;
        }

        if (compress)
            return true;

        planned.Kind = PayloadKind.CopyStored;
        planned.PayloadLength = planned.Size;
        return false;
    }

    /// <summary>
    /// Plans how a bundle with a macOS host is written, validating that the executable can hold the bundle and its code signature.
    /// </summary>
    /// <param name="image">The load commands of the host.</param>
    /// <param name="identifier">The signing identifier, or <see langword="null"/> to write the executable without a code signature.</param>
    /// <param name="bundleEnd">The end of the manifest.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="InvalidOperationException">The bundle or its code signature would end beyond 4 GiB.</exception>
    private static MachOPlan PlanMachO(MachOImage image, string? identifier, long bundleEnd)
    {
        // The load command fields that cover the bundle and the signature are 32 bits wide.
        const long c_MaxEnd = uint.MaxValue;

        if (bundleEnd - image.StringTableOffset > c_MaxEnd)
            throw new InvalidOperationException("The bundle is too large for a macOS executable, whose string table must cover it.");

        if (identifier == null)
            return new MachOPlan(image, null, bundleEnd, bundleEnd, 0);

        long signatureOffset = MachOImage.AlignCodeSignature(bundleEnd);
        long signatureLength = MachOCodeSignature.GetLength(signatureOffset, identifier, image.CodePageSize);

        if (signatureOffset + signatureLength > c_MaxEnd)
            throw new InvalidOperationException("The bundle is too large for a signed macOS executable, whose code signature must start within 4 GiB.");

        return new MachOPlan(image, identifier, bundleEnd, signatureOffset, signatureLength);
    }

    /// <summary>
    /// Reads the signing identifier of the host's current code signature.
    /// </summary>
    /// <returns>The identifier, or <see langword="null"/> if the host is not signed or the signature could not be read.</returns>
    private static async ValueTask<string?> ReadSigningIdentifierAsync(bool useAsync, BundleSource source, MachOImage image, CancellationToken cancellationToken)
    {
        // The identifier follows the code directory header near the start of the signature; page hashes follow it.
        const int c_MaxIdentifierRead = 64 * 1024;

        if (image.CodeSignatureCommandOffset < 0 || image.CodeSignatureOffset >= source.Length)
            return null;

        int length = (int)Math.Min(Math.Min(image.CodeSignatureSize, c_MaxIdentifierRead), source.Length - image.CodeSignatureOffset);
        byte[] buffer = new byte[length];
        await source.ReadExactlyAsync(useAsync, image.CodeSignatureOffset, buffer, cancellationToken).ConfigureAwait(false);
        return MachOCodeSignature.ReadIdentifier(buffer);
    }

    /// <summary>
    /// Validates that the bundle of a macOS executable can be saved where the .NET SDK places it and determines the signing identifier.
    /// </summary>
    /// <remarks>
    /// The signing identifier is <see cref="BundleWriteOptions.MacOSCodeSignIdentifier"/>, else the destination file name, as the .NET SDK
    /// uses, else the identifier of the opened executable's current signature. The signature of a template is not used: it names the template,
    /// not the application.
    /// </remarks>
    /// <returns>The signing identifier, or <see langword="null"/> when <see cref="BundleWriteOptions.MacOSCodeSign"/> is <see langword="false"/>.</returns>
    /// <exception cref="InvalidOperationException">The load commands have no room for a code signature command, or no identifier is known.</exception>
    /// <exception cref="NotSupportedException">The bundle does not start after the string table in the <c>__LINKEDIT</c> segment.</exception>
    private static async ValueTask<string?> ResolveSigningIdentifierAsync(
        bool useAsync,
        BundleSource source,
        BundleLayout layout,
        MachOImage image,
        BundleWriteOptions options,
        string? fileName,
        CancellationToken cancellationToken)
    {
        // The string table is extended over the bundle, so the bundle must follow its start; the tables before the bundle are kept.
        if (layout.BundleOffset < Math.Max(image.LinkEditOffset, image.StringTableOffset))
            throw new NotSupportedException("The bundle of the macOS executable does not follow the start of the string table in its __LINKEDIT segment.");

        if (!options.MacOSCodeSign)
            return null;

        if (image.CodeSignatureCommandOffset < 0
            && MachOImage.c_HeaderSize + image.CommandsSize + MachOImage.c_CodeSignatureCommandSize > Math.Min(image.FirstSectionOffset, image.LinkEditOffset))
        {
            throw new InvalidOperationException("The macOS executable has no room for a code signature load command.");
        }

        return options.MacOSCodeSignIdentifier
            ?? fileName
            ?? (layout.IsTemplate ? null : await ReadSigningIdentifierAsync(useAsync, source, image, cancellationToken).ConfigureAwait(false))
            ?? throw new InvalidOperationException(
                "The signing identifier of the macOS executable is not known, because it was created from a template or is not signed; set "
                + $"{nameof(BundleWriteOptions)}.{nameof(BundleWriteOptions.MacOSCodeSignIdentifier)}.");
    }

    /// <summary>
    /// Writes the padding before the code signature of a macOS executable and the signature, which hashes everything written before it.
    /// </summary>
    private static async ValueTask WriteCodeSignatureAsync(bool useAsync, MachOPlan machO, PageHashingStream hashing, StagingStream staging, CancellationToken cancellationToken)
    {
        await staging.WriteZerosAsync(useAsync, machO.SignatureOffset - machO.BundleEnd, cancellationToken).ConfigureAwait(false);

        // The hashes are complete once everything before the signature has reached the hashing stream.
        await staging.FlushBufferAsync(useAsync, cancellationToken).ConfigureAwait(false);

        byte[] signature = new byte[machO.SignatureLength];
        MachOCodeSignature.Write(signature, machO.SignatureOffset, machO.Identifier!, machO.Image.CodePageSize, hashing.GetPageHashes());
        await StreamCopy.WriteAsync(useAsync, staging, signature, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask WriteDecompressedAsync(
        bool useAsync,
        DecompressionPipeline pipeline,
        PlannedEntry planned,
        StagingStream staging,
        bool needsHash,
        CancellationToken cancellationToken)
    {
        var buffer = await pipeline.TakeAsync(planned).ConfigureAwait(false);

        if (buffer == null)
        {
            await WritePayloadAsync(useAsync, planned, staging, needsHash, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            try
            {
                long copied = await buffer.WriteToAsync(useAsync, staging, cancellationToken).ConfigureAwait(false);

                if (copied != planned.PayloadLength)
                    throw new IOException($"The decompressed data of bundle entry '{planned.Entry.RelativePath}' changed while the bundle was being saved.");
            }
            finally
            {
                buffer.Dispose();
            }
        }

        pipeline.Release();
    }

    /// <summary>
    /// Copies the application host straight into the staging buffer, updating the manifest offset that precedes the bundle signature, removing
    /// a stale Authenticode certificate table reference and updating the load commands of a macOS host in place.
    /// </summary>
    private static async ValueTask WriteHostAsync(bool useAsync, BundlePlan plan, StagingStream destination, CancellationToken cancellationToken)
    {
        var source = plan.Source;
        var layout = plan.Layout;
        byte[] manifestOffsetBytes = new byte[BundleFormat.c_ManifestOffsetLength];
        BinaryEndianConverter.WriteInt64(manifestOffsetBytes, plan.ManifestOffset, EndianType.LITTLE);

        var patches = new List<(long Offset, byte[] Bytes)>
        {
            (layout.SignatureOffset - BundleFormat.c_ManifestOffsetLength, manifestOffsetBytes)
        };

        // The certificate table follows the bundle and is not written, so the reference to it (and the checksum it covers) must go.
        var host = layout.Host;

        if (host.SecurityDirectoryOffset >= 0 && host.SecurityOffset >= layout.BundleOffset)
        {
            patches.Add((host.SecurityDirectoryOffset, new byte[8]));
            patches.Add((host.CheckSumOffset, new byte[4]));
        }

        if (plan.MachO != null)
            AddMachOPatches(plan.MachO, patches);

        long position = 0;

        while (position < layout.BundleOffset)
        {
            await destination.EnsureFreeAsync(useAsync, cancellationToken).ConfigureAwait(false);

            var free = destination.Free;
            int chunk = (int)Math.Min(free.Length, layout.BundleOffset - position);
            await source.ReadExactlyAsync(useAsync, position, free[..chunk], cancellationToken).ConfigureAwait(false);

            var span = free.Span;

            foreach (var (offset, bytes) in patches)
            {
                for (int i = 0; i < bytes.Length; i++)
                {
                    long target = offset + i - position;

                    if (target >= 0 && target < chunk)
                        span[(int)target] = bytes[i];
                }
            }

            destination.Advance(chunk);
            position += chunk;
        }
    }

    private static async ValueTask WriteManifestAsync(bool useAsync, BundlePlan plan, StagingStream staging, CancellationToken cancellationToken)
    {
        var header = plan.Layout.Manifest.Header;

        if (plan.ContentChanged)
        {
            await plan.WaitForBackgroundWorkAsync(useAsync).ConfigureAwait(false);

            using var bundleHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            foreach (var planned in plan.Entries)
                bundleHash.AppendData(planned.Hash!);

            header = header with { Id = BundleFormat.CreateBundleId(bundleHash.GetHashAndReset()) };
        }

        int length = plan.ManifestLength;
        await staging.EnsureFreeAsync(useAsync, cancellationToken).ConfigureAwait(false);

        if (staging.Free.Length >= length)
        {
            ManifestSerializer.Write(header, plan.Entries, s_ToManifestEntry, staging.Free.Span[..length]);
            staging.Advance(length);
            return;
        }

        byte[] manifest = ArrayPool<byte>.Shared.Rent(length);

        try
        {
            ManifestSerializer.Write(header, plan.Entries, s_ToManifestEntry, manifest.AsSpan(0, length));
            await StreamCopy.WriteAsync(useAsync, staging, manifest.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(manifest);
        }
    }

    private static async ValueTask WritePayloadAsync(bool useAsync, PlannedEntry planned, StagingStream destination, bool needsHash, CancellationToken cancellationToken)
    {
        switch (planned.Kind)
        {
            case PayloadKind.Buffer:
                {
                    var buffer = planned.Buffer!;
                    long copied = await buffer.WriteToAsync(useAsync, destination, cancellationToken).ConfigureAwait(false);

                    // The buffer is no longer needed; releasing it early returns its memory to the pool and the budget.
                    buffer.Dispose();
                    planned.Buffer = null;

                    if (copied != planned.PayloadLength)
                        throw new IOException($"The compressed data of bundle entry '{planned.Entry.RelativePath}' changed while the bundle was being saved.");

                    return;
                }

            case PayloadKind.Decompress:
                {
                    using var hash = needsHash && planned.Hash == null ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
                    var content = planned.Entry.Data.Open();

                    try
                    {
                        long copied = await destination.CopyFromAsync(useAsync, content, hash, cancellationToken).ConfigureAwait(false);
                        planned.EnsureContentLength(copied);
                    }
                    finally
                    {
                        await StreamCopy.DisposeAsync(useAsync, content).ConfigureAwait(false);
                    }

                    if (hash != null)
                        planned.SetHash(hash.GetHashAndReset());

                    return;
                }

            case PayloadKind.CopyStored:
                {
                    var data = planned.Entry.Data;
                    bool hashWhileCopying = needsHash && planned.Hash == null && !planned.HashInBackground && !data.IsCompressed;
                    using var hash = hashWhileCopying ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
                    var stored = data.OpenStored();

                    try
                    {
                        long copied = await destination.CopyFromAsync(useAsync, stored, hash, cancellationToken).ConfigureAwait(false);

                        if (copied != planned.PayloadLength)
                        {
                            // New content comes from the caller, so a different length means their stream changed; original data is just corrupt.
                            if (planned.Entry.IsModified)
                                planned.EnsureContentLength(copied);

                            throw new InvalidDataException($"The bundle entry '{planned.Entry.RelativePath}' ended after {copied} of its {planned.PayloadLength} stored bytes.");
                        }
                    }
                    finally
                    {
                        await StreamCopy.DisposeAsync(useAsync, stored).ConfigureAwait(false);
                    }

                    if (hash != null)
                        planned.SetHash(hash.GetHashAndReset());

                    return;
                }

            default:
                throw new InvalidOperationException();
        }
    }

    /// <summary>
    /// Clears the bytes of <paramref name="chunk"/>, which holds the output from <paramref name="chunkOffset"/> on, that lie between two entries.
    /// </summary>
    /// <returns>The first entry whose following gap may still intersect later chunks.</returns>
    private static int ZeroGaps(List<PlannedEntry> entries, int entry, int end, long chunkOffset, Span<byte> chunk)
    {
        long chunkEnd = chunkOffset + chunk.Length;

        for (; entry < end - 1; entry++)
        {
            long gapStart = entries[entry].Offset + entries[entry].PayloadLength;
            long gapEnd = entries[entry + 1].Offset;

            if (gapStart >= chunkEnd)
                break;

            if (gapEnd > chunkOffset && gapEnd > gapStart)
            {
                long clearStart = Math.Max(gapStart, chunkOffset);
                long clearEnd = Math.Min(gapEnd, chunkEnd);
                chunk[(int)(clearStart - chunkOffset)..(int)(clearEnd - chunkOffset)].Clear();
            }

            // The gap continues into the next chunk.
            if (gapEnd > chunkEnd)
                break;
        }

        return entry;
    }
}