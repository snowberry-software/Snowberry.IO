using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Snowberry.IO.SingleFile.Format;

/// <summary>
/// Validation of bundle entry paths, which use <c>/</c> as the separator and come from untrusted files.
/// </summary>
internal static class BundlePath
{
    /// <summary>
    /// The maximum UTF-8 length of an entry path the .NET host accepts on Linux and macOS (<c>PATH_MAX</c>).
    /// </summary>
    public const int c_MaxUnixPathBytes = 4096;

    /// <summary>
    /// The maximum UTF-8 length of an entry path the .NET host accepts on Windows (<c>MAX_PATH</c>).
    /// </summary>
    public const int c_MaxWindowsPathBytes = 260;

    /// <summary>
    /// Resolves the full path of an extraction directory once for <see cref="TryGetSafeOutputPathUnder"/>.
    /// </summary>
    /// <param name="rootDirectory">The directory extracted files must stay inside of.</param>
    /// <returns>The full path of <paramref name="rootDirectory"/>, ending in a directory separator.</returns>
    /// <exception cref="ArgumentException"><paramref name="rootDirectory"/> is not a valid path.</exception>
    /// <exception cref="PathTooLongException"><paramref name="rootDirectory"/> is too long.</exception>
    public static string GetRootPrefix(string rootDirectory)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));

        // A file system root such as C:\ or / keeps its separator when trimmed.
        return Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
    }

    /// <summary>
    /// Determines whether <paramref name="path"/> is rooted on Windows or on Unix, regardless of the current platform.
    /// </summary>
    /// <param name="path">The path to test.</param>
    /// <returns><see langword="true"/> if the path is rooted on either platform; otherwise, <see langword="false"/>.</returns>
    public static bool IsRootedOnAnyPlatform(string path)
    {
        if (Path.IsPathRooted(path))
            return true;

        if (path.Length > 0 && (path[0] == '/' || path[0] == '\\'))
            return true;

        // "C:\x" or "C:/x", which Path.IsPathRooted does not recognize on Unix.
        return path.Length >= 2 && path[1] == ':' && char.IsAsciiLetter(path[0]);
    }

    /// <summary>
    /// Converts a caller-provided path into a bundle entry path.
    /// </summary>
    /// <param name="relativePath">The path to normalize; <c>\</c> is converted to <c>/</c>.</param>
    /// <param name="maxUtf8Length">The maximum UTF-8 length the target host accepts.</param>
    /// <param name="parameterName">The parameter name reported by thrown exceptions.</param>
    /// <returns>The normalized path.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="relativePath"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="relativePath"/> is empty, too long, rooted, contains a NUL character, or contains an empty, <c>.</c> or <c>..</c> segment.
    /// </exception>
    public static string Normalize(string relativePath, int maxUtf8Length, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(relativePath, parameterName);

        string normalized = relativePath.Replace('\\', '/');

        if (normalized.Length == 0 || normalized.Contains('\0') || IsRootedOnAnyPlatform(normalized))
            throw new ArgumentException($"'{relativePath}' is not a valid relative path.", parameterName);

        if (Encoding.UTF8.GetByteCount(normalized) > maxUtf8Length)
            throw new ArgumentException($"'{relativePath}' is longer than the {maxUtf8Length} bytes the application host accepts.", parameterName);

        foreach (string segment in normalized.Split('/'))
        {
            if (segment is "" or "." or "..")
                throw new ArgumentException($"'{relativePath}' is not a valid relative path; it contains an empty, '.' or '..' segment.", parameterName);
        }

        return normalized;
    }

    /// <summary>
    /// Resolves the file an entry is extracted to and verifies that it lies inside <paramref name="rootDirectory"/>.
    /// </summary>
    /// <remarks>
    /// Rejects empty, rooted (on any platform) and NUL-containing paths, paths that resolve to <paramref name="rootDirectory"/> or outside of it,
    /// and paths that resolve to a directory. Paths that Windows maps to a device, such as <c>NUL</c>, resolve outside of the directory and are
    /// rejected; which names Windows maps depends on its version, for example <c>con.txt</c> is a device before Windows 11 and a file since. On
    /// Windows it also rejects <c>:</c> (alternate data streams) and segments ending in <c>.</c> or a space, which Windows silently trims.
    /// Returns <see langword="false"/> instead of throwing when a path cannot be resolved.
    /// </remarks>
    /// <param name="rootDirectory">The directory extracted files must stay inside of.</param>
    /// <param name="relativePath">The untrusted entry path.</param>
    /// <param name="fullPath">When this method returns <see langword="true"/>, the resolved full path; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the path is safe; otherwise, <see langword="false"/>.</returns>
    public static bool TryGetSafeOutputPath(string rootDirectory, string relativePath, [NotNullWhen(true)] out string? fullPath)
    {
        string rootPrefix;

        try
        {
            rootPrefix = GetRootPrefix(rootDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            fullPath = null;
            return false;
        }

        return TryGetSafeOutputPathUnder(rootPrefix, relativePath, out fullPath);
    }

    /// <summary>
    /// Resolves the file an entry is extracted to and verifies that it lies inside the directory of <paramref name="rootPrefix"/>.
    /// </summary>
    /// <remarks>Applies the rules of <see cref="TryGetSafeOutputPath"/>.</remarks>
    /// <param name="rootPrefix">The extraction directory, from <see cref="GetRootPrefix"/>.</param>
    /// <param name="relativePath">The untrusted entry path.</param>
    /// <param name="fullPath">When this method returns <see langword="true"/>, the resolved full path; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the path is safe; otherwise, <see langword="false"/>.</returns>
    public static bool TryGetSafeOutputPathUnder(string rootPrefix, string relativePath, [NotNullWhen(true)] out string? fullPath)
    {
        fullPath = null;

        if (string.IsNullOrEmpty(relativePath) || relativePath.Contains('\0') || IsRootedOnAnyPlatform(relativePath))
            return false;

        if (OperatingSystem.IsWindows() && !IsValidWindowsPath(relativePath))
            return false;

        string candidate;

        try
        {
            candidate = Path.GetFullPath(Path.Combine(rootPrefix, relativePath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        // Windows resolves a device name to a device path such as \\.\NUL, which is not under the root.
        if (Path.EndsInDirectorySeparator(candidate) || !candidate.StartsWith(rootPrefix, comparison))
            return false;

        fullPath = candidate;
        return true;
    }

    private static bool IsValidWindowsPath(string relativePath)
    {
        if (relativePath.Contains(':'))
            return false;

        foreach (string segment in relativePath.Split('/', '\\'))
        {
            if (segment.Length > 0 && (segment[^1] == '.' || segment[^1] == ' '))
                return false;
        }

        return true;
    }
}