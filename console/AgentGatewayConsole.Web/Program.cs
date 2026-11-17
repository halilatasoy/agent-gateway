using AgentGatewayConsole.Web.Endpoints;
using AgentGatewayConsole.Web.Extensions;
using AgentGatewayConsole.Web.Models;
using Serilog;
using Serilog.Events;

// Serilog yapılandırması kodda (webds deseni). Konteynerde yalnızca stdout; loglar Rancher'dan okunur.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "{Timestamp:yyyy-MM-ddTHH:mm:ss.fffZ} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    var settings = builder.Configuration.GetSection("Settings").Get<ConsoleSettings>() ?? new ConsoleSettings();
    builder.WebHost.ConfigureKestrel(kestrel =>
    {
        kestrel.ListenAnyIP(settings.UiPort);    // arayüz (Ingress arkasında)
        kestrel.ListenAnyIP(settings.OtlpPort);  // OTLP/HTTP alıcısı (yalnızca cluster içi)
        kestrel.Limits.MaxRequestBodySize = settings.OtlpMaxBodyBytes;
    });

    builder.Services
        .AddControllerServices()
        .AddInfrastructureServices(builder.Configuration)
        .AddDatabaseServices(builder.Configuration)
        .AddRepositories()
        .AddDomainServices();

    var app = builder.Build();

    await app.MigrateDatabaseAsync();

    app.UseSecurityHeaders();
    app.UsePortIsolation();
    app.UseStaticFiles();
    app.UseRouting();

    app.MapGet("/healthz", () => Results.Ok("ok"));
    app.MapOtlpEndpoints();
    app.MapControllerRoute("default", "{controller=Dashboard}/{action=Index}/{id?}");

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Uygulama başlatılamadı");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>WebApplicationFactory ile entegrasyon testleri için.</summary>
public partial class Program { }
