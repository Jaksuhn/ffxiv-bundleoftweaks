using System.CodeDom.Compiler;

namespace InteropSourceGenerators.Extensions;

/// <summary>
/// Extension methods for <see cref="IndentedTextWriter"/> to support block scoping.
/// </summary>
internal static class IndentedTextWriterExtensions {
    /// <summary>
    /// Writes an opening brace and increases indentation.
    /// Returns a disposable that will write the closing brace and decrease indentation.
    /// </summary>
    /// <param name="writer">The writer to use.</param>
    /// <param name="openingBrace">Optional opening text before the brace (e.g., "if (condition)").</param>
    /// <returns>A disposable that will close the block.</returns>
    public static IDisposable Block(this IndentedTextWriter writer, string? openingBrace = null) {
        if (!string.IsNullOrEmpty(openingBrace)) {
            writer.Write(openingBrace);
            writer.WriteLine();
        }
        writer.WriteLine("{");
        writer.Indent++;
        return new BlockScope(writer);
    }

    /// <summary>
    /// Writes an opening brace on the same line and increases indentation.
    /// Returns a disposable that will write the closing brace and decrease indentation.
    /// </summary>
    /// <param name="writer">The writer to use.</param>
    /// <param name="openingBrace">The text to write before the opening brace (e.g., "if (condition)").</param>
    /// <returns>A disposable that will close the block.</returns>
    public static IDisposable InlineBlock(this IndentedTextWriter writer, string openingBrace) {
        writer.Write(openingBrace);
        writer.WriteLine(" {");
        writer.Indent++;
        return new BlockScope(writer);
    }

    private sealed class BlockScope(IndentedTextWriter writer) : IDisposable {
        private bool _disposed;

        public void Dispose() {
            if (_disposed)
                return;

            writer.Indent--;
            writer.WriteLine("}");
            _disposed = true;
        }
    }
}

