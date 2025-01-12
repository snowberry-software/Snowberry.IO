using System;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Snowberry.IO.Common;
using Snowberry.IO.Common.Interfaces;
using Snowberry.IO.Common.Reader.Interfaces;
using Snowberry.IO.Common.Writer.Interfaces;

namespace Snowberry.IO.SourceGenerator;

public partial class BinaryModelGenerator
{
    internal static void Generate(
        SourceProductionContext context,
        ValidatedTypeDecl validTypeDeclaration)
    {
        var symbol = validTypeDeclaration.TypeSymbol;
        uint typeCurrentVersion = validTypeDeclaration.CurrentVersion;

        var props = symbol.
            GetMembers().
            OfType<IPropertySymbol>().
            Where(x =>
            {
                if (x.IsIndexer || x.IsReadOnly || x.IsStatic || x.IsWriteOnly)
                    return false;

                var attributes = x.GetAttributes();
                if (attributes.FirstOrDefault(x => x.AttributeClass?.Name == c_BinaryPropertyAttributeName && x.AttributeClass?.ContainingNamespace?.ToString() == c_CustomNamespace) == null)
                    return false;

                return true;
            }).Select(x =>
            {
                var result = new MappedProperty()
                {
                    PropertySymbol = x
                };

                var attributeInfo = x.GetAttributes().FirstOrDefault(x => x.AttributeClass?.Name == c_BinaryPropertyAttributeName && x.AttributeClass?.ContainingNamespace?.ToString() == c_CustomNamespace);
                if (attributeInfo != null && attributeInfo.NamedArguments.Length > 0)
                {
                    foreach (var pair in attributeInfo.NamedArguments)
                    {
                        if (pair.Value.Value == null)
                            continue;

                        switch (pair.Key)
                        {
                            case BinaryPropertyConstants.c_MaximumVersionPropertyName:
                                if (pair.Value.Value is uint maxVerVal)
                                    result.MaximumVersion = maxVerVal;
                                break;

                            case BinaryPropertyConstants.c_MinimumVersionPropertyName:
                                if (pair.Value.Value is uint minVerVal)
                                    result.MinimumVersion = minVerVal;
                                break;

                            case BinaryPropertyConstants.c_EndianTypePropertyName:
                                if (Enum.TryParse<EndianType>(pair.Value.Value.ToString(), out var endianVal))
                                    result.EndianType = endianVal;
                                break;

                            case BinaryPropertyConstants.c_IndexPropertyName:
                                if (pair.Value.Value is uint indexVal)
                                    result.Index = indexVal;
                                break;
                        }
                    }
                }

                result.IsPropertyInCurrentTypeVersion = typeCurrentVersion >= result.MinimumVersion && typeCurrentVersion <= result.MaximumVersion;
                return result;
            }).OrderBy(x => x.Index).ToArray();

        uint minimumSupportedVersion = Math.Min(typeCurrentVersion, props.Min(x => x.MinimumVersion));
        uint maximumSupportedVersion = typeCurrentVersion;
        bool isTypeSizeDynamic = props.Any(x => !x.IsPropertyInCurrentTypeVersion);

        var sb = new StringBuilder();

        // Read method
        uint typeSize = 0;
        {
            typeSize = GenerateReadMethod(sb, props, typeCurrentVersion);
        }

        string symbolType = symbol.TypeKind == TypeKind.Struct ? "struct" : "class";
        context.AddSource($"{symbol.Name}.g.cs", SourceText.From($$"""
            #nullable enable
            
            using {{typeof(BinaryUtils).Namespace}};
            using {{typeof(IEndianReader).Namespace}};
            using {{typeof(IEndianWriter).Namespace}};
            using {{typeof(IBinaryModelMetadata).Namespace}};
            using System.Threading.Tasks;

            namespace {{symbol.ContainingNamespace.ToDisplayString()}}
            {
                partial {{symbolType}} {{symbol.Name}} : {{nameof(IBinaryModelMetadata)}}
                {
                    {{sb}}

                    /// <summary>
                    /// The current version of the binary model.
                    /// </summary>
                    public const uint {{BinaryModelConstants.c_CurrentVersionConstantName}} = {{typeCurrentVersion}};

                    /// <summary>
                    /// Determines whether the <see cref="{{nameof(IBinaryModelMetadata.TypeSize)}}"/> is dynamic.
                    /// </summary>
                    public const bool {{BinaryModelConstants.c_IsTypeSizeDynamicConstantName}} = {{(isTypeSizeDynamic ? "true" : "false")}};

                    /// <summary>
                    /// The minimum supported version by the binary model.
                    /// </summary>
                    public const uint {{BinaryModelConstants.c_MinimumSupportedVersionConstantName}} = {{minimumSupportedVersion}};

                    /// <summary>
                    /// The maximum supported version by the binary model.
                    /// </summary>
                    public const uint {{BinaryModelConstants.c_MaximumSupportedVersionConstantName}} = {{maximumSupportedVersion}};

                    /// <summary>
                    /// The type size of the binary model.
                    /// </summary>
                    public const uint c_{{nameof(IBinaryModelMetadata.TypeSize)}} = {{typeSize}};
                    
                    /// <inheritdoc/>
                    public uint {{nameof(IBinaryModelMetadata.Version)}} { get; set; } = {{typeCurrentVersion}};

                    /// <inheritdoc/>
                    public uint {{nameof(IBinaryModelMetadata.CurrentVersion)}} => {{BinaryModelConstants.c_CurrentVersionConstantName}};

                    /// <inheritdoc/>
                    public uint {{nameof(IBinaryModelMetadata.MinimumSupportedVersion)}} => {{BinaryModelConstants.c_MinimumSupportedVersionConstantName}};

                    /// <inheritdoc/>
                    public uint {{nameof(IBinaryModelMetadata.MaximumSupportedVersion)}} => {{BinaryModelConstants.c_MaximumSupportedVersionConstantName}};

                    /// <inheritdoc/>
                    public uint {{nameof(IBinaryModelMetadata.TypeSize)}} => {{typeSize}};

                    /// <inheritdoc/>
                    public bool {{nameof(IBinaryModelMetadata.IsTypeSizeDynamic)}} => {{BinaryModelConstants.c_IsTypeSizeDynamicConstantName}};
                }
            }
            #nullable restore
            """, Encoding.UTF8));
    }

