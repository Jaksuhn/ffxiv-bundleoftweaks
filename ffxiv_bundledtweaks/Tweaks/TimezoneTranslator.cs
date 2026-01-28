using Dalamud.Game;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using System.Globalization;
using System.Text.RegularExpressions;
using TimeZoneNames;

namespace ComplexTweaks.Tweaks;

[Tweak]
public class TimezoneTranslator : Tweak {
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

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled) {
        if (type is not XivChatType.Notice) return;
        if (message.TextValue.IsNullOrEmpty()) return;

        if (_kvp.TryGetValue(Svc.ClientState.ClientLanguage, out var conf)) {
            var regex = conf.Culture.GetFullDateTimeRegexPattern();
            if (!regex.IsMatch(message.TextValue))
                return;

            var serverTz = Svc.ClientState.ClientLanguage == ClientLanguage.French ? conf.LongName : conf.Abbreviation; // french has to be special as always
            var sb = new SeStringBuilder();
            foreach (var item in message.Payloads) {
                if (item is TextPayload tp && !string.IsNullOrEmpty(tp.Text)) {
                    // replace every timestamp in this payload (there can be multiple)
                    var replacedTimes = regex.Replace(tp.Text, m => {
                        if (!DateTime.TryParse(m.Value, conf.Culture, out var serverTime)) {
                            Error($"Failed to parse a {nameof(DateTime)} from [{m.Value}] with culture [{conf.Culture.Name}]");
                            return m.Value;
                        }

                        var localTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(serverTime, conf.ServerTimeZone, TimeZoneInfo.Local.Id)
                            .ToString(conf.Culture.DateTimeFormat.FullDateTimePattern, conf.Culture);

                        Log($"Replaced timestamp [{m.Value} ({serverTz})] with [{localTime} ({LocalTzAbbreviation})]");
                        return localTime;
                    });

                    // replace / append timezone abbreviation.
                    var withLocalTz = Regex.Replace(replacedTimes, $@"\({Regex.Escape(serverTz)}\)", $"({LocalTzAbbreviation})", RegexOptions.IgnoreCase);
                    if (withLocalTz == replacedTimes && !withLocalTz.Contains($"({LocalTzAbbreviation})", StringComparison.OrdinalIgnoreCase))
                        withLocalTz += $" ({LocalTzAbbreviation})"; // if any original string (jp) doesn't have a timezone to replace, append

                    sb.Add(new TextPayload(withLocalTz));
                }
                else {
                    sb.Add(item);
                }
            }

            message = sb.Build();
        }
    }

    private string LocalTzAbbreviation {
        get {
            var local = TimeZoneInfo.Local;
            var now = DateTime.Now;

            try {
                var abbrs = TZNames.GetAbbreviationsForTimeZone(local.Id, CultureInfo.CurrentCulture.Name);
                var candidate = local.IsDaylightSavingTime(now) ? abbrs.Daylight : abbrs.Standard;
                if (!string.IsNullOrWhiteSpace(candidate))
                    return candidate;
            }
            catch {
                // fall through to offset-based abbreviation
            }

            var offset = local.GetUtcOffset(now);
            var sign = offset >= TimeSpan.Zero ? "+" : "-";
            var hours = Math.Abs(offset.Hours).ToString("00", CultureInfo.InvariantCulture);
            var minutes = Math.Abs(offset.Minutes).ToString("00", CultureInfo.InvariantCulture);

            return minutes == "00" ? $"GMT{sign}{hours}" : $"GMT{sign}{hours}:{minutes}";
        }
    }

    private sealed class LanguageConfig(CultureInfo cultureInfo, string serverTimezone) {
        public CultureInfo Culture { get; } = cultureInfo;
        public string ServerTimeZone { get; } = serverTimezone;
        public TimeZoneInfo Id => TimeZoneInfo.FindSystemTimeZoneById(ServerTimeZone);
        public string Abbreviation {
            get {
                var now = DateTime.Now;
                try {
                    var tz = TimeZoneInfo.FindSystemTimeZoneById(ServerTimeZone);
                    var candidate = TimeZoneInfo.Local.IsDaylightSavingTime(now) ? TZNames.GetAbbreviationsForTimeZone(tz.Id, Culture.Name).Daylight : TZNames.GetAbbreviationsForTimeZone(tz.Id, Culture.Name).Standard;
                    if (!string.IsNullOrWhiteSpace(candidate))
                        return candidate;

                    var offset = tz.GetUtcOffset(now);
                    var sign = offset >= TimeSpan.Zero ? "+" : "-";
                    var hours = Math.Abs(offset.Hours).ToString("00", CultureInfo.InvariantCulture);
                    var minutes = Math.Abs(offset.Minutes).ToString("00", CultureInfo.InvariantCulture);

                    return minutes == "00"
                        ? $"GMT{sign}{hours}"
                        : $"GMT{sign}{hours}:{minutes}";
                }
                catch {
                    return "GMT";
                }
            }
        }

        public string LongName // yes this is pointlessly complicated
        {
            get {
                if (TZNames.GetNamesForTimeZone(Id.Id, Culture.Name) is { Generic: var gen } && !string.IsNullOrEmpty(gen)) {
                    if (!gen.StartsWith("heure de ", StringComparison.OrdinalIgnoreCase))
                        return $"heure de {(Id.Id.Contains('/') ? Id.Id.Split('/').Last() : Id.Id)}";
                    return gen;
                }
                return Abbreviation;
            }
        }
    }
}
