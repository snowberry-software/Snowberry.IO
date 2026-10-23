namespace Snowberry.IO.Tests.Infrastructure.SingleFile;

/// <summary>
/// The executable format of a synthetic application host built by <see cref="SyntheticBundleBuilder"/>.
/// </summary>
public enum TestHost
{
    /// <summary>A Windows PE header with one section and an empty security directory.</summary>
    Pe,

    /// <summary>A 64-bit x86-64 ELF header.</summary>
    Elf,

    /// <summary>A 64-bit Arm64 Mach-O header.</summary>
    MachO,

    /// <summary>A universal (fat) Mach-O header.</summary>
    UniversalMachO
}