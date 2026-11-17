using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentGatewayConsole.Web.Models;

namespace AgentGatewayConsole.Web.Services;

/// <summary>
/// Gateway'in guardrail devreye girdiğinde log'a eklediği ham istek gövdesinden, maskelenmiş bir
/// "istek akışı" çıkarır: kullanıcı istemi, modelin araç çağrıları, araç sonuçları ve hassas verinin
/// hangi mesajda bulunduğu.
///
/// Kurallar:
///  - Sistem mesajının içeriği ve araç tanımları (tools) saklanmaz; sistem mesajı yalnızca taranır.
///  - Her metin önce maskelenir, sonra kısaltılır. Ham değer çıktıya hiçbir yoldan giremez.
///  - Maskeleme zaman aşımına uğrarsa hiçbir içerik saklanmaz (güvenli tarafta kal).
///  - Saklanan mesajlar: son kullanıcı istemi ve sonrası (o anki adım zinciri) + verinin bulunduğu mesajlar.
/// Biçimler: OpenAI chat (messages, tool_calls, tool_call_id) ve Anthropic messages (system, tool_use, tool_result).
/// </summary>
public class ContentDetailBuilder
{
    // Gateway'deki AgentgatewayPolicy (agentgateway-console chart'ı) bu adlarla gönderir.
    public const string AttributePrefix = "agwc.";
    public const string BodyAttribute = "agwc.request.body";
    public const string ResponseToolCallsAttribute = "agwc.response.tool_calls";
    public const string CompletionAttribute = "agwc.response.completion";

    private const int MaxFindings = 100;
    private const string TruncationMarker = "\n…\n";

    private readonly SensitiveDataRedactor _redactor;

    public ContentDetailBuilder(SensitiveDataRedactor redactor)
    {
        _redactor = redactor;
    }

    /// <summary>
    /// Ayrıntıyı oluşturur. Üç girdi de boşsa (guardrail devreye girmedi, yakalama kapalı) null döner.
    /// </summary>
    public RequestContentDetail? Build(string? body, string? responseToolCalls, string? completion, CaptureLimits limits)
    {
        var hasBody = !string.IsNullOrWhiteSpace(body);
        var hasToolCalls = !IsEmptyJsonList(responseToolCalls);
        var hasCompletion = !IsEmptyJsonList(completion);
        if (!hasBody && !hasToolCalls && !hasCompletion)
            return null;

        limits = limits.Normalize();
        var detail = new RequestContentDetail();
        try
        {
            if (hasBody)
                AddBody(body!, detail, limits);
            if (hasToolCalls)
                detail.ResponseToolCalls = ParseResponseToolCalls(responseToolCalls!, detail.Findings, limits);
            if (hasCompletion)
                detail.Completion = ParseCompletion(completion!, detail.Findings, limits);
        }
        catch (RegexMatchTimeoutException)
        {
            return new RequestContentDetail { RedactionFailed = true };
        }

        if (detail.Findings.Count > MaxFindings)
            detail.Findings = detail.Findings.Take(MaxFindings).ToList();
        return detail;
    }

