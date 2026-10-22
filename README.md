# Snowberry.IO

[![License](https://img.shields.io/github/license/snowberry-software/Snowberry.IO)](https://github.com/snowberry-software/Snowberry.IO/blob/master/LICENSE)
[![NuGet Version](https://img.shields.io/nuget/v/Snowberry.IO.svg?logo=nuget)](https://www.nuget.org/packages/Snowberry.IO/)

**An endian-aware binary reader and writer for .NET.** Snowberry.IO extends `BinaryReader` and `BinaryWriter` and reads and writes primitives, strings and `Guid` values in little- or big-endian order, plus SHA-1 values, from any `Stream`. It also restricts reads to a region of the stream, scans for byte signatures and can read the memory of another Windows process. It targets .NET 10 plus .NET Standard 2.1 and 2.0, and builds on [Snowberry.IO.Common](https://github.com/snowberry-software/Snowberry.IO/tree/master/src/Snowberry.IO.Common).

## Table of Contents

- [Installation](#installation)
- [Quick Start](#quick-start)
- [Top Features at a Glance](#top-features-at-a-glance)
- [How It Works](#how-it-works)
- [Core Types](#core-types)
- [Region Views](#region-views)
- [Endian Converter](#endian-converter)
- [Custom Analyzer](#custom-analyzer)
- [Process Memory](#process-memory)
- [Supported Data Types](#supported-data-types)
- [Pitfalls](#pitfalls)
- [Related Packages](#related-packages)

## Installation

```bash
dotnet add package Snowberry.IO
```

## Quick Start

1. Write values with an explicit byte order.
2. Rewind and read them back in the same order.

```cs
using Snowberry.IO.Common;
using Snowberry.IO.Reader;
using Snowberry.IO.Writer;

var stream = new MemoryStream();

using (var writer = new EndianStreamWriter(stream, keepStreamOpen: true))
{
    writer.Write(10, EndianType.BIG)
          .Write(20L, EndianType.BIG)
          .Write(30F, EndianType.BIG)
          .WriteCString("hello");
}

stream.Position = 0;

using var reader = new EndianStreamReader(stream);

int a = reader.ReadInt32(EndianType.BIG);
long b = reader.ReadInt64(EndianType.BIG);
float c = reader.ReadSingle(EndianType.BIG);
string text = reader.ReadCString();
```

## Top Features at a Glance

- **Both byte orders for every primitive.** The readers are `BinaryReader`s, so `ReadInt32()` and the other inherited reads stay little-endian; the overloads such as `ReadInt32(EndianType.BIG)` and `Write(value, EndianType.BIG)` pick the byte order for 16-, 32- and 64-bit integers, `float`, `double`, `Guid`, and `Half` on the .NET 10 build. The endian-aware `Write` overloads return the writer, so calls chain.
- **Strings in every common layout.** Zero-terminated (`ReadCString`/`WriteCString`), fixed-size zero-terminated (`ReadSizedCString`/`WriteSizedCString`), 7-bit length-prefixed (`ReadString`), lines (`ReadLine`/`WriteLine`) and raw characters without a prefix or terminator (`WriteStringCharacters`), for UTF-8, UTF-16 and UTF-32.
- **Non-throwing reads.** `TryReadInt32`, `TryReadGuid`, `TryReadSha1` and the other `TryRead*` methods return `false` instead of throwing at the end of the data, and restore the position when the stream can seek. `ReadByteSafe` returns `-1` at the end instead of throwing.
- **Region views.** `EnableRegionView(20..80)` makes a slice of the stream look like the whole stream: `Position` and `Length` become relative, and reads stop at the region end.
- **Bulk reads.** `ReadUntilEnd` returns the remaining bytes, `CopyTo(destination)` and `CopyTo(destination, length)` copy them to another `Stream`, and `ReadAlignment`/`WritePadding` skip or write padding to a power-of-two boundary.
- **Signature scanning.** With `using Snowberry.IO.Extensions;`, `reader.ScanSignatures("44 ?? 44 ?? 44", maxCount: 10, maxAddress: reader.Length)` returns the positions of a byte pattern with `??` wildcards.
- **Buffer helpers.** `ReadInInternalBuffer` fills the reader's `Buffer`, and `ReadInt64At`, `ReadGuidAt`, `ReadSha1At` and friends decode values from it at an offset. The buffer holds 20 bytes unless a larger `bufferSize` is passed to the constructor; call `EnsureBufferSize` before reading more into it.
- **Process memory.** `Win32ProcessReader` reads another process's memory on Windows through the same `IEndianReader` API, with `Position` as the virtual address. See [Process Memory](#process-memory).

## How It Works

Every reader reads through an internal read-only view stream that sits between the `BinaryReader` base and the data source. The view applies the region view and reports every block of bytes to the `Analyzer`, so the inherited `BinaryReader` methods honor both, just like the endian-aware reads. `BaseStream` returns this view, so seeking or reading it moves the reader.

```text
 EndianStreamReader / Win32ProcessReader
   inherited reads       endian-aware, string and TryRead* reads
          \                     /
           v                   v
      view stream (BaseStream)
        - Position and Length relative to the region view
        - Analyzer.AnalyzeReadBytes on every block read
           |
           v
   source: any Stream (EndianStreamReader.Stream)
           or process memory (ReadProcessMemory)
```

The writer has no such layer: `EndianStreamWriter` derives from `BinaryWriter` and writes to the `Stream` directly.

## Core Types

| Type                    | Purpose                                                                                                                  |
| ----------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| `IEndianReader`         | The reader contract, defined in Snowberry.IO.Common.                                                                     |
| `BaseEndianReader`      | Derives from `BinaryReader` and implements `IEndianReader` with the string, region view and try-read logic.              |
| `EndianStreamReader`    | Reads from any `Stream`, including non-seekable ones; disposes it unless `KeepStreamOpen` is set.                        |
| `Win32ProcessReader`    | Reads the memory of a Windows process.                                                                                   |
| `IEndianWriter`         | The writer contract, defined in Snowberry.IO.Common.                                                                     |
| `EndianStreamWriter`    | Writes to a `Stream`; derives from `BinaryWriter` and implements `IEndianWriter`.                                        |
| `BinaryEndianConverter` | Converts spans of bytes to values, see [Endian Converter](#endian-converter).                                            |
| `BinaryUtils`           | `ApplyAlignment` and `CalculatePadding` compute the aligned position or the padding for a power-of-two alignment.        |
| `RegionRange`           | A region given as `StartPosition` and `Size`, accepted by `EnableRegionView(RegionRange)`.                               |
| `Sha1`                  | A 20-byte SHA-1 hash value type; built from bytes or a 40-character hex string, written out with `TryWriteBytes`.        |

`EndianStreamReader(stream, analyzer, bufferSize, encoding)` and `EndianStreamWriter(stream, keepStreamOpen, encoding)` take an explicit `Encoding`; the shorter constructors use UTF-8 without a byte order mark.

## Region Views

A region view restricts a reader to part of its data. Indexes relative to the end are resolved against the full length, which `ActualLength` keeps reporting while the view is active, as `ActualPosition` does for the position:

```cs
using var reader = new EndianStreamReader(File.OpenRead("archive.bin"));

reader.EnableRegionView(0x200..0x400);   // 512 bytes starting at 0x200
long length = reader.Length;              // 512
reader.Position = 0;                      // the first byte of the region

reader.EnableRegionView(^16..);           // the last 16 bytes
reader.DisableRegionView();
```

## Endian Converter

`BinaryEndianConverter` decodes values from a `ReadOnlySpan<byte>`, optionally at an offset. Every overload checks that enough bytes are available and throws `ArgumentOutOfRangeException` otherwise. The single-precision method is `ToFloat`, and `ToGuid` is only available with an offset.

```cs
byte[] buffer = [0x00, 0x00, 0x00, 0x2A, 0x01, 0x02];

int value = BinaryEndianConverter.ToInt32(buffer, EndianType.BIG);              // 42
ushort tail = BinaryEndianConverter.ToUInt16(buffer, offset: 4, EndianType.BIG); // 0x0102
```

## Custom Analyzer

Derive from `Analyzer` to observe or rewrite every block of bytes right after it is read, including the bytes of the inherited `BinaryReader` methods, for example to decrypt content on the fly:

```cs
using Snowberry.IO.Common.Reader;
using Snowberry.IO.Common.Reader.Interfaces;
using Snowberry.IO.Reader;

using var stream = File.OpenRead("encrypted.bin");
using var reader = new EndianStreamReader(stream, new XorAnalyzer(0x5A));

public sealed class XorAnalyzer(byte key) : Analyzer
{
    public override void Initialize(IEndianReader reader)
    {
    }

    public override void AnalyzeReadBytes(IEndianReader reader, Span<byte> buffer, int amount)
    {
        for (int i = 0; i < amount; i++)
            buffer[i] ^= key;
    }
}
```

> **Note:** An analyzer that only observes the bytes can override `ModifiesBytes` to return `false`. `ReadCString`, `ReadSizedCString` and `ReadLine` then search the bytes before they report them, and report the consumed bytes in larger pieces instead of one code unit at a time.

## Process Memory

`Win32ProcessReader(pid, startPosition)` opens a process with `PROCESS_VM_READ` and `PROCESS_QUERY_LIMITED_INFORMATION` access (`c_DefaultAccessFlags`); pass other `accessFlags` for more rights. `Win32ProcessReader(handle, startPosition)` wraps a handle you already hold; set `CloseHandleOnDispose` to `false` when someone else owns it, for example `Process.Handle`.

- **`ReadCString` follows a pointer.** It reads a 4- or 8-byte pointer at `Position`, depending on the bitness of the target, and returns the string it points to, up to `c_MaxCStringByteLength` bytes.
- **`CopyTo` needs a length.** `CopyTo(destination)` always throws `NotSupportedException`; use `CopyTo(destination, length)`.
- **`InjectDLL` loads a library.** `InjectDLL(filePath)` runs `LoadLibraryW` on a remote thread. It needs `PROCESS_CREATE_THREAD`, `PROCESS_VM_OPERATION` and `PROCESS_VM_WRITE` access and a target with the same bitness.

## Supported Data Types

| Type    | Endian type(s)                  |
| ------- | ------------------------------- |
| Boolean | -                               |
| Char    | -                               |
| Int8    | -                               |
| UInt8   | -                               |
| Int16   | Little, Big                     |
| UInt16  | Little, Big                     |
| Int32   | Little, Big                     |
| UInt32  | Little, Big                     |
| Int64   | Little, Big                     |
| UInt64  | Little, Big                     |
| Half    | Little, Big (.NET 10 build)     |
| Single  | Little, Big                     |
| Double  | Little, Big                     |
| Decimal | Little (inherited)              |
| Guid    | Little, Big                     |
| Sha1    | -                               |

## Pitfalls

- **Writer and reader must agree on the encoding.** `EndianStreamReader` and `EndianStreamWriter` default to UTF-8 without BOM on all targets. Pass the same explicit `Encoding` to both when the data uses another encoding. `Win32ProcessReader` always decodes UTF-8.
- **Alignment must be a power of two.** `ReadAlignment` and `WritePadding` throw `ArgumentOutOfRangeException` for `0` or values such as `3` or `6`.
- **`Length` and `Position` need a seekable stream.** Plain reads and the `TryRead*` methods work on non-seekable streams, but `Length`, `Position`, region views, `ReadCString`, `ReadSizedCString`, `ReadLine`, `ReadUntilEnd`, `ReadAlignment` and `CopyTo` require `Stream.CanSeek`.
- **A failed fixed-size read consumes the available bytes.** Like `BinaryReader`, `ReadInt32()` and the other fixed-size reads throw `EndOfStreamException` at the end of the data and leave `Position` after the bytes that were there. Use the `TryRead*` methods or `ReadExactly(byte[], int, int)` to keep the position on a seekable stream.
- **`ReadCString` needs a terminator.** A string that runs to the end of the data throws `EndOfStreamException` instead of returning a partial value.
- **`WriteLine` writes `Environment.NewLine`.** The line ending differs between Windows and other platforms; `ReadLine` accepts CR LF, LF and CR.
- **`Win32ProcessReader` has no length.** Without a region view its `Length` is `0`, so use `ReadCString` (which follows a pointer) and `CopyTo(destination, length)` rather than length-based methods.
- **Readers are not thread-safe.** Use one reader per thread, or synchronize access.

## Related Packages

| Package                                                                                                               | Purpose                                                                        |
| --------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------ |
| [Snowberry.IO.Common](https://github.com/snowberry-software/Snowberry.IO/tree/master/src/Snowberry.IO.Common)         | Reader and writer contracts, `BinaryEndianConverter`, `Sha1` and `EndianType`. |
| [Snowberry.IO.SingleFile](https://github.com/snowberry-software/Snowberry.IO/tree/master/src/Snowberry.IO.SingleFile) | Reads and modifies bundles of single-file published .NET applications.         |
