using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentGatewayConsole.Web.Data.Db.Entities;
using AgentGatewayConsole.Web.Models;
using Microsoft.Extensions.Options;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;

namespace AgentGatewayConsole.Web.Services;

/// <summary>
/// agentgateway'in OTLP erişim logu kayıtlarını <see cref="GatewayRequestLog"/>'a dönüştürür.
/// Saf dönüşüm: veritabanı veya HTTP bağımlılığı yok.
///
/// agentgateway v1.7.0 notları (kaynak koddan):
///  - Tüm alanlar LogRecord niteliği olarak gelir; gövde boştur.
///  - HTTP alanları OTLP'de semconv adlarıyla gelir (http.status → http.response.status_code vb.);
///    eski adlar da yedek olarak okunur.
///  - TimeUnixNano 0 gelir; zaman için ObservedTimeUnixNano kullanılır.
///  - agw.ai.guardrails bir string'dir (JSON benzeri metin), dizi değil.
///  - agwc.* nitelikleri (içerik yakalama) ham gelir; asla AttributesJson'a yazılmaz, yalnızca
///    ContentDetailBuilder'dan maskelenerek geçip ayrı tabloya gider.
/// </summary>
public class OtlpLogMapper
{
    // Veritabanında okunabilir kalsın diye <, > ve Türkçe karakterler kaçışlanmaz; bu JSON HTML'e gömülmez,
    // arayüze controller üzerinden yeniden serileştirilerek gider.
    private static readonly JsonSerializerOptions ContentJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly Regex ActionRegex = new("\"action\"\\s*:\\s*\"(?<a>[A-Za-z]+)\"", RegexOptions.Compiled);
    private static readonly Regex PhaseActionRegex = new(
        "\"phase\"\\s*:\\s*\"(?<p>[A-Za-z]+)\"[^}]*?\"action\"\\s*:\\s*\"(?<a>[A-Za-z]+)\"", RegexOptions.Compiled);
    private static readonly Regex DurationPartRegex = new(
        @"(?<v>\d+(?:\.\d+)?)\s*(?<u>ns|µs|us|ms|s|m|h)", RegexOptions.Compiled);

    private readonly IReadOnlyList<string> _excludedPrefixes;
    private readonly ContentDetailBuilder _contentBuilder;

    public OtlpLogMapper(IOptions<IngestOptions> options, ContentDetailBuilder contentBuilder)
    {
        _excludedPrefixes = options.Value.ExcludedAttributePrefixes;
        _contentBuilder = contentBuilder;
    }

    public IEnumerable<GatewayRequestLog> Map(ExportLogsServiceRequest request, CaptureLimits? limits = null)
    {
        foreach (var resourceLogs in request.ResourceLogs)
        {
            var resourceAttrs = ToDictionary(resourceLogs.Resource?.Attributes);
            foreach (var scopeLogs in resourceLogs.ScopeLogs)
                foreach (var record in scopeLogs.LogRecords)
                    yield return Map(record, resourceAttrs, limits);
        }
    }

