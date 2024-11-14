using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Snowberry.IO.Common;

namespace Snowberry.IO.SourceGenerator;

[Generator(LanguageNames.CSharp)]
public partial class BinaryModelGenerator : IIncrementalGenerator
{
    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
#if DEBUG
        //Debugger.Launch();
#endif

        context.RegisterPostInitializationOutput(static context =>
        {
            context.AddSource($"{c_BinarySerializationAttributeName}.g.cs", SourceText.From($$"""
                using System;
                using {{typeof(EndianType).Namespace}};

                namespace {{c_CustomNamespace}}
                {
                    // Generated at: {{DateTime.UtcNow.ToString("yyyy-MM-dd")}}

                    /// <summary>
                    /// Enables the generation of the binary model.
                    /// </summary>
                    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
                    internal sealed class {{c_BinarySerializationAttributeName}} : Attribute
                    {
                        public {{c_BinarySerializationAttributeName}}(uint version) 
                        {
                            if (Version < 1)
                                throw new ArgumentOutOfRangeException(nameof(version), "Version must be greater than or equal to 1.");

                            Version = version;
                        }

                        /// <summary>
                        /// The current version of the binary model.
                        /// </summary>
                        public uint Version { get; }
                    }

                    /// <summary>
                    /// Used to exclude properties from the binary serialization.
                    /// </summary>
                    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
                    internal sealed class {{c_BinaryIgnoreAttributeName}} : Attribute
                    {
                        public {{c_BinaryIgnoreAttributeName}}() 
                        {
                        }
                    }

                    /// <summary>
                    /// Used to modify property metadata used in the binary serialization.
                    /// </summary>
                    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
                    internal sealed class {{c_BinaryPropertyAttributeName}} : Attribute
                    {
                        public {{c_BinaryPropertyAttributeName}}()
                        {
                        }

                        /// <summary>
                        /// The minimum supported version of the binary model where the property is present.
                        /// </summary>
                        public uint {{BinaryPropertyConstants.c_MinimumVersionPropertyName}} { get; set; } = 1;

                        /// <summary>
                        /// The maximum supported version of the binary model where the property is present.
                        /// </summary>
                        public uint {{BinaryPropertyConstants.c_MaximumVersionPropertyName}} { get; set; } = uint.MaxValue;

                        /// <summary>
                        /// The endian type to use.
                        /// </summary>
                        public {{nameof(EndianType)}} {{BinaryPropertyConstants.c_EndianTypePropertyName}} { get; set; }

                        /// <summary>
                        /// Specifies a sorting index.
                        /// </summary>
                        /// <remarks>Ascending sorting is used.</remarks>
                        public uint {{BinaryPropertyConstants.c_IndexPropertyName}} { get; set; }
                    }
                }

                """, Encoding.UTF8));
        });

        //context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());

        var compilation = context.CompilationProvider;

        var binarySerializationAttribute = compilation.Select(static (c, _) => c.GetTypeByMetadataName($"{c_CustomNamespace}.{c_BinarySerializationAttributeName}")!);
        var binaryIgnoreAttribute = compilation.Select(static (c, _) => c.GetTypeByMetadataName($"{c_CustomNamespace}.{c_BinaryIgnoreAttributeName}")!);
        var binaryPropertyAttribute = compilation.Select(static (c, _) => c.GetTypeByMetadataName($"{c_CustomNamespace}.{c_BinaryPropertyAttributeName}")!);

