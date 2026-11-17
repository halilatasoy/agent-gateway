namespace AgentGatewayConsole.Web.Models;

/// <summary>
/// appsettings.json → "Redaction" bölümü: panelin içerik kaydetmeden önce uyguladığı maskeleme kuralları.
/// Boş bırakılırsa <c>SensitiveDataRedactor.DefaultRules</c> kullanılır; maskeleme hiçbir koşulda kapanmaz.
/// Kurallar gateway'in guardrail desenleriyle (agentgateway-stack values → guardrails) uyumlu tutulmalıdır.
/// </summary>
public class RedactionOptions
{
    public List<RedactionRule> Rules { get; set; } = new();
}

public class RedactionRule
{
    /// <summary>Veri türü anahtarı; arayüzde "DataType:{Type}" mesajıyla gösterilir.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>.NET regex deseni.</summary>
    public string Pattern { get; set; } = string.Empty;

    /// <summary>Eşleşmenin yerine yazılacak etiket, ör. &lt;TC_KIMLIK_NO&gt;.</summary>
    public string Placeholder { get; set; } = string.Empty;
}

/// <summary>Yakalanan içerik sınırları; DB AppSettings'ten gelir (restart gerektirmez).</summary>
public sealed record CaptureLimits(int MaxMessages, int MaxMessageChars)
{
    public static readonly CaptureLimits Default = new(30, 2000);

    /// <summary>Değerleri makul aralığa çeker; yanlış girilmiş bir ayar içeriği şişirmesin.</summary>
    public CaptureLimits Normalize() => new(Math.Clamp(MaxMessages, 1, 500), Math.Clamp(MaxMessageChars, 200, 100_000));
}

/// <summary>
/// Bir isteğin maskelenmiş içerik ayrıntısı. <c>GatewayRequestContents.DetailJson</c> içinde saklanır.
/// Ham değer içermez: tüm metinler <c>SensitiveDataRedactor</c>'dan geçtikten sonra buraya girer.
/// </summary>
public class RequestContentDetail
{
    /// <summary>Gövde JSON değil ya da mesaj listesi tanınmadı; mesajlar saklanmadı, yalnızca bulgular var.</summary>
    public bool ParseFailed { get; set; }

    /// <summary>Maskeleme tamamlanamadı (regex zaman aşımı); güvenlik için hiçbir içerik saklanmadı.</summary>
    public bool RedactionFailed { get; set; }

    public int TotalMessages { get; set; }
    public int OmittedMessages { get; set; }
    public List<ContentMessage> Messages { get; set; } = new();

    /// <summary>Modelin bu istekte döndürdüğü araç çağrıları (sonraki adım).</summary>
    public List<ContentToolCall> ResponseToolCalls { get; set; } = new();

    /// <summary>Yanıt aşamasında maskelenen kayıtlarda model yanıtı (maskelenmiş).</summary>
    public List<string> Completion { get; set; } = new();

    /// <summary>Tüm bulgular; her biri bulunduğu yer ve maskelenmiş bağlam parçasıyla.</summary>
    public List<ContentFinding> Findings { get; set; } = new();
}

public class ContentMessage
{
    /// <summary>İstekteki özgün sıra (0'dan başlar).</summary>
    public int Index { get; set; }

    /// <summary>user | assistant | tool | system</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>Maskelenmiş ve gerekirse kısaltılmış içerik; sistem mesajında null.</summary>
    public string? Content { get; set; }

    /// <summary>Kısaltmadan önceki uzunluk (karakter).</summary>
    public int ContentLength { get; set; }

    public bool Truncated { get; set; }

    /// <summary>İçerik bilinçli olarak saklanmadı (sistem mesajı).</summary>
    public bool ContentOmitted { get; set; }

    /// <summary>Araç sonucu mesajında, ait olduğu çağrının kimliği ve aracın adı.</summary>
    public string? ToolCallId { get; set; }
    public string? ToolName { get; set; }

    /// <summary>Model mesajındaki araç çağrıları.</summary>
    public List<ContentToolCall> ToolCalls { get; set; } = new();

    public List<ContentFinding> Findings { get; set; } = new();
}

public class ContentToolCall
{
    public string? Id { get; set; }
    public string? Name { get; set; }

    /// <summary>Maskelenmiş ve gerekirse kısaltılmış argümanlar (JSON metni).</summary>
    public string? Arguments { get; set; }
    public bool Truncated { get; set; }
}

public class ContentFinding
{
    /// <summary>Veri türü: TcKimlik | Iban | CreditCard | Phone | Email | Number</summary>
    public string Type { get; set; } = string.Empty;

    public int Count { get; set; }

    /// <summary>Bulunduğu yer: user | assistant | tool | system | toolCall | responseToolCall | completion | body</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Kaynak bir araçsa (tool, toolCall, responseToolCall) aracın adı.</summary>
    public string? ToolName { get; set; }

    /// <summary>İlk eşleşmenin çevresindeki maskelenmiş metin.</summary>
    public string? Snippet { get; set; }
}
