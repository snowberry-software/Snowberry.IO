# Snowberry.IO.Common

[![NuGet Version](https://img.shields.io/nuget/v/Snowberry.IO.Common.svg?logo=nuget)](https://www.nuget.org/packages/Snowberry.IO.Common/)

**The shared contracts and primitives of Snowberry.IO.** This package defines the `IEndianReader` and `IEndianWriter` interfaces, the `EndianType` byte order, the `Sha1` value type, `RegionRange`, the `Analyzer` base class, `BinaryEndianConverter` and the `BinaryUtils` alignment helpers. Reference it directly when a library only needs the contracts or the converter; use [Snowberry.IO](https://github.com/snowberry-software/Snowberry.IO) for the stream-based implementations. It targets .NET 10 plus .NET Standard 2.1 and 2.0; the .NET Standard 2.0 build depends on [System.Memory](https://www.nuget.org/packages/System.Memory/) and [IndexRange](https://www.nuget.org/packages/IndexRange/) for spans and `Range`.

## Table of Contents

- [Installation](#installation)
- [Quick Start](#quick-start)
- [Top Features at a Glance](#top-features-at-a-glance)
- [Core Types](#core-types)
- [Reader and Writer Contracts](#reader-and-writer-contracts)
- [Pitfalls](#pitfalls)
- [Related Packages](#related-packages)

## Installation

```bash
dotnet add package Snowberry.IO.Common
```

## Quick Start

1. Convert bytes in a chosen byte order.
2. Align a position.
3. Parse and format a SHA-1 hash.

```cs
using System;
using Snowberry.IO.Common;

byte[] data = new byte[] { 0x00, 0x00, 0x00, 0x2A, 0x2A, 0x00, 0x00, 0x00 };

int big = BinaryEndianConverter.ToInt32(data, EndianType.BIG);           // 42, from the start of the span
int little = BinaryEndianConverter.ToInt32(data, 4, EndianType.LITTLE);  // 42, from offset 4

long position = 13;
long padding = BinaryUtils.CalculatePadding(position, 8); // 3
BinaryUtils.ApplyAlignment(ref position, 8);              // position is now 16

var hash = new Sha1("da39a3ee5e6b4b0d3255bfef95601890afd80709");
Console.WriteLine(hash); // DA39A3EE5E6B4B0D3255BFEF95601890AFD80709
```

## Top Features at a Glance

- **`BinaryEndianConverter`.** Bounds-checked conversion of a `ReadOnlySpan<byte>` to `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double` and `Guid` in either byte order. Every type has an overload that takes an offset; all but `Guid` also have one that reads from the start of the span.
- **`Sha1`.** A 20-byte hash value with equality, uppercase hex formatting through `ToString()`, parsing from a 40-character hex string, `GetHashBuffer()` and allocation-free `TryWriteBytes`. It also builds from a 20-byte `byte[]` or span (`From` refills an existing value), and `s_Zero` is the all-zero hash.
- **`IEndianReader` and `IEndianWriter`.** The contracts implemented by the Snowberry.IO readers and writers, so code can accept any reader or writer. See [Reader and Writer Contracts](#reader-and-writer-contracts).
- **`Analyzer`.** A base class for observing or rewriting bytes right after a reader reads them.
- **`BinaryUtils`.** `CalculatePadding(long position, byte alignment)` returns the padding bytes needed, and `ApplyAlignment(ref long position, byte alignment)` moves the position forward to the next aligned value. The alignment must be a power of two, so the largest is 128.

## Core Types

| Type                    | Namespace                                | Purpose                                                       |
| ----------------------- | ---------------------------------------- | ------------------------------------------------------------- |
| `EndianType`            | `Snowberry.IO.Common`                    | Byte order: `LITTLE` or `BIG`.                                |
| `BinaryEndianConverter` | `Snowberry.IO.Common`                    | Converts spans to primitives and `Guid` in either byte order. |
| `BinaryUtils`           | `Snowberry.IO.Common`                    | Alignment and padding helpers.                                |
| `Sha1`                  | `Snowberry.IO.Common`                    | 20-byte SHA-1 hash value.                                     |
| `RegionRange`           | `Snowberry.IO.Common.Reader`             | Start position and size of a reader region view.              |
| `Analyzer`              | `Snowberry.IO.Common.Reader`             | Base class that sees, and may change, every read byte.        |
| `IEndianReader`         | `Snowberry.IO.Common.Reader.Interfaces`  | The reader contract.                                          |
| `IEndianWriter`         | `Snowberry.IO.Common.Writer.Interfaces`  | The writer contract.                                          |

## Reader and Writer Contracts

- **`IEndianReader` mirrors `BinaryReader`.** `ReadInt32()` reads little-endian, and `ReadInt32(EndianType.BIG)` picks the byte order. On top of that it adds `TryRead*` methods that return `false` instead of throwing at the end of the data, `ReadExactly`, `ReadCString`, `ReadSizedCString`, `ReadLine`, `ReadSha1`, `ReadAlignment`, `ReadUntilEnd`, `CopyTo`, region views (`EnableRegionView`, `DisableRegionView`) and the `Analyzer` property.
- **`IEndianWriter` follows `BinaryWriter` with two differences.** Every `Write` returns the writer, so calls chain, and the byte order is an optional parameter, as in `Write(int value, EndianType endian = EndianType.LITTLE)`. It adds `WriteCString`, `WriteSizedCString`, `WriteLine`, `WriteStringCharacters`, `WritePadding` and `Write(Sha1)`.
- **`Analyzer` has two required members.** `Initialize(IEndianReader)` runs when a reader is created with the analyzer, and `AnalyzeReadBytes(IEndianReader, Span<byte>, int)` receives each read. Override `ModifiesBytes` to return `false` when the analyzer only observes.

For worked examples of region views and a custom analyzer, see [Region Views](https://github.com/snowberry-software/Snowberry.IO#region-views) and [Custom Analyzer](https://github.com/snowberry-software/Snowberry.IO#custom-analyzer) in the Snowberry.IO README.

## Pitfalls

- **Converters throw on short input.** Every `BinaryEndianConverter` overload throws `ArgumentOutOfRangeException` when fewer bytes are available than the type needs, or when the offset is negative, instead of reading past the span.
- **Alignment must be a power of two.** `BinaryUtils` throws `ArgumentOutOfRangeException` for `0` or values such as `3`.
- **`Range` converts to `RegionRange` only when both ends count from the start.** `(RegionRange)(20..80)` and `RegionRange.ToRegionRange(20..80)` work, but `^16..` and open ranges such as `20..` (whose end is `^0`) throw `ArgumentException`. Pass those to `IEndianReader.EnableRegionView(Range)`, which resolves them against the reader's length.
- **`Sha1` components are not the SHA-1 state words.** Each component A to E is the little-endian reading of a 4-byte slice of the digest, not H0 to H4. Compare hashes with `==`, `ToString()` or the bytes from `TryWriteBytes`, not with hand-built components.
- **`ModifiesBytes` must stay `true` if the analyzer changes bytes.** Null-terminated string and line reads search for the terminator after the analyzer has run; returning `false` lets them search first and report bytes in larger pieces, so an analyzer that decrypts would be searched before decryption.
- **`Half` is only on the .NET 10 build.** `ReadHalf` and `Write(Half)` are not part of the interfaces in the .NET Standard builds.

## Related Packages

| Package                                                                                                               | Purpose                                                                |
| --------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------- |
| [Snowberry.IO](https://github.com/snowberry-software/Snowberry.IO)                                                    | Stream and process-memory readers and the stream writer.               |
| [Snowberry.IO.SingleFile](https://github.com/snowberry-software/Snowberry.IO/tree/master/src/Snowberry.IO.SingleFile) | Reads and modifies bundles of single-file published .NET applications. |
