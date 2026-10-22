using System.Text.RegularExpressions;
using ArchUnitNET.Fluent.Syntax.Elements.Types;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using Snowberry.IO.SingleFile;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Snowberry.IO.Tests.Architecture;

/// <summary>
/// Contains the layering rules between the namespaces of Snowberry.IO.SingleFile.
/// </summary>
public class SingleFileArchitectureTests
{
    private const string c_Content = c_Root + ".Content";
    private const string c_Format = c_Root + ".Format";
    private const string c_Hosts = c_Format + ".Hosts";
    private const string c_Layout = c_Format + ".Layout";
    private const string c_Manifests = c_Format + ".Manifests";
    private const string c_Root = "Snowberry.IO.SingleFile";
    private const string c_Sources = c_Root + ".Sources";
    private const string c_Streams = c_Root + ".Streams";
    private const string c_Threading = c_Root + ".Threading";
    private const string c_Writing = c_Format + ".Writing";

    private static readonly ArchUnitNET.Domain.Architecture s_Architecture = new ArchLoader()
        .LoadAssemblies(typeof(SingleFileBundle).Assembly)
        .Build();

    /// <summary>
    /// Selects the types declared directly in a namespace, excluding its child namespaces.
    /// </summary>
    /// <param name="ns">The full namespace name.</param>
    /// <returns>The type selection for the namespace.</returns>
    private static GivenTypesConjunction InNamespace(string ns)
    {
        return Types().That().ResideInNamespace(ns);
    }

    /// <summary>
    /// Selects the types declared in a namespace or in any of its child namespaces.
    /// </summary>
    /// <param name="ns">The full namespace name.</param>
    /// <returns>The type selection for the namespace and its children.</returns>
    private static GivenTypesConjunction InNamespaceTree(string ns)
    {
        return Types().That().ResideInNamespaceMatching("^" + Regex.Escape(ns) + @"(\..+)?$");
    }

    [Theory]
    [InlineData(c_Hosts, c_Layout)]
    [InlineData(c_Hosts, c_Writing)]
    [InlineData(c_Manifests, c_Layout)]
    [InlineData(c_Manifests, c_Writing)]
    [InlineData(c_Layout, c_Writing)]
    public void FormatLayer_DoesNotDependOn_HigherFormatLayer(string layer, string higherLayer)
    {
        InNamespace(layer).Should().NotDependOnAny(InNamespace(higherLayer)).Check(s_Architecture);
    }

    [Theory]
    [InlineData(c_Sources, c_Content)]
    [InlineData(c_Sources, c_Format)]
    [InlineData(c_Sources, c_Streams)]
    [InlineData(c_Sources, c_Threading)]
    [InlineData(c_Streams, c_Content)]
    [InlineData(c_Streams, c_Format)]
    [InlineData(c_Streams, c_Threading)]
    [InlineData(c_Threading, c_Content)]
    [InlineData(c_Threading, c_Format)]
    [InlineData(c_Threading, c_Sources)]
    [InlineData(c_Threading, c_Streams)]
    [InlineData(c_Content, c_Format)]
    [InlineData(c_Format, c_Content)]
    public void Namespace_DoesNotDependOn_OtherNamespaceTree(string source, string target)
    {
        InNamespace(source).Should().NotDependOnAny(InNamespaceTree(target)).Check(s_Architecture);
    }

    [Theory]
    [InlineData(c_Sources)]
    [InlineData(c_Streams)]
    [InlineData(c_Threading)]
    [InlineData(c_Content)]
    [InlineData(c_Format)]
    [InlineData(c_Hosts)]
    [InlineData(c_Layout)]
    public void Namespace_DoesNotDependOn_RootNamespace(string source)
    {
        InNamespace(source).Should().NotDependOnAny(InNamespace(c_Root)).Check(s_Architecture);
    }
}