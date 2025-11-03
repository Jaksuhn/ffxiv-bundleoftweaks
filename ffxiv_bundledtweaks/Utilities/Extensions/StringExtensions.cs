using System.Globalization;
using System.Text.RegularExpressions;

namespace ComplexTweaks.Utilities.Extensions;
public static partial class StringExtensions
{
    public static bool TryParseVector3(this string input, out Vector3 output)
    {
        output = Vector3.Zero;
        var pattern = @"(-?\d+(\.\d+)?),(-?\d+(\.\d+)?),(-?\d+(\.\d+)?)";
        var match = Regex.Match(input, pattern);
        if (match.Success)
        {
            var x = float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var y = float.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            var z = float.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture);
            output = new Vector3(x, y, z);
            return true;
        }
        return false;
    }

    public static string ToTitleCase(this string s) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLower());
    public static string GetLast(this string source, int tail_length) => tail_length >= source.Length ? source : source[^tail_length..];
    public static string SplitWords(this string source) => SplitWords().Replace(source, " ").Trim();
    public static string FilterNonAlphanumeric(this string input) => FilterNonAlphanumeric().Replace(input, string.Empty);

    [GeneratedRegex("(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")]
    // smart word split for things in pascal case while also handling acronyms/initialisms
    private static partial Regex SplitWords();

    [GeneratedRegex("[^\\p{L}\\p{N}]")]
    private static partial Regex FilterNonAlphanumeric();
}
