using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using static Snowberry.IO.SourceGenerator.Attributes.AttributeMetadata;

namespace Snowberry.IO.SourceGenerator.Attributes;

/// <summary>
/// Indicates that a type can be serialized using binary serialization.
/// </summary>
internal static class BinarySerializeAttribute
{
    public const string c_Name = $"{nameof(BinarySerializeAttribute)}";
    public const string c_FullName = $"{c_Namespace}.{c_Name}";

    public static string GetCode()
    {
        return $$"""

            using System;

            namespace {{c_Namespace}}
            {
                /// <summary>
                /// Indicates that a type can be serialized using binary serialization. This class cannot be inherited.
                /// </summary>
                [AttributeUsage(validOn: AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
                internal sealed class {{c_Name}} : {{nameof(Attribute)}}
                {
                    public {{c_Name}}(bool allowBufferedReading)
                    {
                        AllowBufferedReading = allowBufferedReading;
                    }

                    public bool AllowBufferedReading { get; set; }
                }
            }
            """;
    }

    [AttributeUsage(validOn: AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    private class AttributeDataAttribute : Attribute
    {
    }
}