using System.Globalization;

namespace HumanGateway.Core.Time;

/// <summary>
/// RFC 3339 UTC timestamp formatting/parsing for protocol strings (schemas/common.schema.json#/$defs/timestamp).
/// All protocol timestamps are UTC with a trailing <c>Z</c>; milliseconds (3 fractional digits) are emitted
/// to stay within the schema's <c>.[0-9]{1,9}Z</c> fractional-second allowance.
/// </summary>
public static class ProtocolTime
{
    private const string FormatString = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    /// <summary>Formats the current UTC instant as an RFC 3339 timestamp.</summary>
    public static string Now() => Format(DateTimeOffset.UtcNow);

    /// <summary>Formats a UTC instant as an RFC 3339 timestamp.</summary>
    public static string Format(DateTimeOffset timestamp)
        => timestamp.UtcDateTime.ToString(FormatString, CultureInfo.InvariantCulture);

    /// <summary>Parses an RFC 3339 timestamp into a <see cref="DateTimeOffset"/> (round-trip kind).</summary>
    public static DateTimeOffset Parse(string value)
        => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    /// <summary>Tries to parse an RFC 3339 timestamp; returns false on an absent or malformed value.</summary>
    public static bool TryParse(string? value, out DateTimeOffset result)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result);
}
