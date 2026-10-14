using System;
using System.Collections.Generic;
using System.Text;

namespace DigitoyEngine;

// Yer tutucu bicimleri (Unity core.upm LocalizationFormatter'in Regex/CultureInfo'suz portu; AOT corelib'de calisir):
//   {n:cap}                                   ilk harf buyuk
//   {n:number}  {n:number:N2} {n:number:F0} {n:number:0.##}   dil ayiracli sayi (binlik/ondalik tablodan)
//   {n:number:short[:esik]}                   1.5K / 12,3M / 2B (tr: B / M / Mr); esik altinda N0
//   {n:time:remaining[:long][:uN][:esik][:week|day|hour|minute|second]}   "3W 4D 5H" / "2 Saat 15 Dakika" / "59:59"
//   {n:time:ago}                              "2 minutes ago" (saniye farki)
//   {n:time:date:<desen>} {n:time:localtime:<desen>}   unix sn -> "14:30", "5 Haziran 2026" (desen: yyyy yy MMMM MMM MM M dddd ddd dd d HH H hh h mm m ss s tt, 'literal')
//   {n:time:datelabel[:relative][:saatDeseni|tarihDeseni]}   "Today 14:00" / "Monday" / "5 June"
// Metinler (gun adlari, "ago", birimler) format.* anahtarlari: tablo satiri ezer, yoksa gomulu FormatDefaults, yoksa en.
// Zaman: NowProvider (sunucu saati baglanabilir), UtcOffsetSeconds (cihaz saat dilimi; host set eder, varsayilan 0).
public static class LocFormat
{
    public static Func<long> NowProvider = () => (DateTime.UtcNow.Ticks - UnixEpochTicks) / TicksPerSecond;
    public static int UtcOffsetSeconds; // localtime/datelabel icin; 0 = UTC

    const long UnixEpochTicks = 621355968000000000L;
    const long TicksPerSecond = 10_000_000L;

    // Dil -> (binlik, ondalik). Katalog disi -> en.
    static (char group, char dec) Separators(string lang) => lang switch
    {
        "tr" or "de" or "es" or "it" or "pt" or "nl" or "id" or "vi" => ('.', ','),
        "fr" or "ru" or "pl" => (' ', ','),
        _ => (',', '.'),
    };

    static (string k, string m, string b) Suffixes(string lang) => lang == "tr" ? ("B", "M", "Mr") : ("K", "M", "B");

    // Giris noktasi: fmt "cap" | "number..." | "time..." | diger (duz ToString).
    public static string Apply(object arg, string fmt, string lang, LocalizationTable table)
    {
        lang = string.IsNullOrEmpty(lang) ? (table?.SourceLanguage ?? "en") : lang.ToLowerInvariant();
        if (fmt == "cap")
        {
            var s = arg?.ToString() ?? "";
            return s.Length > 0 ? char.ToUpperInvariant(s[0]) + s.Substring(1) : s;
        }
        if (fmt.StartsWith("number", StringComparison.Ordinal))
            return Number(arg, fmt, lang);
        if (fmt.StartsWith("time", StringComparison.Ordinal))
            return Time(arg, fmt, lang, table);
        return arg?.ToString() ?? ""; // bilinmeyen bicim: duz metin (IFormattable AOT corelib'de yok)
    }

    // ---------------- sayi ----------------

    static bool ToDouble(object v, out double d)
    {
        switch (v)
        {
            case double x: d = x; return true;
            case float x: d = x; return true;
            case int x: d = x; return true;
            case long x: d = x; return true;
            case short x: d = x; return true;
            case byte x: d = x; return true;
            case string s: return double.TryParse(s, out d);
        }
        d = 0;
        return false;
    }

