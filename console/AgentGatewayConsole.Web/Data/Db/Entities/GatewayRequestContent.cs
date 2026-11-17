namespace AgentGatewayConsole.Web.Data.Db.Entities;

/// <summary>
/// Guardrail'in engellediği veya maskelediği bir isteğin **maskelenmiş** içerik ayrıntısı
/// (konuşma akışı, araç çağrıları, bulunan veri türleri). <see cref="GatewayRequestLog"/> ile 1:1.
///
/// Ana log tablosundan ayrı tutulur: liste, trafik ve saklama sorguları bu büyük JSON'u yüklemez.
/// Ham değer asla buraya yazılmaz; <c>ContentDetailBuilder</c> kaydetmeden önce maskeler.
/// Log kaydı silinince veritabanı tarafında cascade ile silinir.
/// </summary>
public class GatewayRequestContent
{
    public long Id { get; set; }

    public long GatewayRequestLogId { get; set; }
    public GatewayRequestLog? GatewayRequestLog { get; set; }

    /// <summary>Maskelenmiş içerik ayrıntısı (<c>RequestContentDetail</c>), jsonb.</summary>
    public string DetailJson { get; set; } = "{}";
}
