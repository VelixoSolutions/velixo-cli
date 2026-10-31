namespace Velixo.Cli.Auth;

/// <summary>Where one Velixo environment signs people in, as discovered from its MCP URL.</summary>
public sealed record OAuthServerEndpoints
{
    required public string Issuer { get; init; }

    required public string AuthorizationEndpoint { get; init; }

    required public string TokenEndpoint { get; init; }

    public string? RegistrationEndpoint { get; init; }

    public string? RevocationEndpoint { get; init; }

    /// <summary>Gets the RFC 8707 resource to ask tokens for: the protected-resource metadata's <c>resource</c>.</summary>
    required public string Resource { get; init; }
}
