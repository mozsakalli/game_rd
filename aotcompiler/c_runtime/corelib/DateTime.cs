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
    }
}
