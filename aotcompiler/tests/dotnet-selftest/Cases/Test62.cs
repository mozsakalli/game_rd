using System;

// corelib DateTime/TimeSpan: takvim hesabi (.NET ile birebir), unix ms gidis-donus, fark/operatorler, UtcNow mantikli mi.
namespace Demo73
{
    class App73
    {
        public static int Run()
        {
            long h = 0;
            var d = new DateTime(2024, 2, 29, 13, 45, 7); // artik yil
            h = h * 31 + d.Year; h = h * 31 + d.Month; h = h * 31 + d.Day;
            h = h * 31 + d.Hour; h = h * 31 + d.Minute; h = h * 31 + d.Second;
            var e = d.AddDays(1);
            h = h * 31 + e.Month + e.Day; // 3/1
            var y = new DateTime(2000, 12, 31).AddDays(1);
            h = h * 31 + y.Year + y.Month + y.Day; // 2001-01-01
            long ms = new DateTime(1970, 1, 1).Ticks == DateTime.UnixEpoch.Ticks ? 1 : 0;
            h = h * 31 + ms;
            var u = DateTime.UnixEpoch.AddMilliseconds(1700000000123.0);
            h = h * 31 + ((long)(u - DateTime.UnixEpoch).TotalMilliseconds == 1700000000123L ? 1 : 0);
            h = h * 31 + u.Year + u.Month + u.Day + u.Millisecond; // 2023-11-14 .123
            TimeSpan diff = e - d;
            h = h * 31 + (long)diff.TotalHours;
            h = h * 31 + (d < e ? 1 : 0) + (d + TimeSpan.FromHours(24.0) == e ? 2 : 0); // 24.0: .NET 9 int overload'u corelib'de yok
            h = h * 31 + (DateTime.IsLeapYear(1900) ? 1 : 0) + (DateTime.IsLeapYear(2000) ? 2 : 0);
            h = h * 31 + (DateTime.UtcNow.Year >= 2025 ? 1 : 0);
            return (int)(h % 1000000007L);
        }
    }
}
