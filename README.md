[![License](https://img.shields.io/github/license/snowberry-software/Snowberry.IO)](https://github.com/snowberry-software/Snowberry.IO/blob/master/LICENSE)
[![NuGet Version](https://img.shields.io/nuget/v/Snowberry.IO.svg?logo=nuget)](https://www.nuget.org/packages/Snowberry.IO/)

A binary reader and writer that supports different endian types.

# Usage

## Features

- Reader
    - Read different endian types for [supported data types](#supported-data-types).
    - [Custom analyzer](#custom-analyzer) that can be used to analyze bytes when they were filled into a buffer.
    - Read zero terminated strings (`ReadCString`).
    - Read sized zero terminated strings (`ReadSizedCString`).
    - Read size prefixed strings (`ReadString`).
    - Read lines (`ReadLine`).
    - Read custom `Sha1` type.
    - Read padding.
    - Enable custom region view.
    
- Writer
    - Write different endian types for [supported data types](#supported-data-types).
    - Write zero terminated strings (`WriteCString`).
    - Write sized zero terminated string (`WriteSizedCString`).
    - Write custom `Sha1` type.
    - Write padding.
    - Write lines (`WriteLine`).
    - Write size prefixed strings.
    
- Custom `Sha1` type based on [SHA-1](https://en.wikipedia.org/wiki/SHA-1).

## Most used types

| Name                         | Description                                                                                                   |
| ---------------------------- | ------------------------------------------------------------------------------------------------------------- |
| [Reader](#reader-and-writer) |                                                                                                               |
| BaseEndianReader             | The abstract base type that implements the `IEndianReader` interface.                                         |
| EndianStreamReader           | Used for reading from streams, inherits the `BaseEndianReader` type.                                          |
|                              |                                                                                                               |
| [Writer](#reader-and-writer) |                                                                                                               |
| EndianStreamWriter           | Used for writing into streams, inherits the `BinaryWriter` type and implements the `IEndianWriter` interface. |

## Reader and writer

```cs
var stream = new MemoryStream();
using var writer = new EndianStreamWriter(stream, keepStreamOpen: true);

writer.Write(10,  EndianType.BIG)
      .Write(20L, EndianType.BIG)
      .Write(30F, EndianType.BIG)
      .Write(40u, EndianType.BIG)
      .Write(50d, EndianType.BIG);
      
writer.BaseStream.Position = 0;

using var reader = new EndianStreamReader(stream);

_ = reader.ReadInt32(EndianType.BIG);
_ = reader.ReadLong(EndianType.BIG);
_ = reader.ReadFloat(EndianType.BIG);
_ = reader.ReadUInt32(EndianType.BIG);
_ = reader.ReadDouble(EndianType.BIG);

```

## Custom analyzer

The `Analyzer` abstract type can be inherited and used to monitor each buffer fill operation.

This is useful if a binary file is or has encrypted content.

## Endian converter

The `BinaryEndianConverter` type can be used to convert data in a `Span<byte>` to [all supported data types](#supported-data-types) and also accepts an offset.

```cs
var buffer = new byte[] { ... };
int offset = ...;
var endianType = EndianType.BIG;

_ = BinaryEndianConverter.ToLong(buffer, endianType);
_ = BinaryEndianConverter.ToLong(buffer, offset, endianType);
```

## Supported data types

| Type   | Endian type(s) |
| ------ | -------------- |
| Int8   | -              |
| UInt8  | -              |
| Int16  | Little, Big    |
| UInt16 | Little, Big    |
| Int32  | Little, Big    |
| UInt32 | Little, Big    |
| Int64  | Little, Big    |
| UInt64 | Little, Big    |
| Float  | Little, Big    |
| Double | Little, Big    |
| Guid   | Little, Big    |
| Sha1   | Little         |
