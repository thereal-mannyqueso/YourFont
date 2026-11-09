using System.Globalization;

namespace FontBook.Infrastructure.Database;

/// <summary>Dates are stored as ISO-8601 UTC text, which sorts correctly and is human readable.</summary>
internal static class DbTime
{
    public static string Now() => Format(DateTimeOffset.UtcNow);

    public static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
