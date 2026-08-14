using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForgeDeck.Core;

public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = Create();

    public static JsonSerializerOptions Create(Action<JsonSerializerOptions>? configure = null)
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true,
        };
        opts.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        configure?.Invoke(opts);
        return opts;
    }
}
