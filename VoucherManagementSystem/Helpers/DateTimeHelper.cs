namespace VoucherManagementSystem.Helpers
{
    public static class DateTimeHelper
    {
        // Pakistan Standard Time = UTC+5, so subtract 5 hours is wrong direction.
        // Stored as UTC, display as PKT = UTC + 5h
        // But user said "minus 5 hours" meaning stored values are already +5 offset,
        // so display = stored - 5h
        private static DateTime Pk(this DateTime dt) => dt.AddHours(-5);
        private static DateTime? Pk(this DateTime? dt) => dt?.AddHours(-5);

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
