using Microsoft.CodeAnalysis;

namespace InteropSourceGenerators.Extensions;

/// <summary>
///     Extension methods for <see cref="IParameterSymbol" /> types.
/// </summary>
// ReSharper disable once InconsistentNaming
internal static class IParameterSymbolExtensions {
    public static string? GetDefaultValueString(this IParameterSymbol symbol) {
        if (!symbol.HasExplicitDefaultValue)
            return null;

        var defaultValue = symbol.ExplicitDefaultValue;

        if (defaultValue is null)
            return null;

        if (defaultValue is bool boolValue)
            return boolValue ? "true" : "false";

        return defaultValue.ToString();
    }
}
