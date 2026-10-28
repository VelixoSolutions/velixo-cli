using System.Text.Json.Serialization;

namespace Velixo.Cli.Serialization;

public sealed class ClientRegistrationResponse
{
    [JsonPropertyName("client_id")]
    public string? ClientId { get; set; }
}
