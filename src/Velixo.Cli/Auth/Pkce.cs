using System.Security.Cryptography;
using System.Text;

namespace Velixo.Cli.Auth;

/// <summary>RFC 7636 proof key for code exchange, with the S256 method the gateway requires.</summary>
public static class Pkce
{
    public const string S256Method = "S256";

    /// <summary>A new verifier: 32 random bytes in base64url, 43 characters.</summary>
    public static string CreateVerifier() => Base64Url.Encode(RandomNumberGenerator.GetBytes(32));

    /// <summary>The S256 challenge of <paramref name="verifier"/>: base64url of its SHA-256.</summary>
    public static string ComputeChallenge(string verifier)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        return Base64Url.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    }

    /// <summary>A random value for the OAuth <c>state</c> parameter.</summary>
    public static string CreateState() => Base64Url.Encode(RandomNumberGenerator.GetBytes(24));
}