    static string Number(object val, string fmt, string lang)
    {
        if (!ToDouble(val, out double number))
            return val?.ToString() ?? "";
        var parts = fmt.Split(':');
        string mode = parts.Length > 1 ? parts[1] : "";
        if (mode == "short")
        {
            double threshold = 0;
            if (parts.Length > 2) double.TryParse(parts[2], out threshold);
            double abs = Math.Abs(number);
            if (abs < threshold)
                return Fixed(number, 0, true, lang);
            var (k, m, b) = Suffixes(lang);
            if (abs >= 1_000_000_000) return Compact(number / 1_000_000_000d, lang) + b;
            if (abs >= 1_000_000) return Compact(number / 1_000_000d, lang) + m;
            if (abs >= 1_000) return Compact(number / 1_000d, lang) + k;
            return Fixed(number, 0, true, lang);
        }
        if (mode.Length == 0 || mode == "N0")
            return Fixed(number, 0, true, lang);
        if ((mode[0] == 'N' || mode[0] == 'F') && mode.Length <= 3 && int.TryParse(mode.Substring(1), out int dec))
            return Fixed(number, dec, mode[0] == 'N', lang);
        if (mode[0] == '0' || mode[0] == '#')
        {
            // "0.##": '.' sonrasi '0' sayisi zorunlu, '#' sayisi istege bagli ondalik.
            int dot = mode.IndexOf('.');
            int min = 0, max = 0;
            if (dot >= 0)
                for (int i = dot + 1; i < mode.Length; i++) { if (mode[i] == '0') { min++; max++; } else if (mode[i] == '#') max++; }
            return Flexible(number, min, max, mode.Contains(','), lang);
        }
        return Fixed(number, 0, true, lang);
    }

    // "0.##" ile ayni: en fazla 2 ondalik, sondaki sifirlar atilir.
    static string Compact(double v, string lang) => Flexible(v, 0, 2, false, lang);

    static string Fixed(double v, int decimals, bool group, string lang) => Flexible(v, decimals, decimals, group, lang);

    // Kultursuz sayi: yuvarla, tam/kesir ayir, binlik ayirici ekle. minDec zorunlu, maxDec ust sinir (fazlasi atilir, sondaki sifirlar minDec'e kadar kirpilir).
    static string Flexible(double v, int minDec, int maxDec, bool group, string lang)
    {
        if (double.IsNaN(v) || double.IsInfinity(v))
            return v.ToString();
        var (gs, ds) = Separators(lang);
        bool neg = v < 0;
        double a = Math.Abs(v);
        long scale = 1;
        for (int i = 0; i < maxDec; i++) scale *= 10;
        long scaled = (long)(a * scale + 0.5); // yarim yukari (.NET "0.##" gibi), Math.Round banker's degil
        long ip = scaled / scale, fp = scaled % scale;
        var sb = _sb;
        sb.Clear();
        if (neg && scaled != 0) sb.Append('-');
        string ipStr = ip.ToString();
        for (int i = 0; i < ipStr.Length; i++)
        {
            if (group && i > 0 && (ipStr.Length - i) % 3 == 0) sb.Append(gs);
            sb.Append(ipStr[i]);
        }
        if (maxDec > 0)
        {
            string frac = fp.ToString().PadLeft(maxDec, '0');
            int len = frac.Length;
            while (len > minDec && frac[len - 1] == '0') len--;
            if (len > 0) sb.Append(ds).Append(frac, 0, len);
        }
        return sb.ToString();
    }

    static readonly StringBuilder _sb = new(64);

    // ---------------- zaman ----------------

    static string Time(object val, string fmt, string lang, LocalizationTable table)
    {
        var parts = fmt.Split(':');
        string mode = parts.Length > 1 ? parts[1].ToLowerInvariant() : "remaining";
        ToDouble(val, out double secondsD);
        long seconds = (long)secondsD;
        switch (mode)
        {
            case "ago": return Ago(seconds, lang, table);
            case "date": return DatePattern(seconds, Join(parts, 2, "g"), lang, 0);
            case "localtime": return DatePattern(seconds, Join(parts, 2, "HH:mm"), lang, UtcOffsetSeconds);
            case "datelabel": return DateLabel(seconds, parts, lang, table);
            default: return Remaining(seconds < 0 ? 0 : seconds, parts, lang, table);
        }
    }

    static string Join(string[] parts, int from, string def)
        => parts.Length > from ? string.Join(":", parts, from, parts.Length - from) : def;

    static string Text(string key, string lang, LocalizationTable table)
        => table?.Get(key, lang) ?? FormatDefaults.Get(key, lang) ?? "[MISSING:" + key + "]";

