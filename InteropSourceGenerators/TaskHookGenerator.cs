using InteropSourceGenerators.Extensions;
using InteropSourceGenerators.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.CodeDom.Compiler;

namespace InteropSourceGenerators;

[Generator]
internal sealed class TaskHookGenerator : IIncrementalGenerator {
    private static readonly string AutoTaskBaseType = "ComplexTweaks.Services.AutoTask";
    private static readonly string CommonTasksBaseType = "clib.TaskSystem.TaskBase";

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var addressHookInfos = context.SyntaxProvider.ForAttributeWithMetadataName(
            "ComplexTweaks.Attributes.AddressHookAttribute`1",
            static (node, _) => node is MethodDeclarationSyntax { AttributeLists.Count: > 0 },
            ParseHookInfo)
            .Collect();

        var vtblHookInfos = context.SyntaxProvider.ForAttributeWithMetadataName(
            "ComplexTweaks.Attributes.VTableHookAttribute`1",
            static (node, _) => node is MethodDeclarationSyntax { AttributeLists.Count: > 0 },
            ParseHookInfo)
            .Collect();

        var sigHookInfos = context.SyntaxProvider.ForAttributeWithMetadataName(
            "ComplexTweaks.Attributes.SigHookAttribute",
            static (node, _) => node is MethodDeclarationSyntax { AttributeLists.Count: > 0 },
            ParseHookInfo)
            .Collect();

        var allHooks = addressHookInfos
            .Combine(vtblHookInfos)
            .Combine(sigHookInfos)
            .Select(static (tuple, _) => {
                var result = new List<HookInfo>();
                foreach (var h in tuple.Left.Left)
                    if (h != null) result.Add(h);
                foreach (var h in tuple.Left.Right)
                    if (h != null) result.Add(h);
                foreach (var h in tuple.Right)
                    if (h != null) result.Add(h);
                return result;
            });

        var hooksByClass = allHooks
            .SelectMany(static (items, _) => items.GroupBy(static item => item.ClassInfo.Name))
            .Collect()
            .SelectMany(static (items, _) => items);

