using System.Runtime.InteropServices;
using System.Text;
using Snowberry.IO.SingleFile;
using static Snowberry.IO.Tests.Infrastructure.SingleFile.SyntheticBundleBuilder;

namespace Snowberry.IO.Tests.Infrastructure.SingleFile;

/// <summary>
/// Contains the sample bundles and helpers shared by the <see cref="SingleFileBundle"/> tests.
/// </summary>
internal static class BundleTestData
{
    /// <summary>
    /// Content that deflate compresses well.
    /// </summary>
    public static readonly byte[] s_Compressible = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("compressible content ", 200)));

    /// <summary>
    /// <see langword="true"/> when the tests run on Windows.
    /// </summary>
    public static readonly bool s_IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// <see langword="true"/> when the file system of the platform usually ignores case.
    /// </summary>
    public static readonly bool s_IsCaseInsensitiveFileSystem = s_IsWindows || RuntimeInformation.IsOSPlatform(OSPlatform.OSX);


    /// <summary>
    /// Deletes <paramref name="directory"/> and its content if it exists.
    /// </summary>
    /// <param name="directory">The directory to delete.</param>
    public static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    /// <summary>
    /// Returns the index of the length prefix of <paramref name="relativePath"/> in the manifest of <paramref name="bundle"/>.
    /// </summary>
    /// <param name="bundle">The bundle bytes.</param>
    /// <param name="relativePath">A path shorter than 128 bytes.</param>
    /// <returns>The index of the one-byte length prefix.</returns>
    /// <exception cref="ArgumentException">The manifest does not contain <paramref name="relativePath"/>.</exception>
    public static int IndexOfPath(byte[] bundle, string relativePath)
    {
        byte[] pathBytes = [(byte)relativePath.Length, .. Encoding.UTF8.GetBytes(relativePath)];
        int index = bundle.AsSpan().LastIndexOf(pathBytes);

        return index > 0 ? index : throw new ArgumentException($"The bundle does not contain the path '{relativePath}'.", nameof(relativePath));
    }

    /// <summary>
    /// Returns the path of a directory under the temporary directory that does not exist yet.
    /// </summary>
    /// <returns>A unique directory path.</returns>
    public static string NewTempDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "snowberry-test-" + Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// Opens a bundle over a read-only copy of <paramref name="bytes"/>.
    /// </summary>
    /// <param name="bytes">The bundle bytes.</param>
    /// <returns>The opened bundle.</returns>
    public static SingleFileBundle OpenBytes(byte[] bytes)
    {
        return SingleFileBundle.Open(new MemoryStream(bytes, writable: false));
    }

    /// <summary>
    /// Rewrites the declared uncompressed size of a version 6 manifest entry.
    /// </summary>
    /// <param name="bundle">The bundle bytes, which are changed in place.</param>
    /// <param name="relativePath">The path of the entry.</param>
    /// <param name="size">The new size.</param>
    /// <returns><paramref name="bundle"/>.</returns>
    public static byte[] PatchEntrySize(byte[] bundle, string relativePath, long size)
    {
        int pathIndex = IndexOfPath(bundle, relativePath);

        // The record stores the offset (8 bytes), the size (8), the compressed size (8) and the type (1) before the path.
        int sizeIndex = pathIndex - 1 - 8 - 8;
        BitConverter.GetBytes(size).CopyTo(bundle, sizeIndex);
        return bundle;
    }

    /// <summary>
    /// Creates <paramref name="length"/> random bytes from a fixed seed.
    /// </summary>
    /// <param name="length">The number of bytes.</param>
    /// <param name="seed">The seed of the random number generator.</param>
    /// <returns>The same bytes for the same arguments.</returns>
    public static byte[] RandomData(int length, int seed = 1)
    {
        byte[] data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    /// <summary>
    /// Builds a version 6 bundle with an assembly, the deps.json and runtimeconfig.json files and a compressed native library.
    /// </summary>
    /// <param name="host">The application host format.</param>
    /// <returns>The bundle bytes.</returns>
    public static byte[] SampleV6(TestHost host = TestHost.Pe)
    {
        return Build(6,
        [
            new("app.dll", RandomData(300), BundleFileType.Assembly),
            new("app.deps.json", "{\"deps\":1}"u8.ToArray(), BundleFileType.DepsJson),
            new("app.runtimeconfig.json", "{\"rc\":1}"u8.ToArray(), BundleFileType.RuntimeConfigJson),
            new("lib/native.dll", s_Compressible, BundleFileType.NativeBinary, Compress: true)
        ], flags: BundleFlags.NetCoreApp3CompatMode, host: host);
    }

    /// <summary>
    /// Saves <paramref name="bundle"/> to memory.
    /// </summary>
    /// <param name="bundle">The bundle to save.</param>
    /// <param name="options">The write options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The saved bytes.</returns>
    public static byte[] SaveToBytes(SingleFileBundle bundle, BundleWriteOptions? options = null)
    {
        using var output = new MemoryStream();
        bundle.Save(output, options);
        return output.ToArray();
    }
}