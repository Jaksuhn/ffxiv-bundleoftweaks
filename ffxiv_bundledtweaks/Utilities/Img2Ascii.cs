using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Text;

namespace ComplexTweaks.Utilities;

public class Img2Ascii {
    // A lot of this is based on https://github.com/TheZoraiz/ascii-image-converter/
    private static readonly char[] AsciiChars = [ '@', '%', '#', '&', '*', '+', '=', '-', ':', '.', ' ',
        '$', '?', '!', '^', '~', '_', ',', ';', '<', '>', '/', '\\', '|', '(', ')', '[', ']', '{', '}' ];
    private static readonly string AsciiTableSimple = " .:-=+*#%@";
    private static readonly string AsciiTableDetailed = " .'`^\",:;Il!i><~+_-?][}{1)(|\\/tfjrxnuvczXYUJCLQ0OZmwqpdbkhao*#MW&8%B@$";

    public static int GetAsciiTableLength() => AsciiChars.Length;
    public static char GetAsciiChar(int index) => AsciiChars[index];

    public static char MapBrightnessToAscii(double brightness01, bool detailed) {
        var table = detailed ? AsciiTableDetailed : AsciiTableSimple;
        if (brightness01 <= 0) return table[0];
        if (brightness01 >= 1) return table[^1];
        var idx = (int)(brightness01 * table.Length);
        if (idx >= table.Length) idx = table.Length - 1;
        return table[idx];
    }

    /// <summary>
    /// Converts an image to ASCII art with color preservation using ANSI escape codes.
    /// </summary>
    /// <param name="imagePath">Path to the image file</param>
    /// <param name="maxWidth">Maximum width in characters (default: 100). Height will be calculated to maintain aspect ratio.</param>
    /// <returns>A string containing the colored ASCII art</returns>
    public static string ConvertToAscii(string imagePath, int maxWidth = 100) {
        if (string.IsNullOrEmpty(imagePath))
            throw new ArgumentException("Image path cannot be null or empty.", nameof(imagePath));

        if (!System.IO.File.Exists(imagePath))
            throw new System.IO.FileNotFoundException($"Image file not found: {imagePath}");

        using var image = Image.Load<Rgba32>(imagePath);
        return ConvertToAscii(image, maxWidth);
    }

    /// <summary>
    /// Converts an image to ASCII art.
    /// If <paramref name="useAnsiColor"/> is true, ANSI 24-bit color codes are embedded.
    /// </summary>
    /// <param name="image">The image to convert</param>
    /// <param name="maxWidth">Maximum width in characters (default: 100)</param>
    /// <param name="useAnsiColor">Whether to output ANSI color codes</param>
    /// <returns>A string containing the ASCII art</returns>
    public static string ConvertToAscii(Image<Rgba32> image, int maxWidth = 100, bool useAnsiColor = true) {
        ArgumentNullException.ThrowIfNull(image);

        var aspectRatio = (double)image.Height / image.Width;
        var width = maxWidth;
        var height = (int)(maxWidth * aspectRatio * 0.5);

        using var resized = image.Clone(ctx => ctx.Resize(new ResizeOptions {
            Size = new Size(width, height),
            Mode = ResizeMode.Max,
            Sampler = KnownResamplers.Lanczos3
        }));

        var result = new StringBuilder();
        var resetCode = "\x1b[0m";

        resized.ProcessPixelRows(accessor => {
            for (var y = 0; y < accessor.Height; y++) {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++) {
                    var pixel = row[x];
                    var r = pixel.R;
                    var g = pixel.G;
                    var b = pixel.B;
                    var a = pixel.A;

                    // Calculate brightness using perceptual luminance (weighted average)
                    var brightness = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
                    var gammaCorrected = Math.Pow(brightness, 0.5);
                    var charIndex = (int)Math.Round(gammaCorrected * (AsciiChars.Length - 1));
                    charIndex = Math.Clamp(charIndex, 0, AsciiChars.Length - 1);
                    var asciiChar = AsciiChars[charIndex];

                    if (useAnsiColor) {
                        var colorCode = $"\x1b[38;2;{r};{g};{b}m";
                        result.Append(colorCode);
                        result.Append(asciiChar);
                    }
                    else
                        result.Append(asciiChar);
                }

                if (useAnsiColor)
                    result.Append(resetCode);
                result.Append('\n');
            }
        });

        return result.ToString();
    }

    /// <summary>
    /// Converts an image to monochrome ASCII art (no ANSI colors).
    /// </summary>
    public static string ConvertToAsciiMonochrome(Image<Rgba32> image, int maxWidth = 100)
        => ConvertToAscii(image, maxWidth, useAnsiColor: false);

    /// <summary>
    /// Converts an image to ASCII art and returns as a list of colored lines.
    /// </summary>
    /// <param name="imagePath">Path to the image file</param>
    /// <param name="maxWidth">Maximum width in characters (default: 100)</param>
    /// <returns>A list of strings, each representing a colored line of ASCII art</returns>
    public static List<string> ConvertToAsciiLines(string imagePath, int maxWidth = 100)
        => [.. ConvertToAscii(imagePath, maxWidth).Split('\n', StringSplitOptions.RemoveEmptyEntries)];
}
