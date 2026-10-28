using System.Text.Json.Nodes;
using Velixo.Cli.Serialization;

namespace Velixo.Cli.Auth;

/// <summary>Writes a token answer into a profile's credentials, and the credentials into the documents the CLI prints.</summary>
public static class SignInRecords
{
    /// <summary>The gateway's default absolute session lifetime, which no refresh extends.</summary>
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    public static void ApplyTokens(StoredCredentials credentials, TokenResponse tokens, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(tokens);

        credentials.AccessToken = tokens.AccessToken;
        credentials.AccessTokenExpiresAt = now.AddSeconds(tokens.ExpiresIn ?? 3600);
        if (!string.IsNullOrEmpty(tokens.RefreshToken))
        {
            credentials.RefreshToken = tokens.RefreshToken;
        }

        JsonObject? access = JwtClaims.ReadPayload(tokens.AccessToken);
        JsonObject? identity = JwtClaims.ReadPayload(tokens.IdToken);

        var email = JwtClaims.GetString(identity, "email")
            ?? JwtClaims.GetString(access, "preferred_username")
            ?? JwtClaims.GetString(access, "email")
            ?? credentials.User?.Email;
        var subject = JwtClaims.GetString(access, "sub") ?? credentials.User?.Id;
        credentials.User = new SignedInUser { Email = email, Id = subject };

        var organizationId = JwtClaims.GetString(access, "organizationId") ?? credentials.Organization?.Id;
        credentials.Organization = new SignedInOrganization { Id = organizationId, Name = credentials.Organization?.Name };

        credentials.SessionExpiresAt ??= credentials.SignedInAt.Add(SessionLifetime);
    }

    /// <summary>The document <c>velixo login</c> and <c>velixo whoami</c> print.</summary>
    public static JsonObject Describe(StoredCredentials credentials, string profile, string environment)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        return new JsonObject
        {
            ["user"] = new JsonObject
            {
                ["email"] = credentials.User?.Email,
                ["id"] = credentials.User?.Id,
            },
            ["organization"] = new JsonObject
            {
                ["id"] = credentials.Organization?.Id,
                ["name"] = credentials.Organization?.Name,
            },
            ["environment"] = environment,
            ["profile"] = profile,
            ["sessionExpiresAt"] = credentials.SessionExpiresAt,
        };
    }
}
