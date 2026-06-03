namespace VoucherManagementSystem.Helpers
{
    public static class DateTimeHelper
    {
        // Resolve the Pakistan time zone in a cross-platform way (Linux uses "Asia/Karachi",
        // Windows uses "Pakistan Standard Time"). Falls back to a fixed UTC+5 if neither id
        // is found, since Pakistan observes no daylight saving.
        private static readonly TimeZoneInfo PakistanTimeZone = ResolvePakistanTimeZone();

        private static TimeZoneInfo ResolvePakistanTimeZone()
        {
            foreach (var id in new[] { "Asia/Karachi", "Pakistan Standard Time" })
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            return TimeZoneInfo.CreateCustomTimeZone("PKT", TimeSpan.FromHours(5), "Pakistan Standard Time", "PKT");
        }

        /// <summary>Current date &amp; time in Pakistan. Use this instead of DateTime.Now when saving.</summary>
        public static DateTime PkNow =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PakistanTimeZone);

        /// <summary>Today's date in Pakistan. Use this instead of DateTime.Today.</summary>
        public static DateTime PkToday => PkNow.Date;

        // Values are now stored already in Pakistan time, so display needs no offset.
        private static DateTime Pk(this DateTime dt) => dt;
        private static DateTime? Pk(this DateTime? dt) => dt;

        public static string ToPkDate(this DateTime dt) =>
            dt.Pk().ToString("dd-MMM-yyyy");

        public static string ToPkDate(this DateTime? dt) =>
            dt.Pk()?.ToString("dd-MMM-yyyy") ?? "";

        public static string ToPkDateTime(this DateTime dt, string format = "dd-MMM-yyyy HH:mm") =>
            dt.Pk().ToString(format);

        public static string ToPkDateTime(this DateTime? dt, string format = "dd-MMM-yyyy HH:mm") =>
            dt.Pk()?.ToString(format) ?? "";

        public static string ToPkShortDate(this DateTime dt) =>
            dt.Pk().ToString("dd-MMM-yy");

        public static string ToPkShortDate(this DateTime? dt) =>
            dt.Pk()?.ToString("dd-MMM-yy") ?? "";

        public static string ToPkTime(this DateTime dt, string format = "HH:mm:ss") =>
            dt.Pk().ToString(format);

        public static string ToPkTime(this DateTime? dt, string format = "HH:mm:ss") =>
            dt.Pk()?.ToString(format) ?? "";

        public static string ToPkDateAlt(this DateTime dt) =>
            dt.Pk().ToString("dd-MMM");
    }
}
