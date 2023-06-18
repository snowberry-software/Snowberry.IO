using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Snowberry.IO.SourceGenerator.Attributes;

namespace Snowberry.IO.SourceGenerator;

[Generator]
internal partial class SerializeDeserializeGenerator : ISourceGenerator
{
    /// <inheritdoc/>
    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForPostInitialization(pctx => pctx.AddSource($"{nameof(BinarySerializeAttribute)}.g.cs", BinarySerializeAttribute.GetCode()));
        context.RegisterForSyntaxNotifications(() => new DeclarationReceiver());
    }

    /// <inheritdoc/>
    public void Execute(GeneratorExecutionContext context)
    {
        if (context.SyntaxReceiver is not DeclarationReceiver receiver || receiver.TypeDeclarationSyntaxes.Count == 0)
            return;

#if DEBUG
        if (!Debugger.IsAttached)
            Debugger.Launch();
#endif
        var validDecls = new List<ValidTypeDecl>();
        for (int j = 0; j < receiver.TypeDeclarationSyntaxes.Count; j++)
        {
            var typeDeclSyntax = receiver.TypeDeclarationSyntaxes[j];
            var typeSematicModel = context.Compilation.GetSemanticModel(typeDeclSyntax.SyntaxTree);
            var typeSymbol = typeSematicModel.GetDeclaredSymbol(typeDeclSyntax);

            if (typeSymbol is IErrorTypeSymbol)
                continue;

            if (typeSymbol.ContainingNamespace.ToString().StartsWith(AttributeMetadata.c_Namespace))
                continue;

            var attributes = typeSymbol.GetAttributes();

            var typeDeclSettings = new TypeDeclSettings(AllowBufferedReading: false);

            bool isValid = false;
            for (int i = 0; i < attributes.Length; i++)
            {
                var attributeData = attributes[i];

                if (attributeData.AttributeClass == null)
                    continue;

                if (attributeData.AttributeClass.ToString() != BinarySerializeAttribute.c_FullName)
                    continue;

                if (attributeData.ApplicationSyntaxReference.GetSyntax() is not AttributeSyntax attributeSyntax)
                    continue;

                if (attributeSyntax.ArgumentList == null)
                    continue;

                for (int k = 0; k < attributeSyntax.ArgumentList.Arguments.Count; k++)
                {
                    var argument = attributeSyntax.ArgumentList.Arguments[k];
                    var constant = typeSematicModel.GetConstantValue(argument.Expression);

                    if (!constant.HasValue)
                        continue;

                    if (constant.Value is not bool allowBufferedReading)
                        continue;

                    typeDeclSettings = typeDeclSettings with
                    {
                        AllowBufferedReading = allowBufferedReading
                    };
                }

                isValid = true;
                break;
            }

            if (!isValid)
                continue;

            if (!typeDeclSyntax.Modifiers.Any(x => x.IsKind(SyntaxKind.PartialKeyword)))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(TypeNotMarkedAsPartialDiagnostic,
                    typeDeclSyntax.Identifier.GetLocation(),
                    typeSymbol.ToDisplayString()));

                continue;
            }

            validDecls.Add(new(
                typeSymbol.ToString(), 
                typeSymbol, 
                typeDeclSyntax, 
                typeDeclSettings));
        }

        for (int i = 0; i < validDecls.Count; i++)
        {
            var validDecl = validDecls[i];
            var typeSymbol = validDecl.Symbol;
            var typeSettings = validDecl.TypeDeclSettings;

            var properties = new List<IPropertySymbol>();

            foreach (var member in typeSymbol.GetMembers())
            {
                if (member is not IPropertySymbol propertySymbol)
                    continue;

                if (propertySymbol.SetMethod == null)
                    continue;

                properties.Add(propertySymbol);
            }


        }
    }
}

internal class DeclarationReceiver : ISyntaxReceiver
{
    /// <inheritdoc/>
    public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
    {
        if (syntaxNode.IsKind(SyntaxKind.ClassDeclaration))
            TypeDeclarationSyntaxes.Add((ClassDeclarationSyntax)syntaxNode);

        if (syntaxNode.IsKind(SyntaxKind.StructDeclaration))
            TypeDeclarationSyntaxes.Add((StructDeclarationSyntax)syntaxNode);
    }

    public List<TypeDeclarationSyntax> TypeDeclarationSyntaxes { get; } = new();
}

internal record ValidTypeDecl(
    string FullName,
    INamedTypeSymbol Symbol,
    TypeDeclarationSyntax TypeDeclarationSyntax,
    TypeDeclSettings TypeDeclSettings
);

internal record TypeDeclSettings(
    bool AllowBufferedReading
);