    private static uint GenerateReadMethod(StringBuilder sb, MappedProperty[] props, uint typeCurrentVersion)
    {
        GenerateReadMethod(true);
        sb.Append($"\n\t\t");
        return GenerateReadMethod(false);

        uint GenerateReadMethod(bool withOffsets)
        {
            uint offset = 0;
            const string c_ReaderParamName = "reader";
            const string c_OffsetParamName = "offset";

            if (withOffsets)
            {
                sb.AppendLine($"public bool Read({nameof(IEndianReader)} {c_ReaderParamName}, ref int {c_OffsetParamName})\n\t\t{{");
            }
            else
            {
                sb.AppendLine($"public async Task<bool> ReadAsync({nameof(IEndianReader)} {c_ReaderParamName})\n\t\t{{");
            }

            sb.AppendLine("\t\t\t" + $"_ = {c_ReaderParamName} ?? throw new ArgumentNullException(nameof({c_ReaderParamName}));\n");
            WriteRead(nameof(IBinaryModelMetadata.Version), null, SpecialType.System_UInt32, EndianType.LITTLE);

            sb.AppendLine($"\t\t\t" + $"if ({nameof(IBinaryModelMetadata.Version)} > {BinaryModelConstants.c_CurrentVersionConstantName}) return false;");
            sb.AppendLine($"\t\t\t" + $"if ({nameof(IBinaryModelMetadata.Version)} < {BinaryModelConstants.c_MinimumSupportedVersionConstantName}) return false;\n");

            for (int i = 0; i < props.Length; i++)
            {
                var prop = props[i];
                WriteReadProp(prop);
            }

            WriteOffset();
            sb.AppendLine("\t\t\treturn true;");
            sb.AppendLine("\t\t}");

            return offset;

            void WriteReadProp(MappedProperty prop)
            {
                WriteIfClauseForPropertyVersion(sb, prop, () =>
                {
                    WriteRead(
                        prop.PropertySymbol.Name,
                        prop.PropertySymbol.Type,
                        null,
                        prop.EndianType);
                });
            }

            void WriteRead(string propertyName, ITypeSymbol? typeSymbol, SpecialType? specialType, EndianType endianTypeName)
            {
                specialType ??= typeSymbol!.SpecialType;

                string readMethod = GetReadMethodName(typeSymbol, specialType);
                bool supportsDifferentEndian = SupportsDifferentEndianType(typeSymbol, specialType);
                uint typeSize = GetTypeSize(typeSymbol, specialType);

                WriteOffset();
                sb.AppendLine("\t\t\t" + $"{propertyName} = {c_ReaderParamName}.{readMethod}({(supportsDifferentEndian ? $"{nameof(EndianType)}.{endianTypeName}" : string.Empty)});");

                if (withOffsets)
                    sb.AppendLine("\t\t\t" + $"{c_OffsetParamName} += {typeSize};");

                sb.AppendLine();

                offset += typeSize;
            }

            void WriteOffset() => sb.AppendLine("\t\t\t" + $"// Offset: {offset}");

            string GetReadMethodName(ITypeSymbol? typeSymbol, SpecialType? specialType)
            {
                if (specialType != null)
                {
                    string? result = specialType switch
                    {
                        SpecialType.System_Boolean => nameof(IEndianReader.ReadBool),
                        SpecialType.System_SByte => nameof(IEndianReader.ReadByte),
                        SpecialType.System_Byte => nameof(IEndianReader.ReadByte),
                        SpecialType.System_Int16 => nameof(IEndianReader.ReadInt16),
                        SpecialType.System_UInt16 => nameof(IEndianReader.ReadUInt16),
                        SpecialType.System_Int32 => nameof(IEndianReader.ReadInt32),
                        SpecialType.System_UInt32 => nameof(IEndianReader.ReadUInt32),
                        SpecialType.System_Int64 => nameof(IEndianReader.ReadInt64),
                        SpecialType.System_UInt64 => nameof(IEndianReader.ReadUInt64),
                        SpecialType.System_Double => nameof(IEndianReader.ReadDouble),
                        SpecialType.System_Single => nameof(IEndianReader.ReadFloat),
                        _ => null
                    };

                    if (result != null)
                        return result;
                }

                if (typeSymbol is null)
                    return string.Empty;

                if (typeSymbol.ToDisplayString() == typeof(Guid).FullName)
                    return nameof(IEndianReader.ReadGuid);

                if (typeSymbol.ToDisplayString() == typeof(Sha1).FullName)
                    return nameof(IEndianReader.ReadSha1);

                return string.Empty;
            }
        }
    }

