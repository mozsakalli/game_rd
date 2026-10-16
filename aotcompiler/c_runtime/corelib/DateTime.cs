// corelib: minimal DateTime/TimeSpan. Tek alan = tick (100ns, 0001-01-01'den). Takvim hesabi
// proleptik Gregoryen (BCL ile ayni). Saat dilimi YOK: Now == UtcNow (release'te yerel saat gerekirse host'tan gelir).
// Kind/format/parse/kultur yok — oyun kodu zaman damgasi + fark alir, o kadar.
namespace System
{
    public struct TimeSpan
    {
        public const long TicksPerMillisecond = 10000;
        public const long TicksPerSecond = TicksPerMillisecond * 1000;
        public const long TicksPerMinute = TicksPerSecond * 60;
        public const long TicksPerHour = TicksPerMinute * 60;
        public const long TicksPerDay = TicksPerHour * 24;

        readonly long _ticks;
        public TimeSpan(long ticks) { _ticks = ticks; }

        public static readonly TimeSpan Zero = new TimeSpan(0);
        public long Ticks { get { return _ticks; } }
        public double TotalMilliseconds { get { return (double)_ticks / TicksPerMillisecond; } }
        public double TotalSeconds { get { return (double)_ticks / TicksPerSecond; } }
        public double TotalMinutes { get { return (double)_ticks / TicksPerMinute; } }
        public double TotalHours { get { return (double)_ticks / TicksPerHour; } }
        public double TotalDays { get { return (double)_ticks / TicksPerDay; } }

        public static TimeSpan FromMilliseconds(double v) { return new TimeSpan((long)(v * TicksPerMillisecond)); }
        public static TimeSpan FromSeconds(double v) { return new TimeSpan((long)(v * TicksPerSecond)); }
        public static TimeSpan FromMinutes(double v) { return new TimeSpan((long)(v * TicksPerMinute)); }
        public static TimeSpan FromHours(double v) { return new TimeSpan((long)(v * TicksPerHour)); }
        public static TimeSpan FromDays(double v) { return new TimeSpan((long)(v * TicksPerDay)); }

        public static TimeSpan operator +(TimeSpan a, TimeSpan b) { return new TimeSpan(a._ticks + b._ticks); }
        public static TimeSpan operator -(TimeSpan a, TimeSpan b) { return new TimeSpan(a._ticks - b._ticks); }
        public static bool operator ==(TimeSpan a, TimeSpan b) { return a._ticks == b._ticks; }
        public static bool operator !=(TimeSpan a, TimeSpan b) { return a._ticks != b._ticks; }
        public static bool operator <(TimeSpan a, TimeSpan b) { return a._ticks < b._ticks; }
        public static bool operator >(TimeSpan a, TimeSpan b) { return a._ticks > b._ticks; }
        public static bool operator <=(TimeSpan a, TimeSpan b) { return a._ticks <= b._ticks; }
        public static bool operator >=(TimeSpan a, TimeSpan b) { return a._ticks >= b._ticks; }
    }

    public struct DateTime
    {
        const long UnixEpochTicks = 621355968000000000; // 1970-01-01
        const int DaysPer4Years = 1461, DaysPer100Years = 36524, DaysPer400Years = 146097;
        static readonly int[] DaysToMonth365 = { 0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334, 365 };
        static readonly int[] DaysToMonth366 = { 0, 31, 60, 91, 121, 152, 182, 213, 244, 274, 305, 335, 366 };

        readonly long _ticks;
        public DateTime(long ticks) { _ticks = ticks; }
        public DateTime(int year, int month, int day) : this(year, month, day, 0, 0, 0) { }
        public DateTime(int year, int month, int day, int hour, int minute, int second)
        {
            if (year < 1 || year > 9999 || month < 1 || month > 12) throw new InvalidOperationException("DateTime: year/month out of range");
            int[] days = IsLeapYear(year) ? DaysToMonth366 : DaysToMonth365;
            if (day < 1 || day > days[month] - days[month - 1]) throw new InvalidOperationException("DateTime: day out of range");
            int y = year - 1;
            long n = y * 365 + y / 4 - y / 100 + y / 400 + days[month - 1] + day - 1;
            _ticks = n * TimeSpan.TicksPerDay + hour * TimeSpan.TicksPerHour + minute * TimeSpan.TicksPerMinute + second * TimeSpan.TicksPerSecond;
        }

        static extern long GetUtcNowTicks(); // C: System_DateTime_GetUtcNowTicks (corelib.c), 100ns, 0001-01-01'den

        public static readonly DateTime MinValue = new DateTime(0);
        public static readonly DateTime UnixEpoch = new DateTime(UnixEpochTicks);
        public static DateTime UtcNow { get { return new DateTime(GetUtcNowTicks()); } }
        public static DateTime Now { get { return UtcNow; } } // saat dilimi yok

