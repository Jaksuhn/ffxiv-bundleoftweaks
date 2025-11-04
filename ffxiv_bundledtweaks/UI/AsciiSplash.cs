using Automaton.Utilities;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using ECommons.ImGuiMethods;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ComplexTweaks.UI;

public static class AsciiSplash
{
    public static void Reset()
    {
        lock (_lock)
        {
            _cachedColored = null;
            _cachedLines = null;
            _buildTask = null;
            _iconId = 0;
            _sizedOnce = false;
        }
    }
    private static readonly Lock _lock = new();
    private static Task<string[]?>? _buildTask;
    private static volatile string[]? _cachedLines; // legacy fallback
    private static volatile (char[] chars, byte[] rgb)[]? _cachedColored; // per line
    private static volatile int _cachedWidth = 80;
    private static volatile uint _iconId;
    private static volatile bool _sizedOnce;
    private static volatile float _cellW;
    private static volatile float _cellH;
    private static volatile int _maxRows;
    private static volatile int _cols;
    private static volatile int _rows;
    private static readonly float _fontScale = 0.5f;
    private static ref readonly Guid GUID_ContainerFormatPng // straight from terrafx
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            ReadOnlySpan<byte> data = [
                0xF4, 0xFA, 0x7C, 0x1B,
                0x3F, 0x71,
                0x3C, 0x47,
                0xBB,
                0xCD,
                0x61,
                0x37,
                0x42,
                0x5F,
                0xAE,
                0xAF
            ];

