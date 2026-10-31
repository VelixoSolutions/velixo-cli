using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Velixo.Cli.Output;

/// <summary>
/// Writes a <see cref="JsonNode"/> without the reflection-based serializer, so the output path stays
/// trim- and AOT-safe. Non-ASCII text is written as itself rather than escaped, for people reading it.
/// </summary>
public static class JsonText
{
    public static string Indented(JsonNode? node) => Write(node, indented: true);

    public static string Compact(JsonNode? node) => Write(node, indented: false);

    private static string Write(JsonNode? node, bool indented)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(
            buffer,
            new JsonWriterOptions { Indented = indented, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            if (node is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                node.WriteTo(writer);
            }
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
