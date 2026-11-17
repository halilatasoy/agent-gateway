using AgentGatewayConsole.Web.Data.Db;
using AgentGatewayConsole.Web.Data.Db.Repositories;
using AgentGatewayConsole.Web.Models;
using AgentGatewayConsole.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentGatewayConsole.Web.Extensions;

/// <summary>DI kayıtları gruplar hâlinde (webds deseni).</summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddControllerServices(this IServiceCollection services)
    {
        services.AddControllersWithViews(options =>
        {
            options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
        });
        services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");
        return services;
    }

    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ConsoleSettings>(configuration.GetSection("Settings"));
        services.Configure<IngestOptions>(configuration.GetSection("Ingest"));
        services.Configure<RedactionOptions>(configuration.GetSection("Redaction"));
        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<LogService>();
        services.AddSingleton<MessageService>();
        services.AddSingleton<ISettingsProvider, SettingsProvider>();
        return services;
    }

    public static IServiceCollection AddDatabaseServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Connection string yalnızca environment variable'dan gelir (ConnectionStrings__Default); git'e girmez.
        var connectionString = configuration.GetConnectionString("Default");
        services.AddDbContext<ConsoleDbContext>(options =>
        {
            if (!string.IsNullOrWhiteSpace(connectionString))
                options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(5));
        });
        return services;
    }

    public static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<IGatewayRequestLogRepository, GatewayRequestLogRepository>();
        services.AddScoped<IGatewayRequestContentRepository, GatewayRequestContentRepository>();
        services.AddScoped<IAppSettingsRepository, AppSettingsRepository>();
        return services;
    }

    public static IServiceCollection AddDomainServices(this IServiceCollection services)
    {
        services.AddSingleton<SensitiveDataRedactor>();
        services.AddSingleton<ContentDetailBuilder>();
        services.AddSingleton<OtlpLogMapper>();
        services.AddScoped<IOtlpIngestService, OtlpIngestService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddSingleton<RetentionCleanupService>();
        services.AddHostedService(sp => sp.GetRequiredService<RetentionCleanupService>());
        return services;
    }
}