    static string Remaining(long total, string[] parts, string lang, LocalizationTable table)
    {
        bool useLong = false;
        int maxUnits = 3, hms = 0;
        string cap = null;
        for (int i = 2; i < parts.Length; i++)
        {
            string p = parts[i].ToLowerInvariant();
            if (p == "long") useLong = true;
            else if (p.Length == 2 && p[0] == 'u' && char.IsDigit(p[1])) maxUnits = p[1] - '0';
            else if (p is "week" or "day" or "hour" or "minute" or "second") cap = p;
            else if (int.TryParse(p, out int t)) hms = t;
        }
        if (maxUnits < 1) maxUnits = 1;
        long days = total / 86400, hours = total / 3600 % 24, minutes = total / 60 % 60, secs = total % 60;
        if (hms > 0 && total < hms)
        {
            long totalHours = total / 3600;
            if (maxUnits >= 4) return P2(days) + ":" + P2(hours) + ":" + P2(minutes) + ":" + P2(secs);
            if (maxUnits >= 3 || totalHours > 0) return P2(totalHours) + ":" + P2(minutes) + ":" + P2(secs);
            return P2(minutes) + ":" + P2(secs);
        }
        if (total < 60)
            return useLong ? secs + " " + Text(secs == 1 ? "format.time.unit.second.singular" : "format.time.unit.second.plural", lang, table)
                           : secs + Text("format.time.unit.second.abbr", lang, table);
        long w, d, h, m, s;
        switch (cap)
        {
            case "second": w = d = h = m = 0; s = total; break;
            case "minute": w = d = h = 0; m = total / 60; s = secs; break;
            case "hour": w = d = 0; h = total / 3600; m = minutes; s = secs; break;
            case "day": w = 0; d = days; h = hours; m = minutes; s = secs; break;
            default: w = days / 7; d = days % 7; h = hours; m = minutes; s = secs; break;
        }
        var sb = _sb;
        sb.Clear();
        int n = 0;
        void Add(long v, string unit, bool plural)
        {
            if (v <= 0 || n >= maxUnits) return;
            if (n > 0) sb.Append(' ');
            sb.Append(v);
            if (useLong) sb.Append(' ').Append(Text("format.time.unit." + unit + (plural ? ".plural" : ".singular"), lang, table));
            else sb.Append(Text("format.time.unit." + unit + ".abbr", lang, table));
            n++;
        }
        Add(w, "week", w != 1); Add(d, "day", d != 1); Add(h, "hour", h != 1); Add(m, "minute", m != 1); Add(s, "second", s != 1);
        if (n == 0)
            return useLong ? "0 " + Text("format.time.unit.second.plural", lang, table) : "0" + Text("format.time.unit.second.abbr", lang, table);
        return sb.ToString();
    }

    static string Ago(long seconds, string lang, LocalizationTable table)
    {
        if (seconds < 0) seconds = 0;
        long mins = seconds / 60, hours = mins / 60, days = hours / 24, weeks = days / 7, months = days / 30;
        string key; long num = -1;
        if (mins == 0) key = "less.minute.ago";
        else if (mins == 1) key = "a.minute.ago";
        else if (mins < 60) { key = "minutes.ago"; num = mins; }
        else if (hours == 1) key = "an.hour.ago";
        else if (hours < 24) { key = "hours.ago"; num = hours; }
        else if (days == 1) key = "a.day.ago";
        else if (days < 7) { key = "days.ago"; num = days; }
        else if (weeks == 1) key = "a.week.ago";
        else if (weeks < 4) { key = "weeks.ago"; num = weeks; }
        else if (months == 1) key = "a.month.ago";
        else if (months < 12) { key = "months.ago"; num = months; }
        else key = "more.than.a.year";
        string text = Text("format." + key, lang, table);
        return num < 0 ? text : Localize.FormatString(text, new object[] { num }, lang, table);
    }

    static string DateLabel(long ts, string[] parts, string lang, LocalizationTable table)
    {
        long local = ts + UtcOffsetSeconds, nowLocal = NowProvider() + UtcOffsetSeconds;
        long msgDay = FloorDiv(local, 86400), today = FloorDiv(nowLocal, 86400);
        long diff = today - msgDay;
        bool relative = false;
        int pi = 2;
        if (parts.Length > 2 && parts[2].ToLowerInvariant() == "relative") { relative = true; pi = 3; }
        string timePattern = null, datePattern = null;
        if (parts.Length > pi)
        {
            string raw = string.Join(":", parts, pi, parts.Length - pi);
            int pipe = raw.IndexOf('|');
            if (pipe >= 0)
            {
                timePattern = pipe > 0 ? raw.Substring(0, pipe) : null;
                datePattern = pipe < raw.Length - 1 ? raw.Substring(pipe + 1) : null;
            }
            else datePattern = raw;
        }
        if (relative && diff > -7 && diff < 7)
        {
            string day = diff == 0 ? Text("format.time.today", lang, table)
                : diff == 1 ? Text("format.time.yesterday", lang, table)
                : diff == -1 ? Text("format.time.tomorrow", lang, table)
                : DatePattern(ts, "dddd", lang, UtcOffsetSeconds);
            if (!string.IsNullOrEmpty(timePattern))
                return Localize.FormatString(Text("format.time.at", lang, table), new object[] { day, DatePattern(ts, timePattern, lang, UtcOffsetSeconds) }, lang, table);
            return day;
        }
        if (!string.IsNullOrEmpty(datePattern))
            return DatePattern(ts, datePattern, lang, UtcOffsetSeconds);
        Civil(local, out int y, out _, out _, out _, out _, out _);
        Civil(nowLocal, out int ny, out _, out _, out _, out _, out _);
        return DatePattern(ts, Text(y == ny ? "format.date.monthday" : "format.date.full", lang, table), lang, UtcOffsetSeconds);
    }

    // Kultursuz sifir dolgulu (corelib'de ToString("D2") yok).
    static string P2(this long v) => v < 10 && v >= 0 ? "0" + v : v.ToString();
    static string P2(this int v) => v < 10 && v >= 0 ? "0" + v : v.ToString();
    static string P4(this int v) { var s = v.ToString(); while (s.Length < 4) s = "0" + s; return s; }

    static long FloorDiv(long a, long b) => a >= 0 ? a / b : -((-a + b - 1) / b);

    // Unix sn (+ofset) -> takvim (proleptik Gregoryen; corelib DateTime ile ayni).
    static void Civil(long unix, out int year, out int month, out int day, out int hour, out int minute, out int second)
    {
        long days = FloorDiv(unix, 86400);
        long rem = unix - days * 86400;
        hour = (int)(rem / 3600); minute = (int)(rem / 60 % 60); second = (int)(rem % 60);
        var dt = new DateTime(UnixEpochTicks + days * 864_000_000_000L);
        year = dt.Year; month = dt.Month; day = dt.Day;
    }

    static int DayOfWeek(long unix) => (int)(((FloorDiv(unix, 86400) % 7) + 11) % 7); // 1970-01-01 Persembe(4); 0 = Pazar

    // .NET ozel tarih deseni altkumesi; ay/gun adlari gomulu (en/tr/de/fr/es; digerleri en).
    public static string DatePattern(long unix, string pattern, string lang, int offsetSeconds)
    {
        long local = unix + offsetSeconds;
        Civil(local, out int Y, out int M, out int D, out int h, out int mi, out int s);
        int dow = DayOfWeek(local);
        var names = Names(lang);
        var sb = new StringBuilder(pattern.Length + 16);
        for (int i = 0; i < pattern.Length;)
        {
            char c = pattern[i];
            if (c == '\'')
            {
                int end = pattern.IndexOf('\'', i + 1);
                if (end < 0) end = pattern.Length;
                sb.Append(pattern, i + 1, end - i - 1);
                i = end + 1;
                continue;
            }
            int run = 1;
            while (i + run < pattern.Length && pattern[i + run] == c) run++;
            switch (c)
            {
                case 'y': sb.Append(run <= 2 ? (Y % 100).P2() : Y.P4()); break;
                case 'M': sb.Append(run >= 4 ? names.Months[M - 1] : run == 3 ? names.MonthsShort[M - 1] : run == 2 ? M.P2() : M.ToString()); break;
                case 'd': sb.Append(run >= 4 ? names.Days[dow] : run == 3 ? names.DaysShort[dow] : run == 2 ? D.P2() : D.ToString()); break;
                case 'H': sb.Append(run >= 2 ? h.P2() : h.ToString()); break;
                case 'h': { int h12 = h % 12 == 0 ? 12 : h % 12; sb.Append(run >= 2 ? h12.P2() : h12.ToString()); break; }
                case 'm': sb.Append(run >= 2 ? mi.P2() : mi.ToString()); break;
                case 's': sb.Append(run >= 2 ? s.P2() : s.ToString()); break;
                case 't': sb.Append(run >= 2 ? (h < 12 ? "AM" : "PM") : (h < 12 ? "A" : "P")); break;
                case 'g': sb.Append(Y.P4()).Append('-').Append(M.P2()).Append('-').Append(D.P2()).Append(' ').Append(h.P2()).Append(':').Append(mi.P2()); break;
                default: sb.Append(c, run); break;
            }
            i += run;
        }
        return sb.ToString();
    }

