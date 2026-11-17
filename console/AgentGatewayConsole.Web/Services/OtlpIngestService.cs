using AgentGatewayConsole.Web.Data.Db;
using AgentGatewayConsole.Web.Data.Db.Repositories;
using AgentGatewayConsole.Web.Models;
using OpenTelemetry.Proto.Collector.Logs.V1;

namespace AgentGatewayConsole.Web.Services;

public interface IOtlpIngestService
{
    Task<int> IngestAsync(ExportLogsServiceRequest request, CancellationToken ct = default);
}

/// <summary>OTLP export isteğindeki tüm kayıtları tek seferde (toplu) yazar.</summary>
public class OtlpIngestService : IOtlpIngestService
{
    private readonly OtlpLogMapper _mapper;
    private readonly IGatewayRequestLogRepository _repository;
    private readonly ISettingsProvider _settings;

    public OtlpIngestService(OtlpLogMapper mapper, IGatewayRequestLogRepository repository, ISettingsProvider settings)
    {
        _mapper = mapper;
        _repository = repository;
        _settings = settings;
    }

    public async Task<int> IngestAsync(ExportLogsServiceRequest request, CancellationToken ct = default)
    {
        // İçerik sınırları DB'den (60 sn önbellekli); restart gerektirmeden değiştirilebilir.
        var limits = new CaptureLimits(
            await _settings.GetIntAsync(SettingKeys.CaptureMaxMessages, CaptureLimits.Default.MaxMessages, ct),
            await _settings.GetIntAsync(SettingKeys.CaptureMaxMessageChars, CaptureLimits.Default.MaxMessageChars, ct));

        var logs = _mapper.Map(request, limits).ToList();
        if (logs.Count == 0)
            return 0;
        // İçerik (Content) navigation üzerinden aynı SaveChanges'te eklenir.
        await _repository.AddRangeAsync(logs);
        await _repository.SaveChangesAsync();
        return logs.Count;
    }
}
