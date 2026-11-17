using AgentGatewayConsole.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgentGatewayConsole.Web.Controllers;

/// <summary>Dil değişimi — sayfa yenilemenin izinli olduğu istisnalardan biri.</summary>
public class LanguageController : Controller
{
    [HttpPost]
    public IActionResult Set(string lang, string? returnUrl)
    {
        if (MessageService.SupportedLanguages.Contains(lang))
        {
            Response.Cookies.Append(MessageService.LanguageCookie, lang, new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                IsEssential = true
            });
        }
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }
}
