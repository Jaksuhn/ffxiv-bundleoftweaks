using Dalamud.Game;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using System.Globalization;
using System.Text.RegularExpressions;
using TimeZoneNames;

namespace ComplexTweaks.Tweaks;

[Tweak]
public class TimezoneTranslator : Tweak
{
    public override string Name => "Timezone Translator";
    public override string Description => "Translates system message timestamps in chat to your time zone";

    // the server times are relative to the server associated with a given language, not whatever you log in to. fun.
    private readonly Dictionary<ClientLanguage, LanguageConfig> _kvp = new()
    {
        { ClientLanguage.Japanese, new LanguageConfig(
            new Func<CultureInfo>(() => {
                var c = (CultureInfo)new CultureInfo("ja-JP").Clone();
                c.DateTimeFormat.FullDateTimePattern = "MMMdd'日''（'ddd'）'HH:mm"; // 11月27日（木）23:59まで
                return c;
            })(),
            "Asia/Tokyo") },
        { ClientLanguage.English, new LanguageConfig(
            new Func<CultureInfo>(() => {
                var c = (CultureInfo)new CultureInfo("en-US").Clone();
                c.DateTimeFormat.FullDateTimePattern = "MMM. dd, yyyy %h:mm tt"; // Nov. 27, 2025 6:59 a.m. (PST)
                c.DateTimeFormat.AMDesignator = "a.m.";
                c.DateTimeFormat.PMDesignator = "p.m.";
                return c;
            })(),
            "America/Los_Angeles") },
        { ClientLanguage.German, new LanguageConfig(
            new Func<CultureInfo>(() => {
                var c = (CultureInfo)new CultureInfo("de-DE").Clone();
                c.DateTimeFormat.FullDateTimePattern = "dd. MMM yyyy 'um' HH:mm 'Uhr'"; // 27. Nov. 2025 um 15:59 Uhr (MEZ)
                return c;
            })(),
            "Europe/Berlin") },
        { ClientLanguage.French, new LanguageConfig(
            new Func<CultureInfo>(() => {
                var c = (CultureInfo)new CultureInfo("en-US").Clone();
                c.DateTimeFormat.FullDateTimePattern = "dd MMMM yyyy 'à' HH'h'mm"; // 27 novembre 2025 à 15h59 (heure de Paris)
                return c;
            })(),
            "Europe/Paris") },
    };

    public override void Enable() => Svc.Chat.ChatMessage += OnChatMessage;
    public override void Disable() => Svc.Chat.ChatMessage -= OnChatMessage;

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        if (type is not XivChatType.Notice) return;
        if (message.TextValue.IsNullOrEmpty()) return;

        if (_kvp.TryGetValue(Svc.ClientState.ClientLanguage, out var conf))
        {
            if (conf.Culture.GetFullDateTimeRegexPattern().Match(message.TextValue) is not { Success: true } match) return;

            Log($"Detected timestamp [{match.Value}] in message {message.TextValue}");
            if (DateTime.TryParse(match.Value, conf.Culture, out var serverTime))
            {
                var localTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(serverTime, conf.ServerTimeZone, TimeZoneInfo.Local.Id).ToString(conf.Culture.DateTimeFormat.FullDateTimePattern, conf.Culture);
                var sb = new SeStringBuilder();
                foreach (var item in message.Payloads)
                {
                    if (item is TextPayload tp && (tp.Text?.Contains(match.Value) ?? false)) // there might be multiple text payloads (like if ST clickable chat links is enabled)
                    {
                        string text, original = text = string.Concat(tp.Text.AsSpan(0, match.Index), localTime, tp.Text.AsSpan(match.Index + match.Length));
                        var serverTz = Svc.ClientState.ClientLanguage == ClientLanguage.French ? conf.LongName : conf.Abbreviation; // french has to be special as always
                        text = Regex.Replace(text, $@"\({Regex.Escape(serverTz)}\)", $"({LocalTzAbbreviation})", RegexOptions.IgnoreCase);
                        if (text == original)
                            text += $" ({LocalTzAbbreviation})"; // if any original string (jp) doesn't have a timezone to replace, append

                        Log($"Replaced [{match.Value} ({serverTz})] with [{localTime} ({LocalTzAbbreviation})]");
                        sb.Add(new TextPayload(text));
                    }
                    else
                        sb.Add(item);
                }
                message = sb.Build();
            }
            else
                Error($"Failed to parse a {nameof(DateTime)} from [{match.Value}] with culture [{conf.Culture.Name}]");
        }
    }

    private string LocalTzAbbreviation
        => TimeZoneInfo.Local.IsDaylightSavingTime(DateTime.Now)
        ? TZNames.GetAbbreviationsForTimeZone(TimeZoneInfo.Local.Id, CultureInfo.CurrentCulture.Name).Daylight ?? "null"
        : TZNames.GetAbbreviationsForTimeZone(TimeZoneInfo.Local.Id, CultureInfo.CurrentCulture.Name).Standard ?? "null";

    private sealed class LanguageConfig(CultureInfo cultureInfo, string serverTimezone)
    {
        public CultureInfo Culture { get; } = cultureInfo;
        public string ServerTimeZone { get; } = serverTimezone;
        public TimeZoneInfo Id => TimeZoneInfo.FindSystemTimeZoneById(ServerTimeZone);
        public string Abbreviation => TimeZoneInfo.FindSystemTimeZoneById(ServerTimeZone) is var tz
            ? TimeZoneInfo.Local.IsDaylightSavingTime(DateTime.Now)
                ? TZNames.GetAbbreviationsForTimeZone(tz.Id, Culture.Name).Daylight ?? "null"
                : TZNames.GetAbbreviationsForTimeZone(tz.Id, Culture.Name).Standard ?? "null"
            : "null";

        public string LongName // yes this is pointlessly complicated
        {
            get
            {
                if (TZNames.GetNamesForTimeZone(Id.Id, Culture.Name) is { Generic: var gen } && !string.IsNullOrEmpty(gen))
                {
                    if (!gen.StartsWith("heure de ", StringComparison.OrdinalIgnoreCase))
                        return $"heure de {(Id.Id.Contains('/') ? Id.Id.Split('/').Last() : Id.Id)}";
                    return gen;
                }
                return Abbreviation;
            }
        }
    }
}
