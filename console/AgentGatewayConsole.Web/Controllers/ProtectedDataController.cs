using System.Text.Json;
using AgentGatewayConsole.Web.Data.Db;
using AgentGatewayConsole.Web.Data.Db.Repositories;
using AgentGatewayConsole.Web.Models;
using AgentGatewayConsole.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgentGatewayConsole.Web.Controllers;

/// <summary>Yakalanan Hassas Veriler ekranı: guardrail'in engellediği veya maskelediği istekler.</summary>
public class ProtectedDataController : Controller
{
    private static readonly JsonSerializerOptions ContentJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IGatewayRequestLogRepository _repository;
    private readonly IGatewayRequestContentRepository _contents;
    private readonly IDashboardService _dashboard;
    private readonly ISettingsProvider _settings;
    private readonly MessageService _messages;
    private readonly LogService _log;

    public ProtectedDataController(IGatewayRequestLogRepository repository, IGatewayRequestContentRepository contents,
        IDashboardService dashboard, ISettingsProvider settings, MessageService messages, LogService log)
    {
        _repository = repository;
        _contents = contents;
        _dashboard = dashboard;
        _settings = settings;
        _messages = messages;
        _log = log;
    }

    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> GetList(DashboardRange range = DashboardRange.Last7Days, CancellationToken ct = default)
    {
        try
        {
            var maxRows = await _settings.GetIntAsync(SettingKeys.ProtectedEventsMaxRows, 1000, ct);
            var events = await _repository.GetProtectedEventsAsync(_dashboard.GetRangeStartUtc(range), maxRows, ct);
            var data = events.Select(e => new
            {
                e.Id,
                e.OccurredAtUtc,
                outcome = e.GuardrailOutcome.ToString(),
                phase = e.GuardrailPhase,
                e.Protocol,
                model = e.RequestModel,
                e.ToolName,
                e.ClientAddress,
                e.StatusCode,
                e.DurationMs,
                dataTypes = SplitTypes(e.DetectedDataTypes)
            });
            return Json(new { success = true, data });
        }
        catch (Exception ex)
        {
            _log.LogError("Yakalanan veriler listelenemedi", ex, nameof(ProtectedDataController));
            return Json(new { success = false, message = _messages.GetError("UnexpectedError"), data = Array.Empty<object>() });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetDetail(long id)
    {
        try
        {
            var e = await _repository.GetByIdAsync(id);
            if (e is null)
                return Json(new { success = false, message = _messages.GetError("RecordNotFound") });

            // İçerik zaten maskelenmiş olarak saklanır; burada yalnızca okunur.
            var content = await _contents.GetByLogIdAsync(id);
            var detail = content is null
                ? null
                : JsonSerializer.Deserialize<RequestContentDetail>(content.DetailJson, ContentJsonOptions);

            return Json(new
            {
                success = true,
                data = new
                {
                    e.Id,
                    e.OccurredAtUtc,
                    outcome = e.GuardrailOutcome.ToString(),
                    phase = e.GuardrailPhase,
                    e.Protocol,
                    e.HttpMethod,
                    e.Path,
                    e.StatusCode,
                    e.DurationMs,
                    e.ClientAddress,
                    e.Gateway,
                    e.Route,
                    e.Provider,
                    e.RequestModel,
                    e.ResponseModel,
                    e.InputTokens,
                    e.OutputTokens,
                    e.McpMethod,
                    e.McpTarget,
                    e.ToolName,
                    e.Reason,
                    e.Error,
                    guardrails = PrettyJson(e.GuardrailsRaw),
                    attributes = PrettyJson(e.AttributesJson),
                    dataTypes = SplitTypes(e.DetectedDataTypes),
                    content = detail
                }
            });
        }
        catch (Exception ex)
        {
            _log.LogError($"Kayıt detayı alınamadı (id={id})", ex, nameof(ProtectedDataController));
            return Json(new { success = false, message = _messages.GetError("UnexpectedError") });
        }
    }

    private static string[] SplitTypes(string? types)
        => string.IsNullOrWhiteSpace(types)
            ? Array.Empty<string>()
            : types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? PrettyJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return raw;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return raw;
        }
    }
}
