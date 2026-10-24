# Snowberry.IO.SingleFile

[![NuGet Version](https://img.shields.io/nuget/v/Snowberry.IO.SingleFile.svg?logo=nuget)](https://www.nuget.org/packages/Snowberry.IO.SingleFile/)

**Read, modify and create the bundle of a single-file published .NET application.** Snowberry.IO.SingleFile opens the bundle inside the executable, lists and streams the embedded files, and writes a new executable with files replaced, added or removed; it also creates new bundles, after an application host template or without a host. It supports bundle format versions 1, 2 and 6 (.NET Core 3.x, .NET 5 and .NET 6 or later). Windows, Linux and single-architecture macOS application hosts can be read and saved, and macOS executables are signed ad-hoc like the .NET SDK signs them; universal (fat) macOS executables cannot be opened. Entries are read on demand, and saving writes a single forward pass in which unchanged entries are copied as they are stored. It targets .NET 10 and builds on [Snowberry.IO](https://github.com/snowberry-software/Snowberry.IO).

## Table of Contents

- [Installation](#installation)
- [Quick Start](#quick-start)
- [Top Features at a Glance](#top-features-at-a-glance)
- [How It Works](#how-it-works)
- [API Reference](#api-reference)
- [Compression](#compression)
- [Pitfalls](#pitfalls)
- [Related Packages](#related-packages)

## Installation

```bash
dotnet add package Snowberry.IO.SingleFile
```

## Quick Start

1. Open the executable.
2. Read, replace, add or remove entries.
3. Save a new executable.

```cs
using System.Text;
using Snowberry.IO.SingleFile;

using var bundle = SingleFileBundle.Open("app.exe");

Console.WriteLine($"Bundle {bundle.Header.Id}, format {bundle.Header.MajorVersion}.{bundle.Header.MinorVersion}");

foreach (BundleEntry entry in bundle.Entries)
    Console.WriteLine($"{entry.Type,-18} {entry.RelativePath} ({entry.Size} bytes)");

BundleEntry runtimeConfig = bundle.RuntimeConfigJson!;
string json = Encoding.UTF8.GetString(runtimeConfig.ReadAllBytes());
runtimeConfig.Replace(Encoding.UTF8.GetBytes(json.Replace(
    "\"System.Globalization.Invariant\": false",
    "\"System.Globalization.Invariant\": true")));

bundle.AddFromFile("plugins/extra.dll", BundleFileType.Assembly, "extra.dll");
bundle.RemoveAll(entry => entry.Type == BundleFileType.Symbols);

bundle.Save("app.patched.exe");
```

## Top Features at a Glance

- **Clear failures.** `SingleFileBundle.Open` throws `InvalidDataException` with the reason when a file is not a valid or supported bundle. `TryOpen(string, out SingleFileBundle?)` returns `false` for that case only; it still throws `ArgumentException`, `FileNotFoundException`, `UnauthorizedAccessException` and `IOException`. There is no `TryOpen` overload for streams, but `Open` and `OpenAsync` accept any readable, seekable `Stream`.
- **Streaming reads.** `BundleEntry.Open()` returns a stream over the entry; compressed entries are decompressed incrementally as the stream is read, and reading fails when an entry's data does not match its declared size. Entries can be read concurrently; reads of a bundle opened from a stream are serialized.
- **Simple edits.** `SingleFileBundle` adds and removes entries: `Add` and `AddFromFile` take a relative path, `Remove` takes a path or an entry, and `RemoveAll` takes a predicate. `BundleEntry` changes its own content: `Replace`, `ReplaceFromFile` and `Revert`. `GetEntry` and `TryGetEntry` look entries up by path, case-sensitively.
- **New bundles.** `SingleFileBundle.Create(header)` starts an empty bundle without an application host, for working with the bundle format alone: saving writes the manifest offset and the bundle signature, the entries and the manifest, and `Open` reads the result back, but it is not an executable. `Create(hostPath)` and `Create(Stream)` start one after an application host that contains the bundle signature with a manifest offset of `0`, like the `apphost` and `singlefilehost` templates of the .NET runtime packs, and throw `InvalidDataException` for a host that already contains a bundle. The host is used as is: patching the application name into an `apphost` is up to the caller. A signed host loses its signature when the bundle is saved, as an opened bundle does (see [Pitfalls](#pitfalls)). `header` defaults to format version 6.0 without flags; its `Id` is replaced by the identifier computed when saving.
- **macOS signing.** Saving a macOS executable updates its load commands the way the .NET SDK does: the `__LINKEDIT` segment and the string table are extended over the bundle and the old code signature is replaced by a new ad-hoc signature of SHA-256 page hashes, which Apple Silicon Macs require to run it. Arm64 executables are hashed in 16 KiB pages, like `codesign` on macOS 26 and the .NET 11 SDK, and others in 4 KiB pages; the .NET 10 SDK hashes every executable in 4 KiB pages, and Arm64 Macs accept both. The signing identifier is `BundleWriteOptions.MacOSCodeSignIdentifier`, else the file name for `Save(string)`, else, for an opened bundle, the identifier of the current signature; `MacOSCodeSign = false` leaves the executable unsigned, for signing it with `codesign` afterwards.
- **Faithful saves.** Unchanged entries are copied byte for byte and the assembly alignment of the opened bundle is kept, so saving an unmodified bundle with the default options (`Preserve`, `AssemblyAlignment = null`) reproduces the original file, unless the file is signed or has other data after the manifest (see [Pitfalls](#pitfalls)). The destination stream does not need to be seekable, and compression and hashing run in parallel.
- **Safe extraction.** `ExtractToDirectory` validates every entry path before writing, so a crafted path such as `../x` cannot escape the target directory, and on Windows a path that the running Windows version maps to a device, such as `NUL`, is rejected. Two entries whose paths are equal, or differ only in case on Windows and macOS, make the extraction fail instead of overwriting each other.
- **Async variants.** `SingleFileBundle.OpenAsync`, `CreateAsync`, `SaveAsync` and `ExtractToDirectoryAsync`, and `BundleEntry.ReadAllBytesAsync`, `ExtractToFileAsync` and `ExtractToDirectoryAsync` accept a `CancellationToken`, and the bundle supports `await using`.

```cs
await using var bundle = await SingleFileBundle.OpenAsync("app.exe");

await bundle.ExtractToDirectoryAsync("extracted");
await bundle.SaveAsync(Stream.Null);
```

## How It Works

Opening a bundle reads the application host up to the bundle signature and the manifest; entry content stays in the file until it is read. Edits only swap an entry's content source in memory. Saving plans the layout first, compressing or decompressing entries in parallel where needed, then writes the destination in one forward pass: the host, the entries in `Entries` order, the manifest and, for a signed macOS executable, the code signature, whose page hashes are computed as the bytes are written. When any content changed, a new bundle identifier is computed from the entry hashes.

```text
app.exe ──Open──> host image + manifest      entries read lazily (Open / ReadAllBytes)
                          │
                          ▼
             Add / Remove / Replace / Revert  (in memory only)
                          │
                          ▼  Save
   plan: compress / decompress / hash in parallel
         buffered in memory up to MaxCompressionMemory, then temp files
                          │
                          ▼
   write: host ─> entries (copied, compressed or decompressed) ─> manifest ─> code signature (macOS)
```

`Save(string)` writes to a temporary file next to the destination and moves it into place when complete, so a macOS executable gets a new file and macOS does not reuse a cached signature check. On Linux and macOS the file gets the permissions of the file the bundle was opened or created from, or `rwxr-xr-x` for a bundle opened or created from a stream or created without an application host.

## API Reference

**`SingleFileBundle`**: `Header` (`MajorVersion`, `MinorVersion`, `Id`, `Flags`, `SupportsCompression`), `Entries`, `DepsJson`, `RuntimeConfigJson`, `IsModified`, plus the open, create, edit, save and extract methods above.

**`BundleEntry`**: `RelativePath`, `Type`, `Size`, `CompressedSize`, `IsCompressed`, `Offset` (`-1` once modified), `IsModified`, `IsNew`, `Open`, `ReadAllBytes`, `ExtractToFile` (the directory must exist) and `ExtractToDirectory` (returns the written path).

**`BundleFileType`**

| Value               | Purpose                                                                    |
| ------------------- | -------------------------------------------------------------------------- |
| `Unknown`           | A file of unknown type, such as a content or resource file.               |
| `Assembly`          | A managed assembly; uncompressed assemblies are aligned when written.     |
| `NativeBinary`      | A native library.                                                          |
| `DepsJson`          | The application's `*.deps.json`; never compressed.                         |
| `RuntimeConfigJson` | The application's `*.runtimeconfig.json`; never compressed.                |
| `Symbols`           | A debug symbols file.                                                      |

**`BundleWriteOptions`** is a record; derive variations from `BundleWriteOptions.Default` with a `with` expression. Invalid values throw when set: `ArgumentException` for `MacOSCodeSignIdentifier` and `ArgumentOutOfRangeException` otherwise.

| Property                  | Default                                             | Constraint                                                        |
| ------------------------- | --------------------------------------------------- | ----------------------------------------------------------------- |
| `Compression`             | `Preserve`                                          | A defined `BundleCompression` value.                              |
| `CompressionLevel`        | `SmallestSize`                                      | A defined `CompressionLevel` value.                               |
| `CompressionThreshold`    | `0.75`                                              | Greater than `0`.                                                 |
| `AssemblyAlignment`       | `null` (keep the opened alignment)                  | `null` or a positive power of two.                                |
| `MaxCompressionMemory`    | 64 MiB                                              | Not negative; `0` buffers all compressed data in temporary files. |
| `MaxDegreeOfParallelism`  | `-1` (processor count)                              | `-1` or greater than `0`; `1` does one thing at a time.           |
| `MacOSCodeSign`           | `true`                                              | `false` leaves a saved macOS executable unsigned.                 |
| `MacOSCodeSignIdentifier` | `null` (file name, or the opened file's identifier) | `null` or a non-empty string without null characters.             |

## Compression

Bundles of format version 6 can store entries compressed; format 1 and 2 bundles are always saved uncompressed. `BundleWriteOptions.Compression` controls how entries are saved:

| Mode            | Unchanged entries                                                                     | New and replaced entries                                      |
| --------------- | ------------------------------------------------------------------------------------- | ------------------------------------------------------------- |
| `Preserve`      | Copied exactly as stored.                                                             | Compressed if the bundle already contains compressed entries. |
| `Compress`      | Compressed if stored uncompressed; compressed ones are copied.                        | Compressed.                                                   |
| `NoCompression` | Decompressed if stored compressed.                                                    | Stored uncompressed.                                          |
| `Auto`          | As `Compress` if the bundle already contains compressed entries; otherwise copied.    | Compressed if the bundle already contains compressed entries. |

`deps.json` and `runtimeconfig.json` entries are never compressed, and entries that do not shrink below `CompressionThreshold` (0.75 by default) are stored uncompressed, matching the .NET SDK bundler.

```cs
bundle.Save("app.small.exe", new BundleWriteOptions { Compression = BundleCompression.Compress });
```

`Auto` compresses as much as possible without risking the application: a bundle that already contains compressed entries runs on a host that can read them, so all of it is compressed; any other bundle is left uncompressed. Unlike `Compress`, it never throws for framework-dependent applications.

To keep compressed data even when it does not shrink enough, set `CompressionThreshold` to `double.PositiveInfinity`.

## Pitfalls

- **Save to a different path.** The opened file stays open for reading, so `Save` refuses to overwrite it with an `IOException`; save to another path and replace the file afterwards.
- **Keep replacement streams open.** A seekable stream passed to `Replace` or `Add` is read when the bundle is saved, so it must stay open and unchanged until then; a non-seekable stream is copied into memory immediately.
- **Memory content is referenced, not copied.** Array-backed `ReadOnlyMemory<byte>` passed to `Replace` or `Add` must not change until the bundle has been saved.
- **Source files stay open.** `AddFromFile` and `ReplaceFromFile` open the file immediately and keep it open, readable by others, until the entry is removed or its content replaced or reverted, or the bundle is disposed.
- **Do not enumerate `Entries` while editing.** The list reflects later `Add` and `Remove` calls; use `RemoveAll` to remove entries by condition.
- **`Header` describes the opened file.** For a created bundle it is the header passed to `Create`. Saving writes a new identifier when content changed but does not update `Header`.
- **Compression needs a self-contained app.** The .NET host only reads compressed entries in self-contained single-file apps, so `BundleCompression.Compress` throws `InvalidOperationException` when the bundle format supports compression (version 6) and the application is framework-dependent. The check reads the bundled `runtimeconfig.json`: the app counts as framework-dependent when `runtimeOptions` lists `framework` or `frameworks` but not `includedFrameworks`; a missing, unparsable or larger than 1 MiB file counts as self-contained. Use `Auto` to compress only when the bundle shows it can be.
- **Compressing while saving needs buffer space.** Entries compressed while saving are buffered until they are written: in memory up to `BundleWriteOptions.MaxCompressionMemory` (64 MiB by default), and in temporary files beyond it.
- **Empty bundles cannot be saved.** `Save` throws `InvalidOperationException` when every entry has been removed, because the .NET host refuses to run a bundle without files.
- **Signatures are not kept.** Data after the manifest, such as an Authenticode signature, is dropped and its reference in the Windows host is cleared; sign the saved executable again. A macOS executable gets a new ad-hoc signature instead, which does not identify a developer; sign it with `codesign` and a certificate if it must pass Gatekeeper.
- **Created or unsigned macOS executables need an identifier for streams.** `Save(Stream)` takes the signing identifier from the current signature of an opened bundle. A created bundle does not use its template's signature, which names the template rather than the application, so saving it or an unsigned executable to a stream throws `InvalidOperationException` unless `MacOSCodeSignIdentifier` is set.
- **Some macOS executables cannot be saved.** `Save` throws `NotSupportedException` when the load commands do not have the layout the .NET SDK requires (one `__LINKEDIT` segment after the other segments and the load commands, with an `LC_SYMTAB` inside it) or the bundle does not follow the start of the string table, and `InvalidOperationException` when the bundle and its signature would end beyond 4 GiB or an unsigned host has no room for a code signature command. `Create` rejects a macOS host without that layout with `InvalidDataException`, and universal (fat) macOS executables make `Open` and `Create` throw `InvalidDataException`.
- **Extraction can be partial.** If an entry fails to extract, for example because a file exists and `overwrite` is `false`, files already written are kept.
- **Extraction follows existing links.** Symbolic links and junctions already in the destination directory are followed, so files can be written outside of it. Extract into a new or trusted directory.
- **Collision checks only fold case.** Paths that differ in Unicode normalization, or only in case on a case-insensitive Linux mount, are not detected as the same file; with `overwrite` set to `true`, one entry then overwrites the other.

## Related Packages

| Package                                                                                                       | Purpose                                                          |
| ------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------- |
| [Snowberry.IO](https://github.com/snowberry-software/Snowberry.IO)                                            | Stream and process-memory readers and the stream writer.         |
| [Snowberry.IO.Common](https://github.com/snowberry-software/Snowberry.IO/tree/master/src/Snowberry.IO.Common) | Reader and writer contracts, `BinaryEndianConverter` and `Sha1`. |
