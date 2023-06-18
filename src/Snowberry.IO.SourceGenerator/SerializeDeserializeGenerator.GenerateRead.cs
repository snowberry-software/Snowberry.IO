using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Snowberry.IO.Common;
using Snowberry.IO.Common.Reader.Interfaces;
using Snowberry.IO.Common.Writer.Interfaces;

namespace Snowberry.IO.SourceGenerator;

internal partial class SerializeDeserializeGenerator
{
    public const string ReadLocalOffsetName = "offset";
    public const string ReadReaderParamName = "reader";
    public const string ReadMethodName = "Read";

    public const string ReadDefaultEndianParamName = "defaultEndianType";
    public const string ReadDefaultEndianParamValue = $"{nameof(EndianType)}.{nameof(EndianType.LITTLE)}";

    internal void GenerateReadMethod(GeneratorExecutionContext context, ValidTypeDecl validTypeDecl)
    {
        bool generateLocalOffsetVariable = true;
        var sb = new StringBuilder();

        sb.AppendLine(string.Concat(
            $"\tpublic void {ReadMethodName}(",

            $"{nameof(IEndianReader)} {ReadReaderParamName}, ",
            $"{(generateLocalOffsetVariable ? "" : $"ref int {ReadLocalOffsetName}, ")}",
            $"{nameof(EndianType)} {ReadDefaultEndianParamName} = {ReadDefaultEndianParamValue}",

            ")"
        ));

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

    private static string GetUsings()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"using {typeof(int).Namespace};");
        sb.AppendLine($"using {typeof(IEndianReader).Namespace};");
        sb.AppendLine($"using {typeof(IEndianWriter).Namespace};");
        sb.AppendLine($"using {typeof(EndianType).Namespace};");
        sb.AppendLine($"using {typeof(Sha1).Namespace};");
        sb.AppendLine($"using Snowberry.IO.Extensions;\n");

        return sb.ToString();
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
