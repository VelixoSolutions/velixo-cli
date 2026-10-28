namespace Velixo.Cli.Auth;

/// <summary>Base64url without padding (RFC 4648 §5), as OAuth and JWTs use it.</summary>
public static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Decode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var base64 = text.Replace('-', '+').Replace('_', '/');
        base64 = (base64.Length % 4) switch
        {
            2 => base64 + "==",
            3 => base64 + "=",
            _ => base64,
        };

        return Convert.FromBase64String(base64);
    }
}
