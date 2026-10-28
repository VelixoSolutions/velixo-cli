using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Velixo.Cli.Auth;
using Velixo.Cli.Config;
using Velixo.Cli.Output;

namespace Velixo.Cli.Serialization;

/// <summary>
/// Every type the CLI reads or writes as JSON. Source generation keeps the NativeAOT publish free of
/// reflection; a type missing here fails to serialize at run time, not at build time.
/// </summary>
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(CliConfig))]
[JsonSerializable(typeof(ProjectConfig))]
[JsonSerializable(typeof(ProfileState))]
[JsonSerializable(typeof(CredentialsFile))]
[JsonSerializable(typeof(ErrorEnvelope))]
[JsonSerializable(typeof(ProtectedResourceMetadata))]
[JsonSerializable(typeof(AuthorizationServerMetadata))]
[JsonSerializable(typeof(ClientRegistrationRequest))]
[JsonSerializable(typeof(ClientRegistrationResponse))]
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(OAuthErrorResponse))]
[JsonSerializable(typeof(GitHubRelease))]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonArray))]
public sealed partial class CliJsonContext : JsonSerializerContext;