        context.RegisterSourceOutput(hooksByClass,
            static (sourceContext, item) => {
                sourceContext.AddSource($"{item.Key}.TaskHookGenerator.g.cs", RenderTaskHookInfos(item));
            });
    }

    private static HookInfo? ParseHookInfo(GeneratorAttributeSyntaxContext context, CancellationToken _) {
        var containingType = context.TargetSymbol.ContainingType;
        if (containingType == null)
            return null;

        if (!InheritsFromTaskBase(containingType))
            return null;

        var hierarchy = new List<string>();
        for (var parent = containingType; parent is not null; parent = parent.ContainingType)
            hierarchy.Add(parent.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));

        var methodSyntax = (MethodDeclarationSyntax)context.TargetNode;
        var methodSymbol = (IMethodSymbol)context.TargetSymbol;

        if (context.Attributes.Length == 0)
            return null;

        var attr = context.Attributes[0];
        if (attr.AttributeClass == null)
            return null;

        string? addressName = null;
        var attrClass = attr.AttributeClass;
        var attrName = attrClass.Name;
        var attrMetadataName = attrClass.GetFullyQualifiedMetadataName();

        if (attrName.Contains("AddressHookAttribute") || attrMetadataName.Contains("AddressHookAttribute")) {
            if (attrClass.TypeArguments.Length > 0 && attr.ConstructorArguments.Length > 0)
                addressName = $"(nint){attrClass.TypeArguments[0].GetFullyQualifiedName()}.MemberFunctionPointers.{(string)attr.ConstructorArguments[0].Value!}";
        }
        else if (attrName.Contains("VTableHookAttribute") || attrMetadataName.Contains("VTableHookAttribute")) {
            if (attrClass.TypeArguments.Length > 0)
                addressName = $"{attrClass.TypeArguments[0].GetFullyQualifiedName()}.StaticVirtualTablePointer->{methodSymbol.Name}";
        }
        else if (attrName.Contains("SigHookAttribute") || attrMetadataName.Contains("SigHookAttribute")) {
            if (attr.ConstructorArguments.Length > 0) {
                var signature = (string)attr.ConstructorArguments[0].Value!;
                addressName = $"Svc.SigScanner.ScanText(\"{signature}\")";
            }
        }

        return new HookInfo(
            new ClassInfo(
                containingType.ToDisplayString(),
                methodSymbol.ContainingNamespace.ToDisplayString(
                    new SymbolDisplayFormat(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces)),
                [.. hierarchy]
            ),
            new MethodInfo(
                methodSymbol.Name,
                methodSyntax.Modifiers.ToString(),
                methodSymbol.ReturnType.GetFullyQualifiedName(),
                methodSymbol.IsStatic,
                [.. methodSymbol.Parameters.Select(ParseParameter)]
            ),
            addressName);
    }

    private static bool InheritsFromTaskBase(INamedTypeSymbol? typeSymbol) {
        if (typeSymbol == null)
            return false;

        var current = typeSymbol;
        while (current != null) {
            if (current.HasFullyQualifiedMetadataName(AutoTaskBaseType) || current.HasFullyQualifiedMetadataName(CommonTasksBaseType))
                return true;
            current = current.BaseType;
        }
        return false;
    }

    private static ParameterInfo ParseParameter(IParameterSymbol parameterSymbol) => new(
        parameterSymbol.Name,
        parameterSymbol.Type.GetFullyQualifiedName(),
        parameterSymbol.GetDefaultValueString(),
        parameterSymbol.RefKind);

    private static string RenderTaskHookInfos(IGrouping<string, HookInfo> items) {
        using var baseTextWriter = new StringWriter();
        using var writer = new IndentedTextWriter(baseTextWriter, "    ");

        var classInfo = items.First().ClassInfo;

        writer.WriteLine("// <auto-generated/>");

        if (classInfo.Namespace.Length > 0) {
            writer.WriteLine($"namespace {classInfo.Namespace};");
            writer.WriteLine();
        }

        // must be marked as unsafe if it has a pointer
        var hasPointerTypes = items.Any(hook =>
            hook.MethodInfo.Parameters.Any(p => p.Type.Contains("*")) ||
            hook.MethodInfo.ReturnType.Contains("*"));

        // write opening class hierarchy in reverse order
        var classBlocks = new List<IDisposable>();
        for (var i = classInfo.Hierarchy.Length - 1; i >= 0; i--) {
            if (i == 0) {
                var unsafeKeyword = hasPointerTypes ? "unsafe " : "";
                classBlocks.Add(writer.Block($"public {unsafeKeyword}partial class {classInfo.Hierarchy[i]} : clib.Services.IAutoTaskHooks"));
            }
            else {
                var unsafeKeyword = hasPointerTypes ? "unsafe " : "";
                classBlocks.Add(writer.Block($"public {unsafeKeyword}partial class {classInfo.Hierarchy[i]}"));
            }
        }

        foreach (var hookInfo in items) {
            writer.WriteLine($"private delegate {hookInfo.MethodInfo.ReturnType} {hookInfo.MethodInfo.Name}Delegate({hookInfo.MethodInfo.GetParameterTypesAndNamesString()});");
            writer.WriteLine($"private Dalamud.Hooking.Hook<{hookInfo.MethodInfo.Name}Delegate> {hookInfo.MethodInfo.Name}Hook {{ get; set; }} = null!;");
            writer.WriteLine();
        }

        foreach (var hookInfo in items)
            RenderTaskCompletionSourceSupport(writer, hookInfo);

        writer.WriteLine();

        using (writer.Block("void IAutoTaskHooks.SetupHooks()")) {
            foreach (var hookInfo in items) {
                if (string.IsNullOrEmpty(hookInfo.AddressName)) {
                    writer.WriteLine($"PluginLog.Error($\"[{nameof(TaskHookGenerator)}] No address found for {hookInfo.MethodInfo.Name} - hook will not be set up\");");
                    continue;
                }
                var wrapperName = $"{hookInfo.MethodInfo.Name}_Wrapper";
                writer.WriteLine($"{hookInfo.MethodInfo.Name}Hook = Svc.Hook.HookFromAddress<{hookInfo.MethodInfo.Name}Delegate>({hookInfo.AddressName}, {wrapperName});");
            }
        }

        writer.WriteLine();

        using (writer.Block("void IAutoTaskHooks.EnableHooks()"))
            foreach (var hookInfo in items)
                writer.WriteLine($"{hookInfo.MethodInfo.Name}Hook?.Enable();");

        writer.WriteLine();

        using (writer.Block("void IAutoTaskHooks.DisableHooks()"))
            foreach (var hookInfo in items)
                writer.WriteLine($"{hookInfo.MethodInfo.Name}Hook?.Disable();");

        writer.WriteLine();

        using (writer.Block("void IAutoTaskHooks.DisposeHooks()")) {
            foreach (var hookInfo in items) {
                writer.WriteLine($"{hookInfo.MethodInfo.Name}Hook?.Dispose();");
                var waitListName = $"_{hookInfo.MethodInfo.Name.ToLowerInvariant()}Waits";
                // avoid modification during enumeration
                writer.WriteLine($"var waitsToCancel = System.Linq.Enumerable.ToArray({waitListName});");
                using (writer.Block($"foreach (var wait in waitsToCancel)"))
                    writer.WriteLine("wait.Tcs.TrySetCanceled();");
                writer.WriteLine($"{waitListName}.Clear();");
            }
        }

        foreach (var hookInfo in items)
            RenderWrapperMethod(writer, hookInfo);

        foreach (var hookInfo in items)
            RenderWaitForMethod(writer, hookInfo);

        // Dispose class blocks in reverse order to close them
        for (var i = classBlocks.Count - 1; i >= 0; i--)
            classBlocks[i].Dispose();

        writer.Flush();

        return baseTextWriter.ToString();
    }

    private static void RenderTaskCompletionSourceSupport(IndentedTextWriter writer, HookInfo hookInfo) {
        var methodName = hookInfo.MethodInfo.Name;
        var waitListName = $"_{methodName.ToLowerInvariant()}Waits";
        var registrationClassName = $"{methodName}WaitRegistration";

        // for filters and tcs
        using (writer.Block($"private sealed class {registrationClassName}")) {
            // generate filters from hook params, minus pointers
            foreach (var param in hookInfo.MethodInfo.Parameters.Where(p => !p.Type.Contains("*"))) {
                var nullableType = GetNullableType(param.Type);
                writer.WriteLine($"public {nullableType} {Capitalize(param.Name)} {{ get; }}");
            }

            writer.WriteLine($"public System.Threading.Tasks.TaskCompletionSource<{GetReturnTupleType(hookInfo)}> Tcs {{ get; }}");
            writer.WriteLine();
            using (writer.Block($"public {registrationClassName}({GetFilterParameters(hookInfo)})")) {
                foreach (var param in hookInfo.MethodInfo.Parameters.Where(p => !p.Type.Contains("*")))
                    writer.WriteLine($"{Capitalize(param.Name)} = {param.Name};");
                writer.WriteLine($"Tcs = new System.Threading.Tasks.TaskCompletionSource<{GetReturnTupleType(hookInfo)}>();");
            }
        }
        writer.WriteLine();

        writer.WriteLine($"private readonly System.Collections.Generic.List<{registrationClassName}> {waitListName} = new System.Collections.Generic.List<{registrationClassName}>();");
        writer.WriteLine();
    }

    private static void RenderWrapperMethod(IndentedTextWriter writer, HookInfo hookInfo) {
        var methodName = hookInfo.MethodInfo.Name;
        var wrapperName = $"{methodName}_Wrapper";
        var waitListName = $"_{methodName.ToLowerInvariant()}Waits";
        var registrationClassName = $"{methodName}WaitRegistration";

        using (writer.Block($"private unsafe {hookInfo.MethodInfo.ReturnType} {wrapperName}({hookInfo.MethodInfo.GetParameterTypesAndNamesString()})")) {
            using (writer.Block($"foreach (var wait in System.Linq.Enumerable.ToArray({waitListName}))")) {
                writer.WriteLine("bool matches = true;");
                writer.WriteLine();

                foreach (var param in hookInfo.MethodInfo.Parameters.Where(p => !p.Type.Contains("*"))) {
                    var paramName = param.Name;
                    var waitFieldName = Capitalize(paramName);
                    var nullableType = GetNullableType(param.Type);
                    var isNullable = nullableType.EndsWith("?") || nullableType.StartsWith("System.Nullable<");

                    writer.WriteLine($"// filter for {paramName} ({param.Type})");
                    if (isNullable)
                        using (writer.Block($"if (wait.{waitFieldName}.HasValue)"))
                            writer.WriteLine($"matches = matches && object.Equals(wait.{waitFieldName}.Value, {paramName});");
                    else
                        using (writer.Block($"if (wait.{waitFieldName} != null)"))
                            writer.WriteLine($"matches = matches && object.Equals(wait.{waitFieldName}, {paramName});");
                }
                writer.WriteLine();

                using (writer.Block("if (matches)"))
                    writer.WriteLine($"wait.Tcs.TrySetResult(({GetReturnTupleValues(hookInfo)}));");
            }
            writer.WriteLine();

            writer.WriteLine($"// Call original");
            if (hookInfo.MethodInfo.ReturnType == "void")
                writer.WriteLine($"{methodName}({hookInfo.MethodInfo.GetParameterNamesString()});");
            else {
                writer.WriteLine($"var result = {methodName}({hookInfo.MethodInfo.GetParameterNamesString()});");
                writer.WriteLine("return result;");
            }
        }
        writer.WriteLine();
    }

    private static void RenderWaitForMethod(IndentedTextWriter writer, HookInfo hookInfo) {
        var methodName = hookInfo.MethodInfo.Name;
        var waitMethodName = $"WaitFor{methodName}";
        var waitListName = $"_{methodName.ToLowerInvariant()}Waits";
        var registrationClassName = $"{methodName}WaitRegistration";

        // Use ContinueWith to avoid awaiting in unsafe contexts
        writer.Write($"public System.Threading.Tasks.Task<{GetReturnTupleType(hookInfo)}> {waitMethodName}({GetFilterParameters(hookInfo)})");
        writer.WriteLine();
        using (writer.Block()) {
            var nonPointerParamNames = string.Join(", ", hookInfo.MethodInfo.Parameters
                .Where(p => !p.Type.Contains("*"))
                .Select(p => p.Name));
            writer.WriteLine($"var registration = new {registrationClassName}({nonPointerParamNames});");
            writer.WriteLine($"{waitListName}.Add(registration);");
            writer.WriteLine();
            using (writer.Block("return registration.Tcs.Task.ContinueWith(task =>")) {
                writer.WriteLine($"{waitListName}.Remove(registration);");
                writer.WriteLine("return task.Result;");
            }
            writer.WriteLine(", System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously);");
        }
        writer.WriteLine();
    }

    private static string GetFilterParameters(HookInfo hookInfo)
        // use all hook params as filters, nullable for being optional
        => string.Join(", ", hookInfo.MethodInfo.Parameters
            .Where(p => !p.Type.Contains("*")) // Exclude pointer types (allow in async when)
            .Select(p => {
                var nullableType = GetNullableType(p.Type);
                return $"{p.RefKind.GetParameterPrefix()}{nullableType} {p.Name} = null";
            }));

    private static string GetNullableType(string type) {
        if (type.Contains("*") || type == "void" || type.StartsWith("System.Nullable<") || type.EndsWith("?"))
            return type;

        var valueTypes = new[] { "int", "uint", "long", "ulong", "short", "ushort", "byte", "sbyte", "bool", "float", "double", "decimal", "char" };
        if (valueTypes.Any(vt => type == vt || type.StartsWith(vt + " ")))
            return type + "?";

        return type;
    }

    private static string GetReturnTupleType(HookInfo hookInfo) {
        // Exclude pointer types from return tuple (can't be used as generic type arguments)
        var nonPointerParams = hookInfo.MethodInfo.Parameters.Where(p => !p.Type.Contains("*")).Select(p => p.Type).ToArray();
        if (hookInfo.MethodInfo.ReturnType == "void") {
            // For void methods, return tuple of all non-pointer parameters
            if (nonPointerParams.Length == 0)
                return "System.Threading.Tasks.Task";
            var paramTypes = string.Join(", ", nonPointerParams);
            return $"({paramTypes})";
        }
        else {
            // For non-void methods, include return type in tuple (if not a pointer)
            var returnType = hookInfo.MethodInfo.ReturnType.Contains("*") ? null : hookInfo.MethodInfo.ReturnType;
            if (nonPointerParams.Length == 0 && returnType == null)
                return "System.Threading.Tasks.Task";

            var parts = new List<string>(nonPointerParams);
            if (returnType != null)
                parts.Add(returnType);

            if (parts.Count == 0)
                return "System.Threading.Tasks.Task";
            if (parts.Count == 1)
                return parts[0];

            return $"({string.Join(", ", parts)})";
        }
    }

    private static string GetReturnTupleValues(HookInfo hookInfo) {
        var nonPointerParams = hookInfo.MethodInfo.Parameters.Where(p => !p.Type.Contains("*")).ToArray(); // have to exclude pointers
        var paramNames = string.Join(", ", nonPointerParams.Select(p => p.Name));

        if (hookInfo.MethodInfo.ReturnType == "void")
            return paramNames;
        else {
            // For non-void, include return value if not a pointer
            if (hookInfo.MethodInfo.ReturnType.Contains("*"))
                return paramNames;
            return string.IsNullOrEmpty(paramNames) ? "result" : $"{paramNames}, result";
        }
    }

    private static string Capitalize(string str) => string.IsNullOrEmpty(str) ? str : char.ToUpperInvariant(str[0]) + str[1..];
}

