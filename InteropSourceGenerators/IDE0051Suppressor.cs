using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

namespace InteropSourceGenerators;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class IDE0051Suppressor : DiagnosticSuppressor
{
    private const string SuppressedDiagnosticId = "IDE0051"; // Remove unused private members

    private static readonly string[] ReflectionBasedAttributeNames =
    [
        "TweakEventAttribute",
        "CommandHandlerAttribute"
    ];

    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions { get; } =
        ImmutableArray.Create(
            new SuppressionDescriptor(
                $"{nameof(IDE0051Suppressor)}",
                SuppressedDiagnosticId,
                $"Methods with {string.Join(", ", ReflectionBasedAttributeNames.Select(x => $"[{x.Replace("Attribute", "")}]"))} attributes are used via reflection and should not be flagged as unused"
            )
        );

    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        foreach (var diagnostic in context.ReportedDiagnostics)
        {
            if (diagnostic.Id != SuppressedDiagnosticId)
                continue;

            var location = diagnostic.Location;
            if (location.SourceTree == null)
                continue;

            var root = location.SourceTree.GetRoot(context.CancellationToken);
            var node = root.FindNode(location.SourceSpan);

            if (node.FirstAncestorOrSelf<MethodDeclarationSyntax>() is not { } methodDeclaration)
                continue;

            var semanticModel = context.GetSemanticModel(location.SourceTree);
            if (semanticModel.GetDeclaredSymbol(methodDeclaration, context.CancellationToken) is not IMethodSymbol methodSymbol)
                continue;

            var hasReflectionBasedAttribute = methodSymbol.GetAttributes()
                .Any(attr =>
                {
                    if (attr.AttributeClass == null)
                        return false;

                    var attributeClassName = attr.AttributeClass.Name;
                    return ReflectionBasedAttributeNames.Contains(attributeClassName);
                });

            if (hasReflectionBasedAttribute)
                context.ReportSuppression(Suppression.Create(SupportedSuppressions[0], diagnostic));
        }
    }
}

