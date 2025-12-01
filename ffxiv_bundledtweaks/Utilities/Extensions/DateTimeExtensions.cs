using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ComplexTweaks.Utilities.Extensions;

public static class DateTimeExtensions {
    // there was probably a better way to do this
    public static Regex GetFullDateTimeRegexPattern(this CultureInfo info) {
        var form = info.DateTimeFormat.FullDateTimePattern;

        var regex = new StringBuilder();
        var inQuotes = false;
        var dtfi = info.DateTimeFormat;

        for (var i = 0; i < form.Length; i++) {
            var c = form[i];

            if (c == '\'') {
                if (i + 1 < form.Length && form[i + 1] == '\'') {
                    regex.Append(Regex.Escape("'"));
                    i++;
                }
                else
                    inQuotes = !inQuotes;
                continue;
            }

            if (inQuotes) {
                var escaped = Regex.Escape(c.ToString());
                regex.Append(escaped);
                continue;
            }

            if (c == '%') {
                if (i + 1 < form.Length) {
                    i++; // Skip % and check next char
                    c = form[i];
                }
                else {
                    // % at end of string, treat as literal
                    regex.Append(Regex.Escape("%"));
                    continue;
                }
            }

            switch (c) {
                case 'd':
                    if (i + 1 < form.Length && form[i + 1] == 'd') {
                        if (i + 2 < form.Length && form[i + 2] == 'd') {
                            if (i + 3 < form.Length && form[i + 3] == 'd') {
                                var dayNames = string.Join("|", dtfi.DayNames.Where(d => !string.IsNullOrEmpty(d)).Select(Regex.Escape));
                                var pattern = $@"({dayNames})";
                                regex.Append(pattern);
                                i += 3;
                            }
                            else {
                                var dayNames = string.Join("|", dtfi.AbbreviatedDayNames.Where(d => !string.IsNullOrEmpty(d)).Select(Regex.Escape));
                                var pattern = $@"({dayNames})";
                                regex.Append(pattern);
                                i += 2;
                            }
                        }
                        else {
                            regex.Append(@"\d{2}");
                            i++;
                        }
                    }
                    else
                        regex.Append(@"\d{1,2}");
                    break;

                case 'M':
                    if (i + 1 < form.Length && form[i + 1] == 'M') {
                        if (i + 2 < form.Length && form[i + 2] == 'M') {
                            if (i + 3 < form.Length && form[i + 3] == 'M') {
                                var monthNames = string.Join("|", dtfi.MonthNames.Where(m => !string.IsNullOrEmpty(m)).Select(Regex.Escape));
                                var pattern = $@"({monthNames})";
                                regex.Append(pattern);
                                i += 3;
                            }
                            else {
                                var monthNames = string.Join("|", dtfi.AbbreviatedMonthNames.Where(m => !string.IsNullOrEmpty(m)).Select(Regex.Escape));
                                var pattern = $@"({monthNames})";
                                regex.Append(pattern);
                                i += 2;
                            }
                        }
                        else {
                            regex.Append(@"\d{2}");
                            i++;
                        }
                    }
                    else
                        regex.Append(@"\d{1,2}");
                    break;

                case 'y':
                case 'Y':
                    var yCount = 1;
                    while (i + yCount < form.Length && (form[i + yCount] == 'y' || form[i + yCount] == 'Y'))
                        yCount++;

                    if (yCount >= 4)
                        regex.Append(@"\d{4}");
                    else if (yCount == 2)
                        regex.Append(@"\d{2}");
                    else
                        regex.Append(@"\d{1,4}");

                    i += yCount - 1;
                    break;

                case 'h':
                    if (i + 1 < form.Length && form[i + 1] == 'h') {
                        regex.Append(@"\d{2}");
                        i++;
                    }
                    else
                        regex.Append(@"\d{1,2}");
                    break;

                case 'H':
                    if (i + 1 < form.Length && form[i + 1] == 'H') {
                        regex.Append(@"\d{2}");
                        i++;
                    }
                    else
                        regex.Append(@"\d{1,2}");
                    break;

                case 'm':
                    if (i + 1 < form.Length && form[i + 1] == 'm') {
                        regex.Append(@"\d{2}");
                        i++;
                    }
                    else
                        regex.Append(@"\d{1,2}");
                    break;

                case 's':
                    if (i + 1 < form.Length && form[i + 1] == 's') {
                        regex.Append(@"\d{2}");
                        i++;
                    }
                    else
                        regex.Append(@"\d{1,2}");
                    break;

                case 'f':
                case 'F':
                    var fCount = 1;
                    while (i + fCount < form.Length && (form[i + fCount] == 'f' || form[i + fCount] == 'F'))
                        fCount++;
                    regex.Append(@"\d{" + fCount + "}");
                    i += fCount - 1;
                    break;

                case 't':
                    var ampm = string.Join("|", new[] { dtfi.AMDesignator, dtfi.PMDesignator }.Where(s => !string.IsNullOrEmpty(s)).Select(Regex.Escape));
                    var ampmPattern = $@"({ampm})";
                    regex.Append(ampmPattern);
                    if (i + 1 < form.Length && form[i + 1] == 't')
                        i++;
                    break;

                case 'z':
                    var zCount = 1;
                    while (i + zCount < form.Length && form[i + zCount] == 'z')
                        zCount++;
                    regex.Append(@"[+-]\d{2,4}");
                    i += zCount - 1;
                    break;

                case 'K':
                    regex.Append(@"[Z+-]?\d{2}:\d{2}");
                    break;

                case 'g':
                case 'G':
                    break;

                default:
                    var escaped = Regex.Escape(c.ToString());
                    regex.Append(escaped);
                    break;
            }
        }

        return new Regex(regex.ToString(), RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }
}
