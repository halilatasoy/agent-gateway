using AgentGatewayConsole.Web.Data.Db;
using AgentGatewayConsole.Web.Endpoints;
using AgentGatewayConsole.Web.Models;
using AgentGatewayConsole.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AgentGatewayConsole.Web.Extensions;

public static class ApplicationBuilderExtensions
{
    /// <summary>Güvenlik başlıkları — tek yerde, bir kez eklenir.</summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
        => app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            await next();
        });

    /// <summary>
    /// OTLP portu yalnızca /v1/logs'a, UI portu yalnızca arayüze hizmet eder.
    /// Ingress UI portunu dışarı açar; bu ayrım dışarıdan sahte log gönderimini engeller.
    /// </summary>
    public static IApplicationBuilder UsePortIsolation(this IApplicationBuilder app)
        => app.Use(async (context, next) =>
        {
            var settings = context.RequestServices.GetRequiredService<IOptions<ConsoleSettings>>().Value;
            if (settings.EnforcePortIsolation)
            {
                var isOtlpPort = context.Connection.LocalPort == settings.OtlpPort;
                var isOtlpPath = context.Request.Path.StartsWithSegments(OtlpEndpoints.LogsPath);
                if (isOtlpPort != isOtlpPath)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
            }
            await next();
        });

    /// <summary>
    /// Açılışta migration uygular. PostgreSQL pod'u uygulamadan geç hazır olabileceği için yeniden dener.
    /// İlişkisel olmayan sağlayıcıda (InMemory testleri) şemayı oluşturur.
    /// </summary>
    public static async Task MigrateDatabaseAsync(this WebApplication app, CancellationToken ct = default)
    {
        var log = app.Services.GetRequiredService<LogService>();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var scope = app.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ConsoleDbContext>();
                if (db.Database.IsRelational())
                    await db.Database.MigrateAsync(ct);
                else
                    await db.Database.EnsureCreatedAsync(ct);
                log.LogInformation("Veritabanı hazır", "Startup");
                return;
            }
            catch (Exception ex) when (attempt < 30)
            {
                log.LogError($"Veritabanına bağlanılamadı (deneme {attempt}/30), 2 sn sonra tekrar", ex, "Startup");
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
    }
}
