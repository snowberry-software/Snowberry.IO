﻿namespace Snowberry.IO.Common;

/// <summary>
/// Byte ordering type.
/// </summary>
public enum EndianType
{
    /// <summary>
    /// Bytes are stored with the least significant byte first.
    /// </summary>
    LITTLE,

    /// <summary>
    /// Bytes are stored with the most significant byte first.
    /// </summary>
    BIG
}