    sealed class LangNames
    {
        public string[] Months, MonthsShort, Days, DaysShort;
    }

    static readonly Dictionary<string, LangNames> _names = new()
    {
        ["en"] = N("January February March April May June July August September October November December", "Sunday Monday Tuesday Wednesday Thursday Friday Saturday"),
        ["tr"] = N("Ocak Şubat Mart Nisan Mayıs Haziran Temmuz Ağustos Eylül Ekim Kasım Aralık", "Pazar Pazartesi Salı Çarşamba Perşembe Cuma Cumartesi"),
        ["de"] = N("Januar Februar März April Mai Juni Juli August September Oktober November Dezember", "Sonntag Montag Dienstag Mittwoch Donnerstag Freitag Samstag"),
        ["fr"] = N("janvier février mars avril mai juin juillet août septembre octobre novembre décembre", "dimanche lundi mardi mercredi jeudi vendredi samedi"),
        ["es"] = N("enero febrero marzo abril mayo junio julio agosto septiembre octubre noviembre diciembre", "domingo lunes martes miércoles jueves viernes sábado"),
    };

    static LangNames N(string months, string days)
    {
        var m = months.Split(' ');
        var d = days.Split(' ');
        var ms = new string[12];
        var ds = new string[7];
        for (int i = 0; i < 12; i++) ms[i] = m[i].Length > 3 ? m[i].Substring(0, 3) : m[i];
        for (int i = 0; i < 7; i++) ds[i] = d[i].Length > 3 ? d[i].Substring(0, 3) : d[i];
        return new LangNames { Months = m, MonthsShort = ms, Days = d, DaysShort = ds };
    }

    static LangNames Names(string lang) => _names.TryGetValue(lang ?? "en", out var n) ? n : _names["en"];
}

// Gomulu format.* metinleri (Unity FormatDefaults paritesi): tablo satiri ayni key ile ezer. Sira: en tr es de fr.
public static class FormatDefaults
{
    static readonly string[] Langs = { "en", "tr", "es", "de", "fr" };

