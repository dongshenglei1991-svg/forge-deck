using System.Text;
using System.Text.Json;

namespace ForgeDeck.Core.Bridge;

public sealed class BridgeDispatcher
{
    private static readonly JsonSerializerOptions Opts = JsonOptions.Create(o => o.WriteIndented = false);
    private readonly Dictionary<string, Func<JsonElement?, Task<object?>>> _handlers = new();

    /// <summary>需要推送给前端的消息（响应走返回值，事件走这里）。</summary>
    public event Action<string>? Outgoing;

    public void Register(string method, Func<JsonElement?, Task<object?>> handler) =>
        _handlers[method] = handler;

    public void Emit(string eventName, object data) =>
        Outgoing?.Invoke(JsonSerializer.Serialize(new { @event = eventName, data }, Opts));

    public async Task<string?> HandleAsync(string json)
    {
        JsonElement? id = null;
        string? method = null;
        JsonElement? parameters = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Error(null, "-32600", "请求必须是 JSON 对象");
            if (root.TryGetProperty("id", out var idEl)) id = idEl.Clone();
            if (root.TryGetProperty("method", out var mEl)) method = mEl.GetString();
            if (root.TryGetProperty("params", out var pEl) && pEl.ValueKind != JsonValueKind.Null)
                parameters = pEl.Clone();
        }
        catch (JsonException)
        {
            return Error(null, "-32700", "请求不是合法 JSON");
        }
        if (string.IsNullOrEmpty(method)) return Error(id, "-32602", "缺少 method");
        if (!_handlers.TryGetValue(method!, out var handler))
            return Error(id, "-32601", $"未知方法：{method}");

        object? result;
        try { result = await handler(parameters); }
        catch (BridgeException ex) { return Error(id, ex.Code, ex.Message); }
        catch (Exception ex) { return Error(id, "internal", ex.Message); }
        if (id == null) return null;

        // 响应封包必须用 Utf8JsonWriter 回写原始 id：匿名对象序列化时 default 的 JsonElement 会抛
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("id");
            id.Value.WriteTo(writer);
            writer.WritePropertyName("result");
            JsonSerializer.Serialize(writer, result, Opts);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string Error(JsonElement? id, string code, string message)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            if (id != null)
            {
                writer.WritePropertyName("id");
                id.Value.WriteTo(writer);
            }
            writer.WritePropertyName("error");
            writer.WriteStartObject();
            writer.WriteString("code", code);
            writer.WriteString("message", message);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