            return ref Unsafe.As<byte, Guid>(ref MemoryMarshal.GetReference(data));
        }
    }

    public static void Draw(int maxWidthChars = 80)
    {
        if (!_sizedOnce)
        {
            ImGui.SetWindowFontScale(_fontScale);

            var cellW = ImGui.CalcTextSize("M").X; // measure after scale
            var cellH = ImGui.GetTextLineHeight();
            var cellSize = Math.Max(cellW, cellH);
            _cellW = cellSize;
            _cellH = cellSize;

            var availY = Math.Max(0f, ImGui.GetContentRegionAvail().Y - _cellH * 2);
            var availX = ImGui.GetContentRegionAvail().X;
            _maxRows = Math.Max(1, (int)Math.Floor(availY / _cellH));
            var maxColsByWidth = Math.Max(1, (int)Math.Floor(availX / _cellW));
            var side = Math.Min(maxColsByWidth, _maxRows);
            var cap = Math.Clamp(maxWidthChars, 32, 400); // higher cap for smaller font
            _cols = Math.Min(side, cap);
            _rows = _cols; // ensure square since all in game icons are squares
            _cachedWidth = _cols;

            ImGui.SetWindowFontScale(ImGuiHelpers.GlobalScaleSafe);
            _sizedOnce = true;
        }

        var colored = _cachedColored;
        if (colored is null)
        {
            EnsureStarted();
            ImGuiEx.PushCursorY(10);
            if (_buildTask is { IsCompleted: true } && _cachedLines is { Length: > 0 })
                foreach (var line in _cachedLines)
                    ImGui.TextUnformatted(line);
            else
                ImGui.TextUnformatted("Loading icon…");
            return;
        }

        ImGui.SetWindowFontScale(_fontScale);
        var drawCellSize = _cellW > 0 && _cellH > 0 ? Math.Max(_cellW, _cellH) : ImGui.CalcTextSize("M").X;
        var cellWidth = drawCellSize;
        var lineHeight = drawCellSize;
        var dl = ImGui.GetWindowDrawList();
        var font = ImGui.GetFont();

        var baseCursorPos = ImGui.GetCursorPos();
        var baseScreenPos = ImGui.GetCursorScreenPos();
        var regionWidth = ImGui.GetContentRegionAvail().X;

        var maxRowsToDraw = Math.Min(colored.Length, _rows > 0 ? _rows : colored.Length);

        for (var rowIndex = 0; rowIndex < maxRowsToDraw; rowIndex++)
        {
            var (chars, rgb) = colored[rowIndex];

            // fixed-width cells to avoid variable char widths
            var totalWidth = (_cols > 0 ? _cols : chars.Length) * cellWidth;
            var x = baseScreenPos.X + Math.Max(0, (regionWidth - totalWidth) * 0.5f);
            var y = baseScreenPos.Y + rowIndex * lineHeight;

            var limit = _cols > 0 ? Math.Min(_cols, chars.Length) : chars.Length;
            for (var i = 0; i < limit; i++)
            {
                var r = rgb[i * 3 + 0] / 255f;
                var g = rgb[i * 3 + 1] / 255f;
                var b = rgb[i * 3 + 2] / 255f;
                var col = ImGui.ColorConvertFloat4ToU32(new Vector4(r, g, b, 1f));
                var ch = chars[i].ToString();
                dl.AddText(font, ImGui.GetFontSize(), new Vector2(x, y), col, ch);
                x += cellWidth;
            }
        }

        ImGui.SetWindowFontScale(ImGuiHelpers.GlobalScaleSafe);
        ImGui.SetCursorPos(new Vector2(baseCursorPos.X, baseCursorPos.Y + maxRowsToDraw * lineHeight));
    }

    private static void EnsureStarted()
    {
        lock (_lock)
        {
            if (_buildTask is { IsCompleted: false })
                return;

            _buildTask = Task.Run(BuildAsync);
        }
    }

    private static async Task<string[]?> BuildAsync()
    {
        try
        {
            var rnd = Random.Shared;
            for (var attempt = 0; attempt < 30; attempt++)
            {
                if (_iconId == 0)
                {
                    var candidate = rnd.Next(0, 100_000); // everything above 100k is way too complex
                    if (!Svc.Texture.TryGetIconPath(new GameIconLookup((uint)candidate), out _))
                        continue;
                    _iconId = (uint)candidate;
                }

                var shared = Svc.Texture.GetFromGameIcon(new GameIconLookup(_iconId));

                try
                {
                    using var rented = await shared.RentAsync().ConfigureAwait(false);
                    using var image = await WrapToImageAsync(rented).ConfigureAwait(false);
                    BuildColoredAscii(image, Math.Clamp(_cachedWidth, 32, 200), out var colored, out var lines);
                    _cachedColored = colored;
                    _cachedLines = lines; // fallback plain
                    Svc.Log.Debug($"[{nameof(AsciiSplash)}] Chosen icon: #{_iconId}");
                    return lines;
                }
                catch
                {
                    // every few attempts, pick a new candidate
                    if (attempt % 5 == 4)
                        _iconId = 0;
                }
            }
        }
        catch { }

        _cachedLines = ["Failed to build ASCII splash."];
        _cachedColored = null;
        return _cachedLines;
    }

    private static async Task<Image<Rgba32>> WrapToImageAsync(IDalamudTextureWrap wrap)
    {
        try
        {
            using var ms = new MemoryStream();
            await TextureReadbackProvider.SaveToStreamAsync(wrap, GUID_ContainerFormatPng, ms, props: null, leaveWrapOpen: true, leaveStreamOpen: true).ConfigureAwait(false);
            ms.Position = 0;
            return Image.Load<Rgba32>(ms);
        }
        catch (Exception ex)
        {
            Svc.Log.Warning($"AsciiSplash PNG save failed; falling back to raw. {ex.Message}");
        }

        var tuple = await TextureReadbackProvider.GetRawImageAsync(wrap, default, leaveWrapOpen: true).ConfigureAwait(false);
        var specs = tuple.Specification;
        var bytes = tuple.RawData;

        var width = specs.Width;
        var height = specs.Height;
        var pitch = specs.Pitch;
        var dxgi = specs.DxgiFormat;

        var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < height; y++)
            {
                var dst = accessor.GetRowSpan(y);
                var srcOffset = y * pitch;
                for (var x = 0; x < width; x++)
                {
                    var i = srcOffset + (x * 4);
                    if (i + 3 >= bytes.Length)
                        break;

                    // 28/87 are common; treat SRGB variants same as UNORM for byte order
                    if (dxgi == 87) // B8G8R8A8
                        dst[x] = new Rgba32(bytes[i + 2], bytes[i + 1], bytes[i + 0], bytes[i + 3]);
                    else // assume R8G8B8A8 or similar
                        dst[x] = new Rgba32(bytes[i + 0], bytes[i + 1], bytes[i + 2], bytes[i + 3]);
                }
            }
        });

        return image;
    }

    private static void BuildColoredAscii(Image<Rgba32> image, int maxWidth, out (char[] chars, byte[] rgb)[] colored, out string[] plain)
    {
        var cols = _cols > 0 ? _cols : Math.Max(1, maxWidth);
        var rows = _rows > 0 ? _rows : cols;

        using var resized = image.Clone(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new Size(cols, rows),
            Mode = ResizeMode.Stretch,
            Sampler = KnownResamplers.NearestNeighbor
        }));

        var lines = new List<string>(rows);
        var outColored = new List<(char[] chars, byte[] rgb)>(rows);

        resized.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var widthCount = _cols > 0 ? _cols : row.Length;
                var lineChars = new char[widthCount];
                var colors = new byte[widthCount * 3];
                for (var x = 0; x < widthCount; x++)
                {
                    var p = row[x];
                    var r = p.R; var g = p.G; var b = p.B;
                    var brightness = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
                    var gamma = Math.Pow(brightness, 0.5);
                    var ch = Img2Ascii.MapBrightnessToAscii(gamma, detailed: true);
                    lineChars[x] = ch;
                    const float brightnessBoost = 1.4f;
                    colors[x * 3 + 0] = (byte)Math.Min(255, (int)(r * brightnessBoost));
                    colors[x * 3 + 1] = (byte)Math.Min(255, (int)(g * brightnessBoost));
                    colors[x * 3 + 2] = (byte)Math.Min(255, (int)(b * brightnessBoost));
                }
                outColored.Add((lineChars, colors));
                lines.Add(new string(lineChars));
            }
        });

        colored = [.. outColored];
        plain = [.. lines];
    }
}