        public long Ticks { get { return _ticks; } }
        public int Year { get { return GetPart(0); } }
        public int Month { get { return GetPart(1); } }
        public int Day { get { return GetPart(2); } }
        public int Hour { get { return (int)(_ticks / TimeSpan.TicksPerHour % 24); } }
        public int Minute { get { return (int)(_ticks / TimeSpan.TicksPerMinute % 60); } }
        public int Second { get { return (int)(_ticks / TimeSpan.TicksPerSecond % 60); } }
        public int Millisecond { get { return (int)(_ticks / TimeSpan.TicksPerMillisecond % 1000); } }
        public DateTime Date { get { return new DateTime(_ticks - _ticks % TimeSpan.TicksPerDay); } }
        public TimeSpan TimeOfDay { get { return new TimeSpan(_ticks % TimeSpan.TicksPerDay); } }

        public static bool IsLeapYear(int year) { return (year & 3) == 0 && (year % 100 != 0 || year % 400 == 0); }

        // part: 0=yil 1=ay 2=gun (BCL GetDatePart algoritmasi)
        int GetPart(int part)
        {
            int n = (int)(_ticks / TimeSpan.TicksPerDay);
            int y400 = n / DaysPer400Years; n -= y400 * DaysPer400Years;
            int y100 = n / DaysPer100Years; if (y100 == 4) y100 = 3; n -= y100 * DaysPer100Years;
            int y4 = n / DaysPer4Years; n -= y4 * DaysPer4Years;
            int y1 = n / 365; if (y1 == 4) y1 = 3;
            if (part == 0) return y400 * 400 + y100 * 100 + y4 * 4 + y1 + 1;
            n -= y1 * 365;
            bool leap = y1 == 3 && (y4 != 24 || y100 == 3);
            int[] days = leap ? DaysToMonth366 : DaysToMonth365;
            int m = (n >> 5) + 1;
            while (n >= days[m]) m++;
            return part == 1 ? m : n - days[m - 1] + 1;
        }

        public DateTime AddTicks(long v) { return new DateTime(_ticks + v); }
        public DateTime AddMilliseconds(double v) { return new DateTime(_ticks + (long)(v * TimeSpan.TicksPerMillisecond)); }
        public DateTime AddSeconds(double v) { return new DateTime(_ticks + (long)(v * TimeSpan.TicksPerSecond)); }
        public DateTime AddMinutes(double v) { return new DateTime(_ticks + (long)(v * TimeSpan.TicksPerMinute)); }
        public DateTime AddHours(double v) { return new DateTime(_ticks + (long)(v * TimeSpan.TicksPerHour)); }
        public DateTime AddDays(double v) { return new DateTime(_ticks + (long)(v * TimeSpan.TicksPerDay)); }
        public DateTime Add(TimeSpan v) { return new DateTime(_ticks + v.Ticks); }
        public TimeSpan Subtract(DateTime v) { return new TimeSpan(_ticks - v._ticks); }

        public static TimeSpan operator -(DateTime a, DateTime b) { return new TimeSpan(a._ticks - b._ticks); }
        public static DateTime operator +(DateTime a, TimeSpan b) { return new DateTime(a._ticks + b.Ticks); }
        public static DateTime operator -(DateTime a, TimeSpan b) { return new DateTime(a._ticks - b.Ticks); }
        public static bool operator ==(DateTime a, DateTime b) { return a._ticks == b._ticks; }
        public static bool operator !=(DateTime a, DateTime b) { return a._ticks != b._ticks; }
        public static bool operator <(DateTime a, DateTime b) { return a._ticks < b._ticks; }
        public static bool operator >(DateTime a, DateTime b) { return a._ticks > b._ticks; }
        public static bool operator <=(DateTime a, DateTime b) { return a._ticks <= b._ticks; }
        public static bool operator >=(DateTime a, DateTime b) { return a._ticks >= b._ticks; }

        public override bool Equals(object other) { return other is DateTime d && d._ticks == _ticks; }
        public override int GetHashCode() { return (int)_ticks ^ (int)(_ticks >> 32); }
        public override string ToString() { return ToString("yyyy-MM-dd HH:mm:ss"); } // kultur yok: sabit ISO benzeri bicim

