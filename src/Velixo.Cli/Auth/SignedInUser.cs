using System.Text.Json.Serialization;

namespace Velixo.Cli.Auth;

public sealed class SignedInUser
{
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    /// <summary>Gets or sets the token's subject.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}