        var validatedTypeDeclarationProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName($"{c_CustomNamespace}.{c_BinarySerializationAttributeName}",
                static (n, _) => n is TypeDeclarationSyntax,
                (syntaxContext, _) => ((TypeDeclarationSyntax)syntaxContext.TargetNode, (INamedTypeSymbol)syntaxContext.TargetSymbol))
            .Combine(binarySerializationAttribute)
            .Select((combined, _) =>
            {
                var (pair, binarySerializationAttribute) = combined;

                var namedTypeSymbol = pair.Item2;

                if (binarySerializationAttribute == null)
                    return null;

                // Check for the presence of the binarySerializationAttribute
                AttributeData? binarySerializationAttributeData = null;
                var attributes = namedTypeSymbol.GetAttributes();
                binarySerializationAttributeData = attributes
                    .FirstOrDefault(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, binarySerializationAttribute));

                if (binarySerializationAttributeData == null)
                    return null;

                object? constructorArgValue = binarySerializationAttributeData.ConstructorArguments.FirstOrDefault().Value;
                uint currentVersion = constructorArgValue is uint cur ? cur : 0;

                return new ValidatedTypeDecl
                {
                    TypeDeclaration = pair.Item1,
                    TypeSymbol = namedTypeSymbol,
                    CurrentVersion = currentVersion
                };
            }).Where(x => x != null);

        context.RegisterSourceOutput(validatedTypeDeclarationProvider.Collect(), static (sourceProductionContext, validatedTypeDeclarations) =>
        {
            for (int i = 0; i < validatedTypeDeclarations.Length; i++)
            {
                var validatedTypeDeclaration = validatedTypeDeclarations[i];

                if (validatedTypeDeclaration == null)
                    continue;

                Generate(sourceProductionContext, validatedTypeDeclaration);
            }
        });
    }

    /// <inheritdoc/>
    //public void Execute(GeneratorExecutionContext context)
    //{
    //    if (context.SyntaxReceiver is not SyntaxReceiver receiver)
    //        return;

    //    var compilation = context.Compilation;
    //    var binarySerializationAttribute = compilation.GetTypeByMetadataName($"{c_CustomNamespace}.{c_BinarySerializationAttributeName}");
    //    var binaryIgnoreAttribute = compilation.GetTypeByMetadataName($"{c_CustomNamespace}.{c_BinaryIgnoreAttributeName}");
    //    var binaryPropertyAttribute = compilation.GetTypeByMetadataName($"{c_CustomNamespace}.{c_BinaryPropertyAttributeName}");

    //    if (binarySerializationAttribute is null)
    //        throw new InvalidProgramException($"Could not find `{c_BinarySerializationAttributeName}` attribute!");

    //    if (binaryIgnoreAttribute is null)
    //        throw new InvalidProgramException($"Could not find `{c_BinaryIgnoreAttributeName}` attribute!");

    //    if (binaryPropertyAttribute is null)
    //        throw new InvalidProgramException($"Could not find `{c_BinaryPropertyAttributeName}` attribute!");

    //    var validTypeDeclarations = new List<ValidatedTypeDecl>();

    //    for (int i = 0; i < receiver.TypeDeclarations.Count; i++)
    //    {
    //        var typeDeclaration = receiver.TypeDeclarations[i];

    //        var model = context.Compilation.GetSemanticModel(typeDeclaration.SyntaxTree);

    //        if (model.GetDeclaredSymbol(typeDeclaration) is not INamedTypeSymbol symbol)
    //            continue;

    //        AttributeData? binarySerializationAttributeData = null;
    //        if ((binarySerializationAttributeData = symbol.GetAttributes().FirstOrDefault(x => binarySerializationAttribute.Equals(x.AttributeClass, SymbolEqualityComparer.Default))) == null)
    //            continue;

    //        object? constructorArgValue = binarySerializationAttributeData.ConstructorArguments.FirstOrDefault().Value;
    //        uint currentVersion = constructorArgValue is uint cur ? cur : 0;

    //        validTypeDeclarations.Add(new()
    //        {
    //            SemanticModel = model,
    //            TypeDeclaration = typeDeclaration,
    //            TypeSymbol = symbol,
    //            CurrentVersion = currentVersion
    //        });
    //    }

    //    for (int i = 0; i < validTypeDeclarations.Count; i++)
    //    {
    //        var validTypeDeclaration = validTypeDeclarations[i];
    //        Generate(context, binaryIgnoreAttribute, binaryPropertyAttribute, compilation, validTypeDeclaration);
    //    }
    //}
}

internal class SyntaxReceiver : ISyntaxReceiver
{
    /// <inheritdoc/>
    public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
    {
        if (syntaxNode is ClassDeclarationSyntax classDeclarationSyntax)
        {
            TypeDeclarations.Add(classDeclarationSyntax);
            return;
        }

        if (syntaxNode is StructDeclarationSyntax structDeclarationSyntax)
        {
            TypeDeclarations.Add(structDeclarationSyntax);
            return;
        }
    }

    public List<TypeDeclarationSyntax> TypeDeclarations { get; } = [];
}

internal class ValidatedTypeDecl
{
    public TypeDeclarationSyntax TypeDeclaration { get; set; } = null!;

    public INamedTypeSymbol TypeSymbol { get; set; } = null!;

    public uint CurrentVersion { get; set; }
}