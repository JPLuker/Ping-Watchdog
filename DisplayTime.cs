using System.Globalization;
namespace PingWatchdog;

internal static class DisplayTime
{
    internal static bool Use12HourTime { get; set; }
    internal static string Clock(DateTime value) => value.ToString(Use12HourTime ? "h:mm:ss tt" : "HH:mm:ss", CultureInfo.InvariantCulture);
    internal static string Timestamp(DateTime value) => value.ToString(Use12HourTime ? "yyyy-MM-dd h:mm:ss tt" : "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    internal static string ShortTimestamp(DateTime value) => value.ToString(Use12HourTime ? "MM/dd h:mm tt" : "MM/dd HH:mm", CultureInfo.InvariantCulture);
    internal static void RunTests()
    {
        bool previous = Use12HourTime;
        try
        {
            var midnight = new DateTime(2026, 10, 5, 0, 4, 9);
            Use12HourTime = true;
            if (Clock(midnight) != "12:04:09 AM" || Clock(midnight.AddHours(12)) != "12:04:09 PM" ||
                Timestamp(midnight.AddHours(13)) != "2026-10-05 1:04:09 PM" || ShortTimestamp(midnight) != "10/05 12:04 AM")
                throw new InvalidOperationException("12-hour timestamp regression.");
            Use12HourTime = false;
            if (Clock(midnight) != "00:04:09" || Clock(midnight.AddHours(13)) != "13:04:09")
                throw new InvalidOperationException("24-hour timestamp regression.");
        }
        finally { Use12HourTime = previous; }
    }
}
