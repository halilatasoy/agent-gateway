using AgentGatewayConsole.Web.Services;
using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Logs.V1;

namespace AgentGatewayConsole.Web.Endpoints;

/// <summary>
/// OTLP/HTTP log alıcısı. agentgateway yalnızca protobuf gönderir (JSON yok), varsayılan yol /v1/logs.
/// </summary>
public static class OtlpEndpoints
{
    public const string LogsPath = "/v1/logs";
    private const string ProtobufContentType = "application/x-protobuf";

    public static IEndpointRouteBuilder MapOtlpEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(LogsPath, ReceiveLogsAsync).DisableAntiforgery();
        return app;
    }

    internal static async Task<IResult> ReceiveLogsAsync(HttpContext context, IOtlpIngestService ingest, LogService log)
    {
        var contentType = context.Request.ContentType ?? string.Empty;
        if (!contentType.StartsWith(ProtobufContentType, StringComparison.OrdinalIgnoreCase))
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);

        ExportLogsServiceRequest request;
        try
        {
            // Kestrel senkron gövde okumasını engeller: önce belleğe kopyala, sonra ayrıştır.
            using var buffer = new MemoryStream();
            await context.Request.Body.CopyToAsync(buffer, context.RequestAborted);
            buffer.Position = 0;
            request = ExportLogsServiceRequest.Parser.ParseFrom(buffer);
        }
        catch (InvalidProtocolBufferException ex)
        {
            log.LogWarning($"Geçersiz OTLP gövdesi: {ex.Message}", nameof(OtlpEndpoints));
            return Results.BadRequest();
        }

        try
        {
            await ingest.IngestAsync(request, context.RequestAborted);
        }
        catch (Exception ex)
        {
            // 503: OTLP istemcisi yeniden deneyebilir (ör. veritabanı geçici olarak erişilemez).
            log.LogError("OTLP kayıtları yazılamadı", ex, nameof(OtlpEndpoints));
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Bytes(new ExportLogsServiceResponse().ToByteArray(), ProtobufContentType);
    }
}
