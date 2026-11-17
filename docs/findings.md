# Bulgular: agentgateway + GitHub Copilot SDK

**Tarih:** 12.11.2026
**Ortam:** Windows 11, RTX 5060 Ti 16 GB, Ollama 0.40.16
**Sürümler:** agentgateway v1.7.0 (standalone), GitHub.Copilot.SDK 1.0.31 (.NET 8)

## Sonuç

**Evet, Copilot SDK agentgateway ile entegre çalışıyor.** Copilot'un hem model trafiği hem MCP araç trafiği gateway'den geçebiliyor. Bu modda GitHub hesabı veya Copilot aboneliği gerekmiyor. Her istek gateway logunda sağlayıcı, model, token, MCP hedefi ve araç adıyla görünüyor; bu da yönetişim, kota ve denetim için gereken temeli sağlıyor.

## Faz 1: agentgateway tek başına

| Test | Sonuç |
|---|---|
| `/v1/models` | ✅ config'teki modeller listelendi |
| `/v1/chat/completions` → Ollama | ✅ log: `gen_ai.provider.name=ollama`, model ve token sayıları |
| MCP `initialize`, `tools/list`, `tools/call echo` | ✅ 13 araç geldi; log: `mcp.target=everything gen_ai.tool.name=echo` |
| UI (`/ui/`) | ✅ 200 |
| Config hot-reload | ✅ yeni model, yeniden başlatmadan aktif oldu |

## Faz 2: Copilot SDK