    /// <summary>Bulunan veri türleri, kural sırasına (önem) göre; ör. "TcKimlik,Email".</summary>
    public string? DetectedTypes(RequestContentDetail detail)
    {
        if (detail.Findings.Count == 0)
            return null;
        var order = _redactor.TypeOrder.ToList();
        var types = detail.Findings
            .Select(f => f.Type)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => order.IndexOf(t) is var i && i >= 0 ? i : int.MaxValue);
        var joined = string.Join(',', types);
        return joined.Length <= 256 ? joined : joined[..256];
    }

    private void AddBody(string body, RequestContentDetail detail, CaptureLimits limits)
    {
        List<RawMessage>? raw = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            raw = ReadMessages(doc.RootElement);
        }
        catch (JsonException)
        {
            raw = null;
        }

        if (raw is null)
        {
            // Tanınmayan biçim: mesajlar saklanmaz, yalnızca gövdedeki bulgular raporlanır.
            detail.ParseFailed = true;
            AddHits(detail.Findings, _redactor.Redact(body).Hits, "body", null);
            return;
        }

        var toolNames = raw
            .SelectMany(m => m.ToolCalls)
            .Where(c => !string.IsNullOrEmpty(c.Id))
            .GroupBy(c => c.Id!)
            .ToDictionary(g => g.Key, g => g.First().Name);

        var processed = raw.Select((m, i) => Process(m, i, toolNames, limits)).ToList();

        detail.TotalMessages = processed.Count;
        detail.Messages = SelectMessages(processed, limits.MaxMessages);
        detail.OmittedMessages = detail.TotalMessages - detail.Messages.Count;
        detail.Findings.AddRange(processed.SelectMany(m => m.Findings));
    }

    private ContentMessage Process(RawMessage m, int index, IReadOnlyDictionary<string, string?> toolNames, CaptureLimits limits)
    {
        var role = m.Role;
        var toolName = m.ToolCallId is not null && toolNames.TryGetValue(m.ToolCallId, out var n) ? n : null;
        var redacted = _redactor.Redact(m.Text);

        var message = new ContentMessage
        {
            Index = index,
            Role = role,
            ContentLength = m.Text.Length,
            ToolCallId = m.ToolCallId,
            ToolName = toolName
        };
        AddHits(message.Findings, redacted.Hits, role, role == "tool" ? toolName : null);

        if (role == "system")
        {
            // Sistem istemi uygulamaya aittir ve büyüktür (Copilot'ta ~50 KB); yalnızca taranır, saklanmaz.
            message.ContentOmitted = true;
        }
        else
        {
            (message.Content, message.Truncated) = Truncate(redacted.Text, limits.MaxMessageChars);
        }

        foreach (var call in m.ToolCalls)
        {
            var args = _redactor.Redact(call.Arguments);
            AddHits(message.Findings, args.Hits, "toolCall", call.Name);
            var (text, truncated) = Truncate(args.Text, limits.MaxMessageChars / 2);
            message.ToolCalls.Add(new ContentToolCall { Id = call.Id, Name = call.Name, Arguments = text, Truncated = truncated });
        }
        return message;
    }

    /// <summary>
    /// Son kullanıcı istemi ve sonrasını (o anki zincir), bulgu içeren mesajları ve sistem mesajı
    /// yer tutucularını saklar. Sınır aşılırsa önce bulgulu mesajlar, sonra en yeni mesajlar kalır.
    /// </summary>
    private static List<ContentMessage> SelectMessages(List<ContentMessage> all, int max)
    {
        var lastUser = all.FindLastIndex(m => m.Role == "user");
        if (lastUser < 0)
            lastUser = 0;

        var priority = all.Where(m => m.Findings.Count > 0 || m.Role == "system").ToList();
        var tail = all.Where(m => m.Index >= lastUser && !priority.Contains(m)).ToList();

        if (priority.Count >= max)
            return priority.TakeLast(max).OrderBy(m => m.Index).ToList();

        return priority
            .Concat(tail.TakeLast(max - priority.Count))
            .OrderBy(m => m.Index)
            .ToList();
    }

    private List<ContentToolCall> ParseResponseToolCalls(string json, List<ContentFinding> findings, CaptureLimits limits)
    {
        var result = new List<ContentToolCall>();
        if (!TryParseArray(json, out var calls))
            return result;
        foreach (var raw in calls)
        {
            var call = ReadToolCall(raw);
            var args = _redactor.Redact(call.Arguments);
            AddHits(findings, args.Hits, "responseToolCall", call.Name);
            var (text, truncated) = Truncate(args.Text, limits.MaxMessageChars / 2);
            result.Add(new ContentToolCall { Id = call.Id, Name = call.Name, Arguments = text, Truncated = truncated });
        }
        return result;
    }

    private List<string> ParseCompletion(string json, List<ContentFinding> findings, CaptureLimits limits)
    {
        var result = new List<string>();
        if (!TryParseArray(json, out var items))
            return result;
        foreach (var item in items)
        {
            var redacted = _redactor.Redact(TextOf(item));
            AddHits(findings, redacted.Hits, "completion", null);
            result.Add(Truncate(redacted.Text, limits.MaxMessageChars).Text);
        }
        return result;
    }

    // ---- Biçim okuma ----------------------------------------------------------------------------

    private sealed record RawToolCall(string? Id, string? Name, string Arguments);

    private sealed record RawMessage(string Role, string Text, List<RawToolCall> ToolCalls, string? ToolCallId);

    /// <summary>OpenAI veya Anthropic istek gövdesinden mesajları okur; tanınmazsa null.</summary>
    private static List<RawMessage>? ReadMessages(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("messages", out var messages)
            || messages.ValueKind != JsonValueKind.Array)
            return null;

        var result = new List<RawMessage>();

        // Anthropic: sistem istemi üst düzeyde
        if (root.TryGetProperty("system", out var system) && system.ValueKind is JsonValueKind.String or JsonValueKind.Array)
            result.Add(new RawMessage("system", TextOf(system), new(), null));

        foreach (var m in messages.EnumerateArray())
        {
            if (m.ValueKind != JsonValueKind.Object)
                continue;
            var role = m.TryGetProperty("role", out var r) && r.ValueKind == JsonValueKind.String
                ? r.GetString()!.ToLowerInvariant()
                : "user";
            if (role == "developer")
                role = "system";

            var toolCalls = new List<RawToolCall>();
            string? toolCallId = m.TryGetProperty("tool_call_id", out var tcid) && tcid.ValueKind == JsonValueKind.String
                ? tcid.GetString()
                : null;

            // OpenAI: assistant.tool_calls[]
            if (m.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array)
                toolCalls.AddRange(calls.EnumerateArray().Select(ReadToolCall));

            var text = new StringBuilder();
            if (m.TryGetProperty("content", out var content))
            {
                if (content.ValueKind == JsonValueKind.Array)
                {
                    foreach (var part in content.EnumerateArray())
                    {
                        var type = part.ValueKind == JsonValueKind.Object && part.TryGetProperty("type", out var t) ? t.GetString() : null;
                        switch (type)
                        {
                            // Anthropic: model bir araç çağırıyor
                            case "tool_use":
                                toolCalls.Add(new RawToolCall(
                                    part.TryGetProperty("id", out var id) ? id.GetString() : null,
                                    part.TryGetProperty("name", out var name) ? name.GetString() : null,
                                    part.TryGetProperty("input", out var input) ? input.GetRawText() : string.Empty));
                                break;
                            // Anthropic: araç sonucu kullanıcı mesajı içinde gelir
                            case "tool_result":
                                role = "tool";
                                toolCallId ??= part.TryGetProperty("tool_use_id", out var tuid) ? tuid.GetString() : null;
                                if (part.TryGetProperty("content", out var resultContent))
                                    AppendLine(text, TextOf(resultContent));
                                break;
                            default:
                                AppendLine(text, TextOf(part));
                                break;
                        }
                    }
                }
                else
                {
                    text.Append(TextOf(content));
                }
            }

            result.Add(new RawMessage(role, text.ToString(), toolCalls, toolCallId));
        }
        return result;
    }

    private static RawToolCall ReadToolCall(JsonElement call)
    {
        if (call.ValueKind != JsonValueKind.Object)
            return new RawToolCall(null, null, call.ToString());
        var id = call.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
        // OpenAI: { function: { name, arguments } } — gateway'in llm.toolCalls'ı: { name, arguments }
        var fn = call.TryGetProperty("function", out var f) && f.ValueKind == JsonValueKind.Object ? f : call;
        var name = fn.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
        var args = fn.TryGetProperty("arguments", out var a)
            ? a.ValueKind == JsonValueKind.String ? a.GetString() ?? string.Empty : a.GetRawText()
            : string.Empty;
        return new RawToolCall(id, name, args);
    }

    /// <summary>Metin, metin parçaları dizisi veya {type:"text", text} nesnesinden düz metin üretir.</summary>
    private static string TextOf(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String:
                return e.GetString() ?? string.Empty;
            case JsonValueKind.Array:
                var sb = new StringBuilder();
                foreach (var part in e.EnumerateArray())
                    AppendLine(sb, TextOf(part));
                return sb.ToString();
            case JsonValueKind.Object:
                if (e.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    return text.GetString() ?? string.Empty;
                if (e.TryGetProperty("type", out var type) && type.GetString() is { } t && t.Contains("image", StringComparison.OrdinalIgnoreCase))
                    return "[image]";
                return e.GetRawText();
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return string.Empty;
            default:
                return e.GetRawText();
        }
    }

    // ---- Yardımcılar ----------------------------------------------------------------------------

    private static void AddHits(List<ContentFinding> target, IEnumerable<RedactionHit> hits, string source, string? toolName)
    {
        foreach (var h in hits)
            target.Add(new ContentFinding { Type = h.Type, Count = h.Count, Source = source, ToolName = toolName, Snippet = h.Snippet });
    }

    /// <summary>Uzun metnin başını ve sonunu tutar (istem genelde sonda, bağlam başta olur).</summary>
    private static (string Text, bool Truncated) Truncate(string text, int max)
    {
        if (text.Length <= max)
            return (text, false);
        var head = max * 2 / 3;
        var tail = max - head;
        return (text[..head] + TruncationMarker + text[^tail..], true);
    }

    private static void AppendLine(StringBuilder sb, string value)
    {
        if (string.IsNullOrEmpty(value))
            return;
        if (sb.Length > 0)
            sb.Append('\n');
        sb.Append(value);
    }

    private static bool TryParseArray(string json, out List<JsonElement> items)
    {
        items = new List<JsonElement>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return false;
            items = doc.RootElement.EnumerateArray().Select(x => x.Clone()).ToList();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsEmptyJsonList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;
        var trimmed = value.Trim();
        return trimmed == "[]" || trimmed == "null" || trimmed == "\"\"";
    }
}