    public GatewayRequestLog Map(LogRecord record, IReadOnlyDictionary<string, object?>? resourceAttrs = null, CaptureLimits? limits = null)
    {
        var attrs = ToDictionary(record.Attributes);
        var (outcome, phase) = ParseGuardrails(GetString(attrs, "agw.ai.guardrails"));

        var log = new GatewayRequestLog
        {
            OccurredAtUtc = ResolveTimestamp(record),
            Protocol = Truncate(GetString(attrs, "protocol"), 16) ?? "http",
            HttpMethod = Truncate(GetString(attrs, "http.request.method", "http.method"), 16),
            Path = Truncate(GetString(attrs, "url.path", "http.path"), 1024),
            StatusCode = (int?)GetLong(attrs, "http.response.status_code", "http.status"),
            DurationMs = ParseDurationMs(GetString(attrs, "duration")),
            ClientAddress = Truncate(GetString(attrs, "client.address", "src.addr"), 64),
            Gateway = Truncate(GetString(attrs, "gateway") ?? GetString(resourceAttrs, "service.name"), 256),
            Route = Truncate(GetString(attrs, "route"), 256),
            Provider = Truncate(GetString(attrs, "gen_ai.provider.name"), 64),
            RequestModel = Truncate(GetString(attrs, "gen_ai.request.model"), 256),
            ResponseModel = Truncate(GetString(attrs, "gen_ai.response.model"), 256),
            InputTokens = GetLong(attrs, "gen_ai.usage.input_tokens"),
            OutputTokens = GetLong(attrs, "gen_ai.usage.output_tokens"),
            McpMethod = Truncate(GetString(attrs, "mcp.method.name"), 128),
            McpTarget = Truncate(GetString(attrs, "mcp.target"), 256),
            ToolName = Truncate(GetString(attrs, "gen_ai.tool.name"), 256),
            GuardrailOutcome = outcome,
            GuardrailPhase = phase,
            GuardrailsRaw = Truncate(GetString(attrs, "agw.ai.guardrails"), 4000),
            Reason = Truncate(GetString(attrs, "reason"), 128),
            Error = Truncate(GetString(attrs, "error"), 2000),
            AttributesJson = JsonSerializer.Serialize(attrs
                .Where(kv => !IsExcluded(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value))
        };

        var detail = _contentBuilder.Build(
            GetRaw(attrs, ContentDetailBuilder.BodyAttribute),
            GetRaw(attrs, ContentDetailBuilder.ResponseToolCallsAttribute),
            GetRaw(attrs, ContentDetailBuilder.CompletionAttribute),
            limits ?? CaptureLimits.Default);
        if (detail is not null)
        {
            log.Content = new GatewayRequestContent { DetailJson = JsonSerializer.Serialize(detail, ContentJsonOptions) };
            log.DetectedDataTypes = _contentBuilder.DetectedTypes(detail);
        }
        return log;
    }

    /// <summary>
    /// Guardrail metnini ayrıştırır ve en ağır kararı döner (Reject > Mask > Allow).
    /// Önce JSON olarak denenir; agentgateway'in Rust debug biçimi JSON'dan saparsa regex'e düşülür.
    /// </summary>
    public static (GuardrailOutcome Outcome, string? Phase) ParseGuardrails(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (GuardrailOutcome.None, null);

        var entries = new List<(string? Phase, string Action)>();
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty("action", out var a))
                        continue;
                    var p = el.TryGetProperty("phase", out var pe) ? pe.GetString() : null;
                    entries.Add((p, a.GetString() ?? string.Empty));
                }
            }
        }
        catch (JsonException)
        {
            foreach (Match m in PhaseActionRegex.Matches(raw))
                entries.Add((m.Groups["p"].Value, m.Groups["a"].Value));
            if (entries.Count == 0)
                foreach (Match m in ActionRegex.Matches(raw))
                    entries.Add((null, m.Groups["a"].Value));
        }

        var best = (Outcome: GuardrailOutcome.None, Phase: (string?)null);
        foreach (var (p, a) in entries)
        {
            var outcome = a.ToLowerInvariant() switch
            {
                "reject" => GuardrailOutcome.Reject,
                "mask" => GuardrailOutcome.Mask,
                "allow" => GuardrailOutcome.Allow,
                _ => GuardrailOutcome.None
            };
            if (outcome > best.Outcome)
                best = (outcome, p?.ToLowerInvariant());
        }
        return best;
    }

    /// <summary>"2ms", "20786ms", "1.5s", "1m2s", "350µs" gibi süreleri milisaniyeye çevirir.</summary>
    public static int? ParseDurationMs(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var plainMs))
            return (int)Math.Round(plainMs);

        double total = 0;
        var matched = false;
        foreach (Match m in DurationPartRegex.Matches(raw))
        {
            matched = true;
            var v = double.Parse(m.Groups["v"].Value, CultureInfo.InvariantCulture);
            total += m.Groups["u"].Value switch
            {
                "ns" => v / 1_000_000,
                "µs" or "us" => v / 1_000,
                "ms" => v,
                "s" => v * 1_000,
                "m" => v * 60_000,
                "h" => v * 3_600_000,
                _ => 0
            };
        }
        return matched ? (int)Math.Round(total) : null;
    }

    private static DateTime ResolveTimestamp(LogRecord record)
    {
        var nanos = record.TimeUnixNano > 0 ? record.TimeUnixNano : record.ObservedTimeUnixNano;
        return nanos > 0
            ? DateTime.UnixEpoch.AddTicks((long)(nanos / 100))
            : DateTime.UtcNow;
    }

    // agwc.* ayardan bağımsız olarak her zaman dışlanır: ham içerik AttributesJson'a giremez.
    private bool IsExcluded(string key)
        => key.StartsWith(ContentDetailBuilder.AttributePrefix, StringComparison.OrdinalIgnoreCase)
           || _excludedPrefixes.Any(p => key.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>Niteliği olduğu gibi metne çevirir (tırnak kırpmadan); dizi/nesne değerler JSON olur.</summary>
    private static string? GetRaw(IReadOnlyDictionary<string, object?> attrs, string key)
    {
        if (!attrs.TryGetValue(key, out var value) || value is null)
            return null;
        return value as string ?? JsonSerializer.Serialize(value);
    }

    private static Dictionary<string, object?> ToDictionary(IEnumerable<KeyValue>? kvs)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (kvs is null)
            return dict;
        foreach (var kv in kvs)
            dict[kv.Key] = ToClr(kv.Value);
        return dict;
    }

    private static object? ToClr(AnyValue? v) => v?.ValueCase switch
    {
        AnyValue.ValueOneofCase.StringValue => v.StringValue,
        AnyValue.ValueOneofCase.IntValue => v.IntValue,
        AnyValue.ValueOneofCase.DoubleValue => v.DoubleValue,
        AnyValue.ValueOneofCase.BoolValue => v.BoolValue,
        AnyValue.ValueOneofCase.ArrayValue => v.ArrayValue.Values.Select(ToClr).ToList(),
        AnyValue.ValueOneofCase.KvlistValue => v.KvlistValue.Values.ToDictionary(x => x.Key, x => ToClr(x.Value)),
        AnyValue.ValueOneofCase.BytesValue => Convert.ToBase64String(v.BytesValue.ToByteArray()),
        _ => null
    };

    private static string? GetString(IReadOnlyDictionary<string, object?>? attrs, params string[] keys)
    {
        if (attrs is null)
            return null;
        foreach (var key in keys)
        {
            if (!attrs.TryGetValue(key, out var value) || value is null)
                continue;
            var s = value switch
            {
                string str => str,
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => JsonSerializer.Serialize(value)
            };
            // agentgateway bazı değerleri tırnaklı gönderebilir ("...").
            if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
                s = s[1..^1];
            if (!string.IsNullOrWhiteSpace(s))
                return s;
        }
        return null;
    }

    private static long? GetLong(IReadOnlyDictionary<string, object?> attrs, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!attrs.TryGetValue(key, out var value) || value is null)
                continue;
            switch (value)
            {
                case long l: return l;
                case int i: return i;
                case double d: return (long)Math.Round(d);
                case string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                    return parsed;
            }
        }
        return null;
    }

    private static string? Truncate(string? value, int max)
        => value is null || value.Length <= max ? value : value[..max];
}
