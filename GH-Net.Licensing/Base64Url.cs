namespace GH_Net.Licensing;

/// <summary>RFC 4648 URL-safe Base64 without padding.</summary>
public static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> data)
        => Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    public static byte[] Decode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(
            normalized.Length + (4 - normalized.Length % 4) % 4,
            '=');
        return Convert.FromBase64String(normalized);
    }
}