    private static void WriteIfClauseForPropertyVersion(StringBuilder sb, MappedProperty prop, Action innerBody)
    {
        if (!prop.IsPropertyInCurrentTypeVersion)
        {
            if (prop.MaximumVersion == uint.MaxValue)
                sb.AppendLine("\t\t\t" + $"if ({nameof(IBinaryModelMetadata.Version)} >= {prop.MinimumVersion})");
            else
                sb.AppendLine("\t\t\t" + $"if ({nameof(IBinaryModelMetadata.Version)} >= {prop.MinimumVersion} && {nameof(IBinaryModelMetadata.Version)} <= {prop.MaximumVersion})");

            sb.AppendLine("\t\t\t" + "{");
            sb.Append($"\t");
        }

        innerBody();

        if (!prop.IsPropertyInCurrentTypeVersion)
            sb.AppendLine("\t\t\t}");
    }

    private static bool SupportsDifferentEndianType(ITypeSymbol? typeSymbol, SpecialType? specialType)
    {
        bool result = false;
        if (specialType != null)
            result = specialType switch
            {
                SpecialType.System_Int16 => true,
                SpecialType.System_UInt16 => true,
                SpecialType.System_Int32 => true,
                SpecialType.System_UInt32 => true,
                SpecialType.System_Int64 => true,
                SpecialType.System_UInt64 => true,
                SpecialType.System_Double => true,
                _ => false
            };

        if (result || typeSymbol == null)
            return true;

        if (typeSymbol.ToDisplayString() == typeof(Guid).FullName)
            return true;

        return false;
    }

    private static uint GetTypeSize(ITypeSymbol? typeSymbol, SpecialType? specialType)
    {
        if (specialType != null)
        {
            int result = specialType switch
            {
                SpecialType.System_Boolean => 1,
                SpecialType.System_SByte => 1,
                SpecialType.System_Byte => 1,
                SpecialType.System_Int16 => 2,
                SpecialType.System_UInt16 => 2,
                SpecialType.System_Int32 => 4,
                SpecialType.System_UInt32 => 4,
                SpecialType.System_Int64 => 8,
                SpecialType.System_UInt64 => 8,
                SpecialType.System_Double => 8,
                SpecialType.System_Single => 4,
                _ => -1
            };

            if (result != -1)
                return (uint)result;
        }

        if (typeSymbol is null)
            return 0;

        if (typeSymbol.ToDisplayString() == typeof(Guid).FullName)
            return 16;

        if (typeSymbol.ToDisplayString() == typeof(Sha1).FullName)
            return 20;

        return 0;
    }

    private class MappedProperty
    {
        public IPropertySymbol PropertySymbol = null!;
        public uint MinimumVersion = 1;
        public uint MaximumVersion = uint.MaxValue;
        public EndianType EndianType = EndianType.LITTLE;
        public uint Index = 0;
        public bool IsPropertyInCurrentTypeVersion = false;
    }
}