| Senaryo | İstemci modu | Model | Sonuç | Süre |
|---|---|---|---|---|
| llm (model gateway'den) | Empty | qwen3:14b | ✅ PASS | 5–24 sn |
| mcp (MCP gateway'den) | Empty | qwen3:14b | ✅ PASS | 9–12 sn |
| full (ikisi birden) | Empty | qwen3:14b | ✅ PASS | 6–18 sn |
| full | CopilotCli | qwen3:14b (4k bağlam) | ❌ zaman aşımı: model `echo` aracını sonsuz döngüde çağırdı (590 sn'de 39 istek) | — |
| full | CopilotCli | qwen3-14b-24k (24k bağlam) | ✅ PASS | 36 sn |

### Entegrasyon şekli (.NET)

```csharp
new SessionConfig
{
    Model = "qwen3:14b",
    Provider = new ProviderConfig { Type = "openai", BaseUrl = "http://<gateway>/v1", WireApi = "completions" },
    McpServers = new Dictionary<string, McpServerConfig>
    {
        ["gw"] = new McpHttpServerConfig { Url = "http://<gateway>/mcp", Tools = ["*"] },
    },
};
```

- Copilot, gateway'in MCP araçlarını `<sunucu-anahtarı>-<araç>` biçiminde adlandırıyor (`gw-echo`). İzin listesinde `mcp:gw-echo` olarak geçiyor.
- agentgateway, birden çok MCP hedefini tek sanal sunucu olarak sunuyor. Copilot tarafında **tek bir** MCP girdisi yetiyor; hangi hedeflerin açık olacağı merkezden, gateway'de yönetiliyor.

## Önemli dersler

1. **Bağlam penceresi kritik.** CopilotCli (tam ajan) modunda tek bir istek yaklaşık **12.8k input token** tutuyor: sistem promptu ve yerleşik araç tanımları. Ollama'nın varsayılan 4096 bağlamı promptu sessizce kırpıyor; her istek tam 2050 token'da kalıyor. Model araç sonucunu göremediği için aynı aracı sonsuza kadar çağırıyor. Hata mesajı da üretmiyor. **Kural: Copilot için kullanılacak her modelde bağlam ≥16k olmalı.** Bu Rancher'daki vLLM/Ollama dağıtımları için de geçerli.
2. **Kaçak döngüyü gateway yakalayabilir.** Yukarıdaki döngü 590 sn'de 39 LLM isteği üretti. Prod'da agentgateway'in rate limit ve token bütçesi (`Budget`) politikaları bu tür döngüleri sınırlamak için kullanılmalı.
3. **İzin yönetimi.** SDK'nın `PermissionHandler.ApproveAll` seçeneği, CopilotCli modunda shell ve dosya yazma araçlarını da onaylıyor. Probe yalnızca `gw` MCP sunucusunun araçlarını onaylıyor. Gerçek entegrasyonda da araçlar beyaz listeyle sınırlandırılmalı.
4. **Empty mod daha öngörülebilir.** Uygulama içine gömülü ajanlar için `CopilotClientMode.Empty` ile açık bir araç listesi vermek, prompt boyutunu (~1.8k token) ve davranışı kontrol altında tutuyor.

## Güvenlik notları (yerel kurulum)

- Kimlik doğrulama yok. Gateway yalnızca `127.0.0.1:4000`'e bağlı, ancak `15020` (stats) ve `15021` (readiness) tüm arayüzleri dinliyor.
- `config.session.key`, gateway tarafından config dosyasına otomatik yazıldı. Bu gizli bir değerdir. Kalıcı kurulumda `SESSION_KEY` ortam değişkeni veya Kubernetes Secret kullanılmalı.

## Test edilmeyenler

- **Copilot sağlayıcı olarak** (agentgateway → GitHub Copilot, `GH_COPILOT_TOKEN`): Copilot aboneliği gerektiriyor. Bu kullanımın GitHub kullanım şartlarıyla uyumu ayrıca kontrol edilmeli.
- **Copilot CLI'ın kendisi** (`npm i -g @github/copilot` ve `~/.copilot/mcp-config.json`): Global kurulum gerektirdiği için yapılmadı. Mekanizma SDK ile aynı; SDK zaten CLI runtime'ını içinde çalıştırıyor.

## Faz 3: Kubernetes / Helm (15.11.2026)

**Ortam:** Rancher Desktop 1.24.0 (kullanıcıya özel kurulum), k3s v1.36.6, containerd

| Test | Sonuç |
|---|---|
| 4 adımlı kurulum (`k8s/install.ps1`) | ✅ Gateway `Programmed`, modeller, MCP backend, HTTPRoute ve politika `Accepted` |
| CRD CEL kuralları (cluster tarafı) | ✅ tüm kaynaklar kabul edildi |
| Cluster içi pod → host Ollama (`host.rancher-desktop.internal`) | ✅ Ollama'yı ağa açmadan erişildi |
| `/v1/models`, `/v1/chat/completions` (`localhost:8080`, port-forward gerekmedi) | ✅ |
| Probe llm / mcp / full (Empty mod) | ✅ PASS (8 / 7 / 12 sn) |
| Probe full, CopilotCli modu, `qwen3-14b-24k` | ✅ PASS (30 sn, ~14.5k token prompt) |
| Hız sınırı (5/dk'ya düşürülüp 8 istek) | ✅ 5 × `200`, sonra 3 × `429` |

**Hazır olanlar:**

- `k8s/charts/agentgateway-stack` chart'ı yazıldı. İçeriği: Gateway, AgentgatewayParameters, AgentgatewayModel'lar, MCP için AgentgatewayBackend ve HTTPRoute, demo MCP sunucusu, rate-limit politikası ve Türkçe Rancher formu (`questions.yaml`).
- Doğrulama sonuçları:
  - `helm lint --strict` temiz.
  - Üretilen 7 özel kaynağın 7'si upstream v1.7.0 CRD'leri ve Gateway API v1.6.3 şemalarına göre yapısal olarak geçerli (ajv ile, çevrimdışı).
  - Kenar durumları (bulut sağlayıcı anahtarı, harici SSE hedefi, hedefsiz MCP, eksik model adı) beklendiği gibi davranıyor.
- `k8s/install.ps1` (4 adımlı kurulum) ve `k8s/setup-local-cluster.ps1` (WSL2, Rancher Desktop ve Kubernetes) betikleri yazıldı.

**Bu fazda çıkan dersler:**

- **k3s/RKE2'de Gateway API CRD'leri önceden kurulu olabilir.** k3s v1.36.6 ile gelen Traefik chart'ı (`traefik-crd`), Gateway API v1.6.3'ü zaten kuruyor. Üzerine `kubectl apply --server-side` ile uygulamak, `ValidatingAdmissionPolicy` nesnelerinde alan sahipliği çakışmasına yol açıyor. `install.ps1` artık mevcut sürümü okuyor; sürüm yeterliyse bu adımı atlıyor, eskiyse duruyor. CRD'lerde `helm.sh/resource-policy: keep` var, yani Traefik kaldırılsa bile Gateway API CRD'leri ve agentgateway kaynakları silinmiyor.
- **Rancher Desktop'ta Kubernetes sürümü açıkça verilmeli.** Sürüm verilmediğinde ilk açılışta v1.25.16 seçildi; chart ≥1.30 istiyor. Betik artık sürümü 1.36.6 olarak sabitliyor.
- `rdctl start --application.admin-access` Windows'ta desteklenmiyor. Bu parametre verilince `rdctl` uygulamayı hiç başlatmadan çıkıyor.
- Windows PowerShell 5.1'de, native komut stderr'e yazdığında `$ErrorActionPreference='Stop'` betiği kesiyor (`NativeCommandError`). Native komutlar `Continue` altında çalıştırılıp başarı `$LASTEXITCODE` ile kontrol ediliyor.
- Rancher Desktop'ın ağ tüneli (`host.rancher-desktop.internal` → 192.168.127.254) host'un loopback'ine ulaşıyor. Yerel testte Ollama'yı `0.0.0.0`'a açmaya gerek kalmadı.

- `AgentgatewayModel` kaynağında `match.model` verilmezse model adı olarak `metadata.name` kullanılıyor. Kubernetes adları `:` içeremediği için `qwen3:14b` gibi Ollama adlarında `match.model` zorunlu.
- CRD kuralları: Ollama sağlayıcısı `baseURL` istiyor. Rate-limit öğelerinin her birinde `requests` veya `tokens` alanlarından yalnızca biri olabiliyor.
- Gateway portu 80 yerine 8080 seçildi, çünkü k3s/RKE2'de 80 portu Traefik veya ingress-nginx'e ait.
- Demo MCP sunucusu `server-everything@2.1.0` sürümüne sabitlendi; bu sürüm Node ≥24 istiyor. En yeni sürüm (2.3.0) 4 günlük olduğu için kullanılmadı.
- Upstream'de CRD gerektirmeyen bir `agentgateway-standalone` chart'ı da yayında. CRD kurma yetkisi olmayan Rancher projeleri için alternatif olabilir.

## Faz 4: Harbor + Rancher Manager (15.11.2026)

**Kurulan:** cert-manager v1.21.3 (yerel CA), Harbor 2.15.4 (chart 1.19.4), Rancher Manager 2.15.3. Hepsi aynı Rancher Desktop cluster'ında; betik `k8s/platform/install-platform.ps1`.

| Test | Sonuç |
|---|---|
| Rancher ve Harbor arayüzleri (`*.127.0.0.1.sslip.io`, yerel CA ile TLS) | ✅ 200 |
| Pod içinden aynı hostname'ler (CoreDNS `rewrite` → Traefik) | ✅ |
| Chart'ların Harbor'a yayını (stack + upstream 2 chart) | ✅ |
| Rancher ClusterRepo (salt-okunur robot hesabı, chart başına 3 repo) | ✅ `OCIDownloaded=True` |
| Rancher, CLI ile kurulan release'leri Installed Apps'te gösteriyor | ✅ |
| Kurulum betiği uçtan uca, tekrar çalıştırılabilir | ✅ |

**Dersler** (şirket ortamı için olanlar `docs/kurumsal-kurulum.md`'de de var):

- **Rancher 2.15 OCI repository'de `caBundle` DER bekliyor** (`x509.ParseCertificate`). Alan açıklamasında PEM yazıyor; PEM verilince `x509: malformed certificate` hatası alınıyor. Kurumsal çözüm: `additionalTrustedCAs`.
- **Rancher OCI etiketlerini semver'e normalleştiriyor.** Upstream `v1.7.0` için `1.7.0` etiketini arıyor. Harbor'da ek etiketle çözüldü; `publish-charts.ps1` bunu otomatik yapıyor.
- **Proje düzeyinde OCI URL'si `/v2/_catalog` istiyor.** Harbor proje robotu bu çağrıda `401` aldı; chart başına repository kullanıldı.
- OCI repository'lerde durum koşulu `Downloaded` değil, `OCIDownloaded`.
- **PowerShell 5.1:**
  - Konsol giriş kodlaması BOM'lu UTF-8 ise hem pipe hem `Process.StandardInput` stdin'e BOM yazıyor. `helm registry login --password-stdin` bu yüzden Harbor'dan `401` aldı. Çözüm: süreç başlarken `[Console]::InputEncoding` geçici olarak BOM'suz UTF-8 yapılıyor.
  - Native argümanlardaki iç çift tırnaklar siliniyor (jsonpath filtreleri, JSON gövdeleri). JSON'lar BOM'suz geçici dosyadan veriliyor.
- **Windows `curl.exe` (Schannel) iptal kontrolü yapıyor.** CRL/OCSP adresi olmayan yerel CA'da `--ssl-no-revoke` gerekiyor. Kurumsal CA'da buna gerek yok.
- **Helm, registry kimlik bilgisini Docker credential helper'ına yazıyor.** Rancher Desktop kuruluyken bu, Windows Kimlik Bilgileri Yöneticisi oluyor. Yayın betiği iş bitince `helm registry logout` yapıyor.
- Kaynak: platform (Rancher + Harbor + cert-manager) ve agentgateway birlikte yaklaşık 6.8 GB RAM kullanıyor.

## Faz 5: Copilot CLI'ın kendisi (15.11.2026)

**Kurulum:** `@github/copilot` 1.0.105, proje içine (`tools/copilot-cli`). Başlatıcı betik: `scripts/copilot-gateway.ps1`. Bağlantı ortam değişkenleriyle kuruluyor: BYOK (`COPILOT_PROVIDER_*`), `COPILOT_OFFLINE=true`, izole `COPILOT_HOME`; MCP ayarı `mcp-config.json` dosyasında.

| Test | Sonuç |
|---|---|
| Tek seferlik istem (`-p`), model gateway üzerinden (cluster → host Ollama) | ✅ |
| MCP aracı (`--allow-tool gw`): keşif, `tools/list`, `tools/call echo` → cluster içi demo MCP pod'u | ✅ "Echo: Copilot CLI MCP testi" (24 sn) |
| GitHub girişi / Copilot aboneliği olmadan (offline mod) | ✅ |

**Dersler:**

- **CLI, statik bağlamı istek göndermeden önce bütçeyle karşılaştırıyor.** `MAX_PROMPT_TOKENS=20000` ve `MAX_OUTPUT_TOKENS=4096` ile `compaction_static_context_blocked` uyarısı alındı ve modele **hiç istek gitmedi**; CLI yalnızca "No response was returned" dedi. Uyarının metni yalnızca `--output-format json` çıktısında görünüyor. 24000/2048 ile çalıştı; gerçek prompt yaklaşık 16k token.
- **SDK'nın getirdiği `copilot.exe` etkileşimli CLI değil**, yalnızca SDK runtime'ı (`copilot-runtime: unsupported argument '--help'`). CLI ayrıca kurulmalı.
- **Küçük yerel model gereksiz araç çağırabiliyor.** `qwen3` modeli "yalnızca şu cümleyi yaz" isteğinde bile `echo` aracını çağırdı. Prod'da daha güçlü bir model ve araç beyaz listesi önerilir.
- **PowerShell başlatıcı betikleri:**
  - `param()` bloğu, iletilen argümanları konumsal olarak betiğin parametrelerine bağlıyor.
  - `[Parameter()]` kullanılınca `-p`, PowerShell'in `-PipelineVariable` parametresiyle eşleşiyor.
  - `$home` salt okunur bir otomatik değişken.
  - Betik bu yüzden `$args` içinden elle ayrıştırma yapıyor.

## Faz 6: Hassas veri izleme ve engelleme (DLP) (15.11.2026)

**Yapılandırma:** chart 0.2.0, `guardrails` değerleri; her `AgentgatewayModel`'e `policies.promptGuard` olarak uygulanıyor.

- **İstek:** TC Kimlik, TR IBAN ve kart numarası → `Reject`. E-posta ve telefon → `Mask`.
- **Yanıt:** Aynı veriler → `Mask`.

| Senaryo | Sonuç |
|---|---|
| Temiz istek | ✅ 200 |
| İstemde TC Kimlik (curl) | ✅ Engellendi; model hiç çağrılmadı (2 ms) |
| İstemde e-posta ve telefon | ✅ Model `<EMAIL_ADDRESS>` ve `<masked>` olarak gördü |
| Copilot CLI istemde TC Kimlik | ✅ CLI Türkçe mesajı gösterdi: `400 İstek engellendi: ...` |
| TC Kimlik **MCP araç çıktısında** ortaya çıktı (istemde yoktu) | ✅ Araç çıktısını taşıyan sonraki model isteği engellendi |

**İzleme:**

- Her LLM isteğinin log satırında `agw.ai.guardrails=[{phase, guard, action}]` var; engellemelerde ayrıca `reason=Guardrail`.
- İstemin ve yanıtın tamamı CEL ile (`llm.prompt`, `llm.completion`) loga eklenebilir. Ancak bu hem performans maliyeti getirir hem de hassas veriyi log'a taşır; ham hâliyle önerilmez. Panel bunu yalnızca korunan isteklerde ve maskeleyerek yapıyor (Faz 8).

**Dersler:**

- **Copilot CLI, `403` yanıtını "kimlik doğrulama hatası" sanıp asıl mesajı gizliyor.** Engelleme yanıtı `400` ve OpenAI hata biçiminde (`{"error":{"message":...}}`) dönünce CLI mesajı gösterdi.
- **Hazır kalıplar ABD/Kanada odaklı** (`Ssn`, `CaSin`). TC Kimlik, IBAN ve TR telefon için özel regex gerekiyor. Regex yanlış pozitif üretebiliyor (örnekte bir sayı dizisi telefon sanılıp maskelendi) ve TC Kimlik sağlama toplamını (checksum) doğrulayamıyor. İkinci katman olarak webhook guardrail önerilir, örneğin `turkish-pii` modeliyle.
- Git Bash, Türkçe karakterli komut satırı argümanlarını Windows'a aktarırken UTF-8'i bozuyor (`400 invalid JSON`). Gövde dosyadan gönderilmeli.

**GitHub'ın kendi modelleri:**

- CLI doğrudan `api.githubcopilot.com`'a giderse trafik TLS ile şifreli olur; agentgateway bunu göremez.
- Çözüm: CLI'ı BYOK ile gateway'e yönlendirmek ve gateway'de `provider: copilot` ile GitHub Copilot API'sine iletmek. Bu durumda aynı log ve guardrail'ler GitHub modelleri için de geçerli olur. Copilot aboneliği gerektirdiği için henüz test edilmedi.
- Şirkette kullanıcıların gateway'i atlamasını önlemek için `api.githubcopilot.com` çıkışı yalnızca gateway'e açılmalıdır.

## Faz 7: İzleme paneli (agentgateway-console) (15.11.2026)

**Ne:** ASP.NET Core 8 MVC, EF Core 8 + PostgreSQL. `webds` projesinin konvansiyonları izlendi: Repository, Messages tr/en, DataTables, LogService, AppSettings.

- **Veri akışı:** agentgateway OTLP/HTTP protobuf ile `/v1/logs` adresine gönderiyor (`AgentgatewayPolicy frontend.accessLog.otlp`).
- **Ekranlar:** Koruma Özeti, Yakalanan Hassas Veriler, Trafik Kaydı.

| Doğrulama | Sonuç |
|---|---|
| `dotnet test` | ✅ 55/55 |
| Helm lint, şema ve sunucu dry-run | ✅ |
| Uçtan uca: temiz / TC / kart / e-posta istekleri | ✅ Panel: 4 istek, 2 engellenen, 1 maskelenen |
| **Çapraz kontrol** (proxy Prometheus metrikleri ile panel, aynı zaman aralığındaki fark) | ✅ Engellenen +2/+2, maskelenen +3/+3, girdi token +32.316/+32.316, MCP araç çağrısı +1/+1 |
| Arayüz (Chrome): 3 ekran, ayrıntı modalı, filtre, TR/EN geçişi | ✅ Konsol hatası yok |
| Harbor → Rancher ClusterRepo (`harbor-agentgateway-console`) | ✅ |

**Dersler:**

- **agentgateway OTLP alan adları stdout'tan farklı:** `http.status` → `http.response.status_code`, `http.path` → `url.path`, `src.addr` → `client.address`. `TimeUnixNano` 0 geliyor; zaman için `ObservedTimeUnixNano` kullanılmalı. `agw.ai.guardrails` dizi değil, JSON metni.
- **.NET'te OTLP tipleri için resmi NuGet paketi yok.** opentelemetry-proto'nun 4 `.proto` dosyası projeye kopyalanıp `Grpc.Tools` ile derlendi.
- **EF Core, constructor ile kurulan record'un özelliğine göre sıralamayı SQL'e çeviremez.** InMemory testleri bunu yakalamaz; anonim tipe projeksiyon yapılmalı.
- **`runAsNonRoot` + imajda `USER app` (isim) → `CreateContainerConfigError`.** Sayısal UID (`$APP_UID` = 1654) gerekiyor.
- **Copilot CLI'ın kendi istekleri de "Maskelendi" olarak görünüyor.** Copilot'un sistem istemindeki bir metin e-posta/telefon kalıbına uyuyor (regex yanlış pozitifi). Webhook tabanlı ikinci katman bunu azaltır. Kaynağı Faz 8'de bulundu.
- Chrome, `*.sslip.io` HTTP adresini HTTPS'e yükseltiyor; yerel CA'ya güvenilmiyorsa port-forward ile `localhost` kullanılabilir.

## Faz 8: Yakalanan isteğin akışı (istem + araç çağrıları) (15.11.2026)

**Ne:** "Yakalanan Hassas Veriler" ayrıntısında, engellenen veya maskelenen isteğin **istek akışı** gösteriliyor: kullanıcı istemi, modelin araç çağrıları (ad + argüman), araç sonuçları ve hassas verinin hangi adımda bulunduğu. Panel sürümü 0.2.0.

**agentgateway v1.7.0'ın log'a verebildikleri (sahte veriyle denendi):**

| CEL | Engellenen istekte | Maskelenen istekte | Not |
|---|---|---|---|
| `llm.prompt` | ❌ **gelmiyor** | ✅ maskelenmiş hâli (`<EMAIL_ADDRESS>`) | Asistan mesajlarındaki `tool_calls` düşürülüyor |
| `string(request.body)` | ✅ | ✅ | **Ham**, maskesiz; `tool_calls` ve `tool_call_id` dahil tam yapı |
| `llm.toolCalls` | — | ✅ | Modelin yanıttaki çağrıları: `{id, name, arguments}` |
| `mcp.tool` | — | — | Hedef, araç, argüman, sonuç; MCP'de guardrail olmadığı için maskesiz |
| `guardrails` | ✅ | ✅ | `[{phase, guard, action}]`; **hangi desenin** eşleştiği yazmıyor |

**Tasarım:**

- Gateway gövdeyi yalnızca guardrail devreye girdiğinde gönderir: `guardrails.exists(g, g.action == "reject" || g.action == "mask") ? string(request.body) : ""`. Temiz isteklerin içeriği gateway'den çıkmaz (doğrulandı: 0 bayt).
- Panel gövdeyi kaydetmeden önce kendi kurallarıyla maskeler (TC, IBAN, kart, TR telefon, e-posta + 9+ haneli sayı güvenlik ağı). Sistem mesajını ve araç tanımlarını atar, yalnızca son kullanıcı istemi ile sonrasını ve verinin bulunduğu mesajları tutar. Maskeleme zaman aşımına uğrarsa içerik hiç saklanmaz.
- İçerik ayrı tabloda (`GatewayRequestContents`, jsonb) tutulur; liste ve saklama sorguları onu yüklemez. Log silinince cascade ile silinir.
- Ham değerin `agwc.*` nitelikleri, `Ingest:ExcludedAttributePrefixes` ayarından bağımsız olarak hiçbir koşulda `AttributesJson`'a yazılmaz.

| Doğrulama | Sonuç |
|---|---|
| `dotnet test` | ✅ 83/83 (maskeleme, akış çıkarma, "ham değer DB'ye girmez" uçtan uca testi dahil) |
| Copilot CLI: `glob` → `view musteri.txt` (TC + e-posta) → engellendi | ✅ Akış birebir görünüyor; araç sonucu `view` işaretli |
| Boyut | Copilot'un 79 KB'lık gövdesi → 2,9 KB maskelenmiş akış |
| Veritabanında ham TC / ham e-posta / `agwc.*` taraması | ✅ 0 kayıt |
| Temiz istek, MCP istekleri | ✅ İçerik oluşmadı |
| Arayüz (Chrome) | ✅ Akış, rozetler, vurgulanan etiketler; konsol hatası yok |

**Bulgular ve dersler:**

- **Copilot'un "Maskelendi" yanlış pozitifinin kaynağı bulundu:** Copilot'un ~29 bin karakterlik sistem isteminde, commit imzası şablonu olarak bir e-posta adresi geçiyor (`Co-authored-by: Copilot <…@…>`). Gateway'in `Email` kuralı bu adresi maskeliyor; dolayısıyla Copilot'un her isteği "Maskelendi" görünüyor. Panel bunu "Sistem mesajı" konumlu bulgu olarak gösteriyor.
- **Sıralama riski:** Log ayarı yeni panel pod'u hazır olmadan uygulanırsa, eski sürüm `agwc.*` alanlarını tanımaz ve ham gövdeyi yazar. Yükseltme iki adımda yapıldı: önce `capture.enabled=false` ile yeni imaj, sonra yakalama açıldı.
- **Helm, değer verilmeden yapılan `upgrade`'de önceki sürümün değerlerini koruyor.** `--set capture.enabled=false` sonrasındaki düz `helm upgrade` yakalamayı açmadı; `--reset-values` gerekti.
- **System.Text.Json varsayılan olarak `<`, `>` ve Türkçe karakterleri `\uXXXX` olarak kaçışlıyor.** Veritabanında okunabilirlik için saklamada `UnsafeRelaxedJsonEscaping` kullanıldı (HTML'e gömülmediği için güvenli).

## Faz 9: Arayüz — Hallmark Cobalt teması (15.11.2026)

**Ne:** Panelin görünümü Hallmark'ın Cobalt temasına göre yeniden yazıldı. Kaynak: [Nutlope/hallmark](https://github.com/Nutlope/hallmark), `references/themes/cobalt.md` ve `site/themes/cobalt/tokens.css`. Panel sürümü 0.3.0. Sunucu kodu, veri ve API değişmedi.

**Tasarım kararları (temanın kurallarından):**

| Kural | Panelde |
|---|---|
| Tek vurgu kobalt; durum renkli rozet değil, tek ikonlu metin | Engellendi (hata rengi), Maskelendi (kobalt), Temiz (yeşil tik), Kontrol yok (soluk) |
| Etiketler Familjen Grotesk, makine değerleri JetBrains Mono, gövde IBM Plex Sans | Zaman, model, IP, durum kodu ve süre eş aralıklı yazıyla |
| Düğmeler gerçek alt kenarı olan tuşlar; seçenekler gömük tepside | Zaman aralığı ve dil seçimi tepside; sayfalama tuşları |
| Paneller iki karşı köşesi kesik, ince çizgili | `corner-shape: bevel` (destekleyen tarayıcıda), diğerlerinde 8 px yuvarlak |
| Kod grafit zeminde, sözdizimi tek renk (`accent-2`) | Guardrail JSON'u ve tüm nitelikler |
| Veri görselleştirme: halka/pasta yok, grafik kütüphanesi yok, KPI duvarı yok | Zaman grafiği veriye göre çizilen SVG (yığılmış çubuk); model/araç dağılımı sıralı tablo + çubuk; 7 KPI kartı yerine "koruma defteri" |

**Yapısal değişiklikler:**

- Bootstrap CSS/JS, bootstrap-icons, SweetAlert2 ve Chart.js artık yüklenmiyor. İkonlar layout'a gömülü SVG setinden (`_Icons.cshtml`), bildirimler `Ui.toastError`'dan geliyor. Dosyaları `wwwroot/lib` altında duruyor (silinmedi).
- Kayıt ayrıntısı Bootstrap modal yerine sağdan açılan yerel `<dialog>` (Esc, arka plan tıklaması, odak satıra döner). Satırlar klavyeyle (Enter/Boşluk) açılıyor.
- İstek akışının sonuna **Gateway kararı** adımı eklendi; yanıt aşamasındaki "sonraki adım" ve "model yanıtı" ayrı bölüm yerine akışın adımları oldu.
- Yazı tipleri `wwwroot/lib/fonts` altında yerel (Google Fonts'tan latin + latin-ext değişken woff2, OFL lisanslı). Kurumsal ortamda dış CDN'e bağımlılık yok.

**Doğrulama:**

| Kontrol | Sonuç |
|---|---|
| `dotnet test` | ✅ 83/83 (TR/EN mesaj eşitliği dahil) |
| Üç ekran, 1440 px (Chrome) | ✅ Konsol hatası yok; üç yazı tipi yerel kopyadan yükleniyor |
| 390 px genişlik | ✅ Sayfa yatay taşmıyor; tablolar kendi kutusunda kayıyor, inceleme paneli tam ekran |
| Cluster | ✅ `agentgateway-console:0.3.0` çalışıyor; yakalama politikası (3 `agwc.*` niteliği) yerinde; chart 0.3.0 Harbor'da |

**Dersler:**

- **`X-Frame-Options: DENY` panelin iframe'e alınmasını engelliyor** (doğru davranış). Dar ekran testi, sayfa HTML'i `srcdoc` ile yüklenerek yapıldı.
- **Gateway yerelde `localhost:8080`'i kullanıyor**; panel yerelde çalıştırılırken `Settings__UiPort` ile başka porta alınmalı.

## Sonraki adımlar

1. **Gerçek Rancher cluster'ı.** `docs/kurumsal-kurulum.md` rehberini izleyin: ayrı Harbor, RKE2 üzerinde HA Rancher, kurumsal CA ve CI'dan chart yayını.
2. **Kimlik doğrulama.** Gateway'i cluster dışına açmadan önce `AgentgatewayPolicy` ile JWT (Keycloak/Entra) veya API key ekleyin.
3. **Model.** Host'taki Ollama yerine cluster içi Ollama/vLLM ya da bulut sağlayıcı kullanın. Copilot için bağlam penceresi en az 16k olmalı.
