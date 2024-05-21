﻿using System;

namespace Snowberry.IO.Common.Reader.Interfaces;

public partial interface IEndianReader
{
    /// <summary>
    /// Reads a specified number of bytes from the current stream into a buffer.
    /// </summary>
    /// <param name="buffer">The span of bytes where the read bytes will be stored.</param>
    /// <returns>
    /// The total number of bytes read into the buffer. This can be less than the number
    /// of bytes requested if that many bytes are not currently available, or zero if the end of the stream is reached.
    /// </returns>
    int Read(Span<byte> buffer);
}