using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentGatewayConsole.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace AgentGatewayConsole.Web.Controllers;

/// <summary>
/// i18n mesajlarını istemciye sunar (webds deseni).
/// GET /api/messages?cats=Global,DataTable → { language, categories: { ... } }
/// ETag + "private, no-cache": dil değişince içerik hash'i değişir, aynı dilde 304 döner.
/// </summary>
[Route("api/messages")]
[ApiController]
public class MessagesController : ControllerBase
{
    private readonly MessageService _messageService;

    public MessagesController(MessageService messageService)
    {
        _messageService = messageService;
    }

    [HttpGet]
    public IActionResult Get([FromQuery] string? cats)
    {
        var categories = string.IsNullOrWhiteSpace(cats)
            ? Array.Empty<string>()
            : cats.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var payload = new { language = _messageService.CurrentLanguage, categories = _messageService.GetCategories(categories) };
        var json = JsonSerializer.Serialize(payload);
        var etag = $"W/\"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)), 0, 8)}\"";
        const string cacheControl = "private, no-cache";

        if (Request.Headers.IfNoneMatch.ToString() == etag)
        {
            Response.Headers[HeaderNames.CacheControl] = cacheControl;
            return StatusCode(StatusCodes.Status304NotModified);
        }

        Response.Headers[HeaderNames.ETag] = etag;
        Response.Headers[HeaderNames.CacheControl] = cacheControl;
        return Content(json, "application/json");
    }
}