        // Ozel bicim alt kumesi (kultur yok): yyyy yy MM M dd d HH H mm m ss s fff ff f, tek/cift tirnak literal, '\' kacis.
        // Standart tek harfli bicimler ("d","T"...) YOK -> FormatException.
        public string ToString(string format)
        {
            if (format == null || format.Length == 0) return ToString();
            if (format.Length == 1) throw new FormatException("Standard DateTime format strings are not supported in corelib: '" + format + "'.");
            var sb = new Text.StringBuilder();
            int i = 0, n = format.Length;
            while (i < n)
            {
                char c = format[i];
                if (c == '\'' || c == '"')
                {
                    int j = i + 1;
                    while (j < n && format[j] != c) { sb.Append(format[j]); j++; }
                    i = j + 1; continue;
                }
                if (c == '\\' && i + 1 < n) { sb.Append(format[i + 1]); i += 2; continue; }
                int run = 1;
                while (i + run < n && format[i + run] == c) run++;
                switch (c)
                {
                    case 'y': AppendPadded(sb, run >= 4 ? Year : Year % 100, run >= 4 ? 4 : 2); break;
                    case 'M': AppendPadded(sb, Month, run >= 2 ? 2 : 1); break;
                    case 'd': AppendPadded(sb, Day, run >= 2 ? 2 : 1); break;
                    case 'H': AppendPadded(sb, Hour, run >= 2 ? 2 : 1); break;
                    case 'h': { int h = Hour % 12; if (h == 0) h = 12; AppendPadded(sb, h, run >= 2 ? 2 : 1); break; }
                    case 'm': AppendPadded(sb, Minute, run >= 2 ? 2 : 1); break;
                    case 's': AppendPadded(sb, Second, run >= 2 ? 2 : 1); break;
                    case 'f':
                    {
                        int digits = run > 7 ? 7 : run;
                        long frac = _ticks % TimeSpan.TicksPerSecond; // 7 basamak
                        for (int k = 7; k > digits; k--) frac /= 10;
                        AppendPadded(sb, (int)frac, digits);
                        break;
                    }
                    case 't': sb.Append(Hour < 12 ? "AM" : "PM"); break;
                    default: for (int k = 0; k < run; k++) sb.Append(c); break;
                }
                i += run;
            }
            return sb.ToString();
        }
        static void AppendPadded(Text.StringBuilder sb, int value, int width)
        {
            string s = value.ToString();
            for (int k = s.Length; k < width; k++) sb.Append('0');
            sb.Append(s);
        }
    }

    // UTC'ye sabitlenmis DateTime (Offset her zaman sifir: corelib'de saat dilimi yok). Unix zaman damgasi yuzeyi icin.
    public struct DateTimeOffset
    {
        const long UnixEpochTicks = 621355968000000000;
        readonly DateTime _utc;
        public DateTimeOffset(DateTime dateTime) { _utc = dateTime; }
        public DateTimeOffset(long ticks, TimeSpan offset) { _utc = new DateTime(ticks - offset.Ticks); }

        public static DateTimeOffset UtcNow { get { return new DateTimeOffset(DateTime.UtcNow); } }
        public static DateTimeOffset Now { get { return UtcNow; } }
        public static readonly DateTimeOffset MinValue = new DateTimeOffset(DateTime.MinValue);
        public static readonly DateTimeOffset UnixEpoch = new DateTimeOffset(DateTime.UnixEpoch);

        public DateTime UtcDateTime { get { return _utc; } }
        public DateTime DateTime { get { return _utc; } }
        public TimeSpan Offset { get { return TimeSpan.Zero; } }
        public long Ticks { get { return _utc.Ticks; } }
        public long UtcTicks { get { return _utc.Ticks; } }

        public long ToUnixTimeSeconds() { return (_utc.Ticks - UnixEpochTicks) / TimeSpan.TicksPerSecond; }
        public long ToUnixTimeMilliseconds() { return (_utc.Ticks - UnixEpochTicks) / TimeSpan.TicksPerMillisecond; }
        public static DateTimeOffset FromUnixTimeSeconds(long seconds) { return new DateTimeOffset(new DateTime(UnixEpochTicks + seconds * TimeSpan.TicksPerSecond)); }
        public static DateTimeOffset FromUnixTimeMilliseconds(long ms) { return new DateTimeOffset(new DateTime(UnixEpochTicks + ms * TimeSpan.TicksPerMillisecond)); }

        public DateTimeOffset AddSeconds(double v) { return new DateTimeOffset(_utc.AddSeconds(v)); }
        public DateTimeOffset AddMilliseconds(double v) { return new DateTimeOffset(_utc.AddMilliseconds(v)); }
        public DateTimeOffset Add(TimeSpan v) { return new DateTimeOffset(_utc.Add(v)); }

        public static TimeSpan operator -(DateTimeOffset a, DateTimeOffset b) { return a._utc - b._utc; }
        public static DateTimeOffset operator +(DateTimeOffset a, TimeSpan b) { return new DateTimeOffset(a._utc + b); }
        public static DateTimeOffset operator -(DateTimeOffset a, TimeSpan b) { return new DateTimeOffset(a._utc - b); }
        public static bool operator ==(DateTimeOffset a, DateTimeOffset b) { return a._utc == b._utc; }
        public static bool operator !=(DateTimeOffset a, DateTimeOffset b) { return a._utc != b._utc; }
        public static bool operator <(DateTimeOffset a, DateTimeOffset b) { return a._utc < b._utc; }
        public static bool operator >(DateTimeOffset a, DateTimeOffset b) { return a._utc > b._utc; }
        public static bool operator <=(DateTimeOffset a, DateTimeOffset b) { return a._utc <= b._utc; }
        public static bool operator >=(DateTimeOffset a, DateTimeOffset b) { return a._utc >= b._utc; }
        public override bool Equals(object other) { return other is DateTimeOffset d && d._utc == _utc; }
        public override int GetHashCode() { return _utc.GetHashCode(); }
        public override string ToString() { return _utc.ToString() + " +00:00"; }
        public string ToString(string format) { return _utc.ToString(format); }
    }
}
