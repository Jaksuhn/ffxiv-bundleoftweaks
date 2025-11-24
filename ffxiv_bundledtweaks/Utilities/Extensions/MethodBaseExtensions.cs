using System.Reflection;

namespace ComplexTweaks.Utilities.Extensions;

public static class MethodBaseExtensions
{
    public static void Log(this MethodBase method)
    {
        var parameters = method.GetParameters();
        if (parameters.Length == 0)
        {
            Svc.Log.Debug($"{method.DeclaringType?.Name}.{method.Name}()");
            return;
        }

        var paramStrings = parameters.Select(p => $"{p.Name}: {p.ParameterType.Name}");
        var joined = string.Join(", ", paramStrings);
        Svc.Log.Debug($"{method.DeclaringType?.Name}.{method.Name}({joined})");
    }

    public static void Log(this MethodBase method, params object?[] parameterValues)
    {
        var parameters = method.GetParameters();
        if (parameters.Length == 0)
        {
            Svc.Log.Debug($"{method.DeclaringType?.Name}.{method.Name}()");
            return;
        }

        var paramStrings = new List<string>();
        for (var i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];
            var value = i < parameterValues.Length ? parameterValues[i] : null;
            var valueString = FormatValue(value);
            paramStrings.Add($"{param.Name}: {valueString}");
        }

        var joined = string.Join(", ", paramStrings);
        Svc.Log.Debug($"{method.DeclaringType?.Name}.{method.Name}({joined})");
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "null",
        nint nintPtr => $"0x{nintPtr:X}",
        string s => $"\"{s}\"",
        char c => $"'{c}'",
        _ => value.ToString() ?? "null"
    };
}

