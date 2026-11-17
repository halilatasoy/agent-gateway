# Agent Gateway İzleme Paneli (agentgateway-console)

agentgateway'in OTLP erişim loglarını toplayan ve şunları gösteren ASP.NET Core 8 MVC uygulaması:

- **Koruma Özeti:** istek, token, MCP araç çağrısı, engellenen/maskelenen sayıları ve grafikler (15 sn'de bir yenilenir)
- **Yakalanan Hassas Veriler:** guardrail'in engellediği veya maskelediği istekler, bulunan veri türü ve **istek akışı** (kullanıcı istemi → araç çağrıları → araç sonuçları; verinin bulunduğu adım işaretli)
- **Trafik Kaydı:** tüm istekler; filtreler ve sunucu tarafı sayfalama

Görünüm [Hallmark](https://github.com/Nutlope/hallmark) Cobalt temasına dayanır: token'lar `wwwroot/css/tokens.css`'te, yazı tipleri `wwwroot/lib/fonts` altında yerel kopya (OFL), dış CDN'e bağımlılık yok. Grafikler kütüphanesiz SVG ile çizilir.

## İçerik ve gizlilik

- **Temiz isteklerin içeriği saklanmaz**; gateway onu panele hiç göndermez.
- **Korunan isteklerde** (Reject/Mask) gateway istek gövdesini gönderir (`agwc.request.body`). `ContentDetailBuilder` gövdeyi kaydetmeden önce:
  1. Sistem mesajının içeriğini ve araç tanımlarını atar (sistem mesajı yalnızca taranır; bulgunun ±60 karakterlik maskelenmiş bağlamı tutulur).
  2. Her mesajı ve araç argümanını `SensitiveDataRedactor` ile maskeler: `Redaction:Rules` (TC, IBAN, kart, TR telefon, e-posta) + 9+ haneli sayılar için güvenlik ağı.
  3. Son kullanıcı istemi ve sonrasını, ayrıca verinin bulunduğu mesajları tutar; mesajları kısaltır (`Capture:MaxMessages`, `Capture:MaxMessageChars`).
- Sonuç ayrı tabloya (`GatewayRequestContents`) maskelenmiş olarak yazılır. Maskeleme tamamlanamazsa içerik hiç yazılmaz.
- `agwc.*` ham nitelikleri hiçbir koşulda `AttributesJson`'a girmez. Diğer içerik nitelikleri `Ingest:ExcludedAttributePrefixes` ile atılır.
- Kapatmak için: chart'ta `capture.enabled=false`.

## Mimari

```
agentgateway ──OTLP/HTTP protobuf──> :4318 /v1/logs ──> OtlpLogMapper ──> PostgreSQL (GatewayRequestLogs)
tarayıcı ──> :8080 (Ingress) ──> Dashboard / ProtectedData / Traffic ──> repository sorguları
```

- **Port izolasyonu:** `/v1/logs` yalnızca 4318'den, arayüz yalnızca 8080'den erişilebilir (`Settings:EnforcePortIsolation`).
- **Ayarlar:**
  - Connection string: env var `ConnectionStrings__Default`
  - Dil ve portlar: `appsettings.json`
  - Saklama süresi (`Retention:Days`) ve yenileme aralığı: DB `AppSettings` tablosu, restart gerektirmez
- **Saklama:** `RetentionCleanupService` saatte bir, süresi dolan kayıtları siler.

## Geliştirme

```powershell
dotnet test console\AgentGatewayConsole.Tests              # xUnit + Moq + InMemory
dotnet tool restore                                         # (console\ içinde) yerel dotnet-ef 8.0.11
dotnet ef migrations add <Ad> -o Data/Db/Migrations         # (console\AgentGatewayConsole.Web içinde)
```

Proto tipleri `Protos/` altındaki opentelemetry-proto v1.11.1 dosyalarından derleme sırasında üretilir.

## İmaj ve kurulum

```powershell
# Yerel (Rancher Desktop): imaj doğrudan k3s'in containerd'sine
nerdctl --namespace k8s.io build -t agentgateway-console:0.3.0 console
helm upgrade -i agentgateway-console k8s\charts\agentgateway-console -n agentgateway-system
```

Chart şunları kurar: PostgreSQL (StatefulSet), uygulama, Ingress (`console.127.0.0.1.sslip.io`) ve agentgateway'in loglarını panele yönlendiren `AgentgatewayPolicy`.

**Var olan bir kurulumu yükseltirken** iki adım izleyin. Aksi halde log ayarı yeni pod'dan önce uygulanır ve eski sürüm `agwc.*` alanlarını tanımadığı için ham gövdeyi yazabilir:

```powershell
helm upgrade agentgateway-console k8s\charts\agentgateway-console -n agentgateway-system --set capture.enabled=false --wait
helm upgrade agentgateway-console k8s\charts\agentgateway-console -n agentgateway-system --reset-values --wait
```

İkinci adımda `--reset-values` gerekli: Helm, değer verilmeden yapılan `upgrade`'de önceki sürümün `capture.enabled=false` değerini korur.
