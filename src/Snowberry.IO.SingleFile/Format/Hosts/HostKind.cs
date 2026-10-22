namespace Snowberry.IO.SingleFile.Format.Hosts;

/// <summary>
/// The executable format of an application host.
/// </summary>
internal enum HostKind
{
    /// <summary>The format was not recognized.</summary>
    Unknown,

    /// <summary>A Windows PE executable.</summary>
    PortableExecutable,

    /// <summary>A Linux ELF executable.</summary>
    Elf,

    /// <summary>A single-architecture macOS Mach-O executable.</summary>
    MachO,

    /// <summary>A universal (fat) macOS Mach-O executable.</summary>
    UniversalMachO
}