    static readonly Dictionary<string, string[]> Texts = new()
    {
        ["format.time.today"] = new[] { "Today", "Bugün", "Hoy", "Heute", "Aujourd'hui" },
        ["format.time.yesterday"] = new[] { "Yesterday", "Dün", "Ayer", "Gestern", "Hier" },
        ["format.time.tomorrow"] = new[] { "Tomorrow", "Yarın", "Mañana", "Morgen", "Demain" },
        ["format.time.at"] = new[] { "{0} at {1}", "{0} {1}", "{0} a las {1}", "{0} um {1}", "{0} à {1}" },
        ["format.date.monthday"] = new[] { "MMMM d", "d MMMM", "d 'de' MMMM", "d. MMMM", "d MMMM" },
        ["format.date.full"] = new[] { "d MMMM yyyy", "d MMMM yyyy", "d 'de' MMMM 'de' yyyy", "d. MMMM yyyy", "d MMMM yyyy" },
        ["format.less.minute.ago"] = new[] { "less than a minute ago", "1 dakikadan az önce", "hace menos de un minuto", "vor knapp einer Minute", "il y a moins d'une minute" },
        ["format.a.minute.ago"] = new[] { "a minute ago", "1 dakika önce", "hace un minuto", "vor einer Minute", "il y a une minute" },
        ["format.minutes.ago"] = new[] { "{0} minutes ago", "{0} dakika önce", "hace {0} minutos", "vor {0} Minuten", "il y a {0} minutes" },
        ["format.an.hour.ago"] = new[] { "an hour ago", "1 saat önce", "hace una hora", "vor einer Stunde", "il y a une heure" },
        ["format.hours.ago"] = new[] { "{0} hours ago", "{0} saat önce", "hace {0} horas", "vor {0} Stunden", "il y a {0} heures" },
        ["format.a.day.ago"] = new[] { "a day ago", "1 gün önce", "hace un día", "vor einem Tag", "il y a un jour" },
        ["format.days.ago"] = new[] { "{0} days ago", "{0} gün önce", "hace {0} días", "vor {0} Tagen", "il y a {0} jours" },
        ["format.a.week.ago"] = new[] { "a week ago", "1 hafta önce", "hace una semana", "vor einer Woche", "il y a une semaine" },
        ["format.weeks.ago"] = new[] { "{0} weeks ago", "{0} hafta önce", "hace {0} semanas", "vor {0} Wochen", "il y a {0} semaines" },
        ["format.a.month.ago"] = new[] { "a month ago", "1 ay önce", "hace un mes", "vor einem Monat", "il y a un mois" },
        ["format.months.ago"] = new[] { "{0} months ago", "{0} ay önce", "hace {0} meses", "vor {0} Monaten", "il y a {0} mois" },
        ["format.more.than.a.month"] = new[] { "more than a month ago", "1 aydan uzun süre önce", "hace más de un mes", "vor mehr als einem Monat", "il y a plus d'un mois" },
        ["format.more.than.a.year"] = new[] { "more than a year ago", "1 yıldan uzun süre önce", "hace más de un año", "vor mehr als einem Jahr", "il y a plus d'un an" },
        ["format.time.unit.week.singular"] = new[] { "Week", "Hafta", "Semana", "Woche", "Semaine" },
        ["format.time.unit.week.plural"] = new[] { "Weeks", "Hafta", "Semanas", "Wochen", "Semaines" },
        ["format.time.unit.day.singular"] = new[] { "Day", "Gün", "Día", "Tag", "Jour" },
        ["format.time.unit.day.plural"] = new[] { "Days", "Gün", "Días", "Tage", "Jours" },
        ["format.time.unit.hour.singular"] = new[] { "Hour", "Saat", "Hora", "Stunde", "Heure" },
        ["format.time.unit.hour.plural"] = new[] { "Hours", "Saat", "Horas", "Stunden", "Heures" },
        ["format.time.unit.minute.singular"] = new[] { "Minute", "Dakika", "Minuto", "Minute", "Minute" },
        ["format.time.unit.minute.plural"] = new[] { "Minutes", "Dakika", "Minutos", "Minuten", "Minutes" },
        ["format.time.unit.second.singular"] = new[] { "Second", "Saniye", "Segundo", "Sekunde", "Seconde" },
        ["format.time.unit.second.plural"] = new[] { "Seconds", "Saniye", "Segundos", "Sekunden", "Secondes" },
        ["format.time.unit.week.abbr"] = new[] { "W", "H", "S", "W", "S" },
        ["format.time.unit.day.abbr"] = new[] { "D", "G", "D", "T", "J" },
        ["format.time.unit.hour.abbr"] = new[] { "H", "S", "H", "H", "H" },
        ["format.time.unit.minute.abbr"] = new[] { "M", "DK", "M", "M", "M" },
        ["format.time.unit.second.abbr"] = new[] { "S", "SN", "S", "S", "S" },
    };

    public static IEnumerable<string> Keys => Texts.Keys;
    public static bool Contains(string key) => Texts.ContainsKey(key);

    public static string Get(string key, string lang)
    {
        if (!Texts.TryGetValue(key, out var v))
            return null;
        int i = 0;
        for (int k = 0; k < Langs.Length; k++)
            if (Langs[k] == lang) { i = k; break; }
        return v[i];
    }
}
