using AgentGatewayConsole.Web.Data.Db;
using AgentGatewayConsole.Web.Models;
using AgentGatewayConsole.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgentGatewayConsole.Web.Controllers;

/// <summary>Koruma Özeti ekranı.</summary>
public class DashboardController : Controller
{
    private readonly IDashboardService _dashboard;
    private readonly ISettingsProvider _settings;
    private readonly MessageService _messages;
    private readonly LogService _log;

    public DashboardController(IDashboardService dashboard, ISettingsProvider settings, MessageService messages, LogService log)
    {
        _dashboard = dashboard;
        _settings = settings;
        _messages = messages;
        _log = log;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["RefreshSeconds"] = await _settings.GetIntAsync(SettingKeys.DashboardRefreshSeconds, 15, ct);
        return View();
    }

    /// <summary>KPI kartları, zaman serisi ve dağılımlar tek istekte.</summary>
    [HttpGet]
    public async Task<IActionResult> GetData(DashboardRange range = DashboardRange.Last24Hours, CancellationToken ct = default)
    {
        try
        {
            var summary = await _dashboard.GetSummaryAsync(range, ct);
            var timeline = await _dashboard.GetTimelineAsync(range, ct);
            var models = await _dashboard.GetModelUsageAsync(range, ct);
            var tools = await _dashboard.GetToolUsageAsync(range, ct);
            return Json(new { success = true, data = new { summary, timeline, models, tools } });
        }
        catch (Exception ex)
        {
            _log.LogError("Özet verisi alınamadı", ex, nameof(DashboardController));
            return Json(new { success = false, message = _messages.GetError("UnexpectedError") });
        }
    }
}
