using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using AwesomeAssertions;
using Snowberry.IO.Common;
using Snowberry.IO.Reader;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Snowberry.IO.Tests.Architecture;

/// <summary>
/// Contains the dependency and naming rules between the Snowberry.IO assemblies, and the namespace rule for every project.
/// </summary>
public class ArchitectureTests
{
    private const string c_SingleFileNamespacePattern = @"^Snowberry\.IO\.SingleFile(\..+)?$";

    private static readonly Assembly s_CommonAssembly = typeof(EndianType).Assembly;
    private static readonly Assembly s_IOAssembly = typeof(EndianStreamReader).Assembly;

    private static readonly ArchUnitNET.Domain.Architecture s_Architecture = new ArchLoader()
        .LoadAssemblies(s_CommonAssembly, s_IOAssembly)
        .Build();


    private static readonly Regex s_NamespaceDeclaration = new(@"^namespace\s+([\w.]+)", RegexOptions.Multiline);

    /// <summary>
    /// Finds the <c>src</c> directory by walking up from the test output directory to the solution file.
    /// </summary>
    /// <returns>The full path of the directory that contains <c>Snowberry.IO.sln</c>.</returns>
    private static string FindSourceDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Snowberry.IO.sln")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not find Snowberry.IO.sln above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void CommonTypes_Always_ResideInCommonNamespace()
    {
        Types().That().ResideInAssembly(s_CommonAssembly).And().DoNotHaveAnyAttributes(typeof(CompilerGeneratedAttribute))
            .Should().ResideInNamespaceMatching(@"^Snowberry\.IO\.Common(\..+)?$")
            .Check(s_Architecture);
    }

    [Fact]
    public void Common_DoesNotDependOn_SingleFile()
    {
        Types().That().ResideInAssembly(s_CommonAssembly)
            .Should().NotDependOnAny(Types().That().ResideInNamespaceMatching(c_SingleFileNamespacePattern))
            .Check(s_Architecture);
    }

    [Fact]
    public void Common_DoesNotDependOn_SnowberryIO()
    {
        Types().That().ResideInAssembly(s_CommonAssembly)
            .Should().NotDependOnAny(Types().That().ResideInAssembly(s_IOAssembly))
            .Check(s_Architecture);
    }

    [Theory]
    [InlineData("Snowberry.IO.Common")]
    [InlineData("Snowberry.IO")]
    [InlineData("Snowberry.IO.SingleFile")]
    [InlineData("Snowberry.IO.Tests")]
    public void Namespaces_ProjectSourceFiles_MatchFolderPaths(string project)
    {
        // ArchUnitNET reads compiled assemblies, which do not record source folders, so this rule scans the source files.
        string projectDirectory = Path.Combine(FindSourceDirectory(), project);
        var mismatches = new List<string>();
        int declarations = 0;

        foreach (string file in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories))
        {
            string relativeDirectory = Path.GetDirectoryName(file)![projectDirectory.Length..]
                .Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            string firstSegment = relativeDirectory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            if (firstSegment is "obj" or "bin")
                continue;

            string expected = relativeDirectory.Length == 0
                ? project
                : project + "." + relativeDirectory.Replace(Path.DirectorySeparatorChar, '.').Replace(Path.AltDirectorySeparatorChar, '.');

            foreach (Match match in s_NamespaceDeclaration.Matches(File.ReadAllText(file)))
            {
                declarations++;

                if (match.Groups[1].Value != expected)
                    mismatches.Add($"{file}: namespace {match.Groups[1].Value}, expected {expected}");
            }
        }

        declarations.Should().BePositive();
        mismatches.Should().BeEmpty();
    }

    [Fact]
    public void SnowberryIOTypes_Always_ResideInSnowberryIONamespace()
    {
        // Snowberry.IO owns Snowberry.IO and its child namespaces, except the ones of Snowberry.IO.Common and Snowberry.IO.SingleFile.
        Types().That().ResideInAssembly(s_IOAssembly).And().DoNotHaveAnyAttributes(typeof(CompilerGeneratedAttribute))
            .Should().ResideInNamespaceMatching(@"^Snowberry\.IO(\.(?!Common(\.|$)|SingleFile(\.|$)).+)?$")
            .Check(s_Architecture);
    }

    [Fact]
    public void SnowberryIO_DoesNotDependOn_SingleFile()
    {
        Types().That().ResideInAssembly(s_IOAssembly)
            .Should().NotDependOnAny(Types().That().ResideInNamespaceMatching(c_SingleFileNamespacePattern))
            .Check(s_Architecture);
    }
}