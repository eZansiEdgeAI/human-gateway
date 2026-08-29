using System.Globalization;
using System.Text;

namespace HumanGateway.Core.Cursor;

/// <summary>
/// A receiver-side sync position: the highest per-gateway sequence number the receiver has applied.
/// Cursors are opaque to the sender and bounded to ≤1024 URL-safe characters (SYNC-FR-03).
/// </summary>
public readonly record struct CursorPosition(long Sequence)
{
    /// <summary>The "no position yet" cursor (first exchange).</summary>
    public static readonly CursorPosition Start = new(0);

    /// <summary>Advances the position to cover <paramref name="sequence"/> (monotonic — never rewinds).</summary>
    public CursorPosition Advance(long sequence) => new(Math.Max(Sequence, sequence));
}

/// <summary>
/// Encodes/decodes the opaque, URL-safe cursor token (SYNC-FR-03). The receiving side issues cursors that
/// encode its position; senders only store and echo the token back, never interpret it (the token format is
/// an implementation detail of the issuing side). Base64url keeps the token within the schema's
/// <c>^[A-Za-z0-9._:/-]*$</c> pattern.
/// </summary>
public static class CursorCodec
{
    private const string Prefix = "v1:";

    /// <summary>Encodes a position into an opaque token, or <see langword="null"/> for <see cref="CursorPosition.Start"/>.</summary>
    public static string? Encode(CursorPosition position)
        => position.Sequence <= 0 ? null : EncodeToken(Prefix + position.Sequence.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Decodes an opaque token back into a position. Returns <see langword="null"/> for an absent/empty token
    /// (first exchange) or an unrecognised token (a cursor issued by a different implementation/version).
    /// </summary>
    public static CursorPosition? TryDecode(string? token)
    {
        var text = DecodeToken(token);
        if (text is null || !text.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var number = text.AsSpan(Prefix.Length);
        if (number.IsEmpty)
        {
            return null;
        }

        return long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence)
            ? new CursorPosition(sequence)
            : null;
    }

    private static string EncodeToken(string text)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(text))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string? DecodeToken(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var base64 = token.Replace('-', '+').Replace('_', '/');
        base64 = (base64.Length % 4) switch
        {
            2 => base64 + "==",
            3 => base64 + "=",
            _ => base64,
        };

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
