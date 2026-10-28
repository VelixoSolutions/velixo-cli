using System.Text.Json.Serialization;

namespace Velixo.Cli.Auth;

public sealed class SignedInOrganization
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Gets or sets the organization's name. The gateway token carries only the id, so this is null until the server sends a name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
