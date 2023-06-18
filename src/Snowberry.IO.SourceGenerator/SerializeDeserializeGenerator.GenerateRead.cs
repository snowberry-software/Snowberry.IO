using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Snowberry.IO.SourceGenerator;

internal partial class SerializeDeserializeGenerator
{
    public const string ReadLocalOffsetName = "offset";
    public const string ReadReaderParamName = "reader";
    public const string ReadMethodName = "Read";
    public const string ReadDefaultEndianParamName = "defaultEndianType";

    internal void GenerateReadMethod(GeneratorExecutionContext context, ValidTypeDecl validTypeDecl)
    {
        var sb = new StringBuilder();

        //sb.AppendLine(string.Concat(
        //    $"\tpublic void {ReadMethodName}(",

        //    $"{nameof(IEndianReader)} {ReadReaderParamName}, ",
        //    $"{(generateLocalOffsetVariable ? "" : $"ref int {ReadLocalOffsetName}, ")}",
        //    $"{nameof(EndianType)} {ReadDefaultEndianParamName} = {endianEnumReference}",

        //    ")"
        //));

        string classCode = GetTypeTemplate(validTypeDecl.Symbol, sb.ToString());
        context.AddSource($"{validTypeDecl.Symbol.Name}.g.cs", classCode);
    }

    private string GetTypeTemplate(INamedTypeSymbol type, string content)
    {
        return $$"""

            namespace {{type.ContainingNamespace}}
            {
                {{AccessibilityToAccessModifier(type.DeclaredAccessibility)}} partial {{(type.IsValueType ? "struct" : "class")}} {{type.Name}}
                {
                    {{content}}
                }
            }
            """;
    }

    static string AccessibilityToAccessModifier(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Internal or Accessibility.Private => "internal",
            _ => "public"
        };
    }
}
