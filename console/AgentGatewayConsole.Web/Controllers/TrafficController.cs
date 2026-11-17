using AgentGatewayConsole.Web.Data.Db.Entities;
using AgentGatewayConsole.Web.Data.Db.Repositories;
using AgentGatewayConsole.Web.Models;
using AgentGatewayConsole.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgentGatewayConsole.Web.Controllers;

/// <summary>Trafik Kaydı ekranı: tüm istekler, DataTables server-side sayfalama.</summary>
public class TrafficController : Controller
{
    // DataTables sütun indeksi → sıralanabilir alan (beyaz liste)
    private static readonly string?[] OrderableColumns = { "occurredAtUtc", "protocol", null, null, "statusCode", null, "durationMs" };

    private readonly IGatewayRequestLogRepository _repository;
    private readonly MessageService _messages;
    private readonly LogService _log;

    public TrafficController(IGatewayRequestLogRepository repository, MessageService messages, LogService log)
    {
        _repository = repository;
        _messages = messages;
        _log = log;
    }

    public IActionResult Index() => View();

    /// <summary>DataTables server-side protokolü: draw/start/length/search/order + özel filtreler.</summary>
    [HttpGet]
    public async Task<IActionResult> GetPage(int draw, int start, int length, string? protocol, string? statusClass,
        string? model, string? outcome, CancellationToken ct)
    {
        try
        {
            var orderIndex = int.TryParse(Request.Query["order[0][column]"], out var oi) ? oi : 0;
            var query = new TrafficQuery
            {
                Start = start,
                Length = length <= 0 ? 25 : length,
                Search = Request.Query["search[value]"],
                Protocol = protocol,
                StatusClass = statusClass,
                Model = model,
                Outcome = Enum.TryParse<GuardrailOutcome>(outcome, true, out var o) ? o : null,
                OrderBy = orderIndex >= 0 && orderIndex < OrderableColumns.Length ? OrderableColumns[orderIndex] : null,
                Descending = !string.Equals(Request.Query["order[0][dir]"], "asc", StringComparison.OrdinalIgnoreCase)
            };

            var page = await _repository.GetTrafficPageAsync(query, ct);
            var data = page.Items.Select(e => new
            {
                e.Id,
                e.OccurredAtUtc,
                e.Protocol,
                target = e.Protocol == "mcp" ? (e.ToolName ?? e.McpMethod) : (e.RequestModel ?? e.Path),
                e.HttpMethod,
                e.StatusCode,
                outcome = e.GuardrailOutcome.ToString(),
                e.DurationMs,
                tokens = (e.InputTokens ?? 0) + (e.OutputTokens ?? 0),
                e.ClientAddress
            });
            return Json(new { draw, recordsTotal = page.TotalCount, recordsFiltered = page.FilteredCount, data });
        }
        catch (Exception ex)
        {
            _log.LogError("Trafik kaydı listelenemedi", ex, nameof(TrafficController));
            return Json(new { draw, recordsTotal = 0, recordsFiltered = 0, data = Array.Empty<object>(), error = _messages.GetError("UnexpectedError") });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetFilterOptions(CancellationToken ct)
    {
        try
        {
            var models = await _repository.GetDistinctModelsAsync(ct);
            return Json(new { success = true, data = new { models } });
        }
        catch (Exception ex)
        {
            _log.LogError("Filtre seçenekleri alınamadı", ex, nameof(TrafficController));
            return Json(new { success = false, message = _messages.GetError("UnexpectedError") });
        }
    }
}
