using InteropSourceGenerators.Extensions;
using InteropSourceGenerators.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.CodeDom.Compiler;

namespace InteropSourceGenerators;

[Generator]
internal sealed class HookGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var hookInfos =
            context.SyntaxProvider.ForAttributeWithMetadataName(
                "ComplexTweaks.Attributes.HookAttribute",
                static (node, _) => node is MethodDeclarationSyntax { AttributeLists.Count: > 0 },
                static (context, token) =>
                {
                    var classSyntax = (ClassDeclarationSyntax)context.TargetNode.Parent!;

                    var hierarchy = new List<string>();
                    for (var parent = context.TargetSymbol.ContainingType; parent is not null; parent = parent.ContainingType)
                        hierarchy.Add(parent.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));

                    var methodSyntax = (MethodDeclarationSyntax)context.TargetNode;
                    var methodSymbol = (IMethodSymbol)context.TargetSymbol;

                    return new HookInfo(
                        new ClassInfo(
                            context.TargetSymbol.ContainingType.ToDisplayString(),
                            methodSymbol.ContainingNamespace.ToDisplayString(
                                new SymbolDisplayFormat(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces)),
                            [.. hierarchy]
                        ),
                        new MethodInfo(
                            methodSymbol.Name,
                            methodSyntax.Modifiers.ToString(),
                            methodSymbol.ReturnType.GetFullyQualifiedName(),
                            methodSymbol.IsStatic,
                            methodSymbol.Parameters.Select(ParseParameter).ToArray()
                        ));
                });

        var addressHookInfos =
            context.SyntaxProvider.ForAttributeWithMetadataName(
                "ComplexTweaks.Attributes.AddressHookAttribute`1",
                static (node, _) => node is MethodDeclarationSyntax { AttributeLists.Count: > 0 },
                static (context, token) =>
                {
                    var classSyntax = (ClassDeclarationSyntax)context.TargetNode.Parent!;

                    var hierarchy = new List<string>();
                    for (var parent = context.TargetSymbol.ContainingType; parent is not null; parent = parent.ContainingType)
                        hierarchy.Add(parent.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));

                    var methodSyntax = (MethodDeclarationSyntax)context.TargetNode;
                    var methodSymbol = (IMethodSymbol)context.TargetSymbol;
                    var attr = methodSymbol.GetAttributes()[0];

                    var addressName = $"(nint){attr.AttributeClass!.TypeArguments[0].GetFullyQualifiedName()}.MemberFunctionPointers.{(string)attr.ConstructorArguments[0].Value!}";

                    return new HookInfo(
                        new ClassInfo(
                            context.TargetSymbol.ContainingType.ToDisplayString(),
                            methodSymbol.ContainingNamespace.ToDisplayString(
                                new SymbolDisplayFormat(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces)),
                            [.. hierarchy]
                        ),
                        new MethodInfo(
                            methodSymbol.Name,
                            methodSyntax.Modifiers.ToString(),
                            methodSymbol.ReturnType.GetFullyQualifiedName(),
                            methodSymbol.IsStatic,
                            methodSymbol.Parameters.Select(ParseParameter).ToArray()
                        ),
                        addressName);
                });

        var vtblHookInfos =
            context.SyntaxProvider.ForAttributeWithMetadataName(
                "ComplexTweaks.Attributes.VTableHookAttribute`1",
                static (node, _) => node is MethodDeclarationSyntax { AttributeLists.Count: > 0 },
                static (context, token) =>
                {
                    var classSyntax = (ClassDeclarationSyntax)context.TargetNode.Parent!;

                    var hierarchy = new List<string>();
                    for (var parent = context.TargetSymbol.ContainingType; parent is not null; parent = parent.ContainingType)
                        hierarchy.Add(parent.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));

                    var methodSyntax = (MethodDeclarationSyntax)context.TargetNode;
                    var methodSymbol = (IMethodSymbol)context.TargetSymbol;
                    var attr = methodSymbol.GetAttributes()[0];

                    var addressName = $"{attr.AttributeClass!.TypeArguments[0].GetFullyQualifiedName()}.StaticVirtualTablePointer->{methodSymbol.Name}";

                    return new HookInfo(
                        new ClassInfo(
                            context.TargetSymbol.ContainingType.ToDisplayString(),
                            methodSymbol.ContainingNamespace.ToDisplayString(
                                new SymbolDisplayFormat(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces)),
                            [.. hierarchy]
                        ),
                        new MethodInfo(
                            methodSymbol.Name,
                            methodSyntax.Modifiers.ToString(),
                            methodSymbol.ReturnType.GetFullyQualifiedName(),
                            methodSymbol.IsStatic,
                            methodSymbol.Parameters.Select(ParseParameter).ToArray()
                        ),
                        addressName);
                });

        var sigHookInfos =
            context.SyntaxProvider.ForAttributeWithMetadataName(
                "ComplexTweaks.Attributes.SigHookAttribute",
                static (node, _) => node is MethodDeclarationSyntax { AttributeLists.Count: > 0 },
                static (context, token) =>
                {
                    var classSyntax = (ClassDeclarationSyntax)context.TargetNode.Parent!;

                    var hierarchy = new List<string>();
                    for (var parent = context.TargetSymbol.ContainingType; parent is not null; parent = parent.ContainingType)
                        hierarchy.Add(parent.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));

                    var methodSyntax = (MethodDeclarationSyntax)context.TargetNode;
                    var methodSymbol = (IMethodSymbol)context.TargetSymbol;
                    var attr = methodSymbol.GetAttributes()[0];

                    var signature = (string)attr.ConstructorArguments[0].Value!;
                    var addressName = $"Svc.SigScanner.ScanText(\"{signature}\")";

                    return new HookInfo(
                        new ClassInfo(
                            context.TargetSymbol.ContainingType.ToDisplayString(),
                            methodSymbol.ContainingNamespace.ToDisplayString(
                                new SymbolDisplayFormat(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces)),
                            [.. hierarchy]
                        ),
                        new MethodInfo(
                            methodSymbol.Name,
                            methodSyntax.Modifiers.ToString(),
                            methodSymbol.ReturnType.GetFullyQualifiedName(),
                            methodSymbol.IsStatic,
                            methodSymbol.Parameters.Select(ParseParameter).ToArray()
                        ),
                        addressName);
                });

        var hooks = hookInfos.Collect();
        var addressHooks = addressHookInfos.Collect();
        var vtblHooks = vtblHookInfos.Collect();
        var sigHooks = sigHookInfos.Collect();

        var allHooks = hooks
            .Combine(addressHooks)
            .Combine(vtblHooks)
            .Combine(sigHooks)
            .Select((tuple, _) => tuple.Left.Left.Left.Concat(tuple.Left.Left.Right).Concat(tuple.Left.Right).Concat(tuple.Right));

        var addressHookInfoByClass = allHooks
            .SelectMany((items, _) => items.GroupBy(item => item.ClassInfo.Name))
            .Where(items => items.Any());

        context.RegisterSourceOutput(addressHookInfoByClass,
            static (sourceContext, item) => { sourceContext.AddSource($"{item.Key}.AddressHookGenerator.g.cs", RenderHookInfos(item)); });
    }

    private static ParameterInfo ParseParameter(IParameterSymbol parameterSymbol) => new(
        parameterSymbol.Name,
        parameterSymbol.Type.GetFullyQualifiedName(),
        parameterSymbol.GetDefaultValueString(),
        parameterSymbol.RefKind);

    private static string RenderHookInfos(IGrouping<string, HookInfo> items)
    {
        using var baseTextWriter = new StringWriter();
        using var writer = new IndentedTextWriter(baseTextWriter, "    ");

        var classInfo = items.First().ClassInfo;

        // write file header
        writer.WriteLine("// <auto-generated/>");

        // write namespace 
        if (classInfo.Namespace.Length > 0)
        {
            writer.WriteLine($"namespace {classInfo.Namespace};");
            writer.WriteLine();
        }

        // write opening struct hierarchy in reverse order
        // note we do not need to specify the accessibility here since a partial declared with no accessibility uses the other partial
        for (var i = classInfo.Hierarchy.Length - 1; i >= 0; i--)
        {
            writer.WriteLine($"public unsafe partial class {classInfo.Hierarchy[i]}");
            writer.WriteLine("{");
            writer.Indent++;
        }

        // render delegates and hooks
        foreach (var hookInfo in items)
        {
            writer.WriteLine($"private delegate {hookInfo.MethodInfo.ReturnType} {hookInfo.MethodInfo.Name}Delegate({hookInfo.MethodInfo.GetParameterTypesAndNamesString()});");
            writer.WriteLine($"private Dalamud.Hooking.Hook<{hookInfo.MethodInfo.Name}Delegate> {hookInfo.MethodInfo.Name}Hook {{ get; set; }} = null!;");
            writer.WriteLine();
        }

        writer.WriteLine();

        // render SetupHooks
        writer.WriteLine("public override void SetupHooks()");
        writer.WriteLine("{");
        writer.Indent++;
        foreach (var hookInfo in items)
        {
            var addressName = hookInfo.AddressName ?? $"{hookInfo.MethodInfo.Name}Address";
            writer.WriteLine($"{hookInfo.MethodInfo.Name}Hook = Svc.Hook.HookFromAddress<{hookInfo.MethodInfo.Name}Delegate>({addressName}, {hookInfo.MethodInfo.Name});");
        }
        writer.Indent--;
        writer.WriteLine("}");

        // write closing struct hierarchy
        for (var i = 0; i < classInfo.Hierarchy.Length; i++)
        {
            writer.Indent--;
            writer.WriteLine("}");
        }

        writer.Flush();

        return baseTextWriter.ToString();
    }
}
