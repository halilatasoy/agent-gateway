# Şirket ortamında kurulum rehberi: Harbor + Rancher + agentgateway

Bu rehber, yerelde (Rancher Desktop) denediğimiz düzenin şirket ortamına nasıl taşınacağını anlatır. Yerel kurulum betikleri `k8s/platform/` altında. Yereldeki her parçanın şirketteki karşılığı aşağıdaki tabloda.

## 1. Hedef mimari

```
                 ┌──────────────────────── Platform ekibi ────────────────────────┐
 Geliştirici /   │                                                                │
 CI hattı  ──push──> Harbor (harbor.sirket.local)          Rancher Manager        │
                 │   ├─ proje: agentgateway (chart'lar)    (rancher.sirket.local) │
                 │   ├─ proxy cache: docker.io, ghcr.io,   RKE2 yönetim cluster'ı │
                 │   │  cr.agentgateway.dev (imajlar)      3 düğüm, HA            │
                 │   └─ robot hesapları, Trivy taraması          │                │
                 └──────────────┬──────────────────────────────────┼──────────────┘
                                │ chart + imaj pull                │ yönetir
                                ▼                                  ▼
                 ┌──────────── İş yükü cluster'ı (RKE2) ────────────────┐
                 │ agentgateway-crds → agentgateway → agentgateway-stack │
                 │ Gateway (agentgateway-system) ── LLM: vLLM / Azure OpenAI
                 │                               └─ MCP: iç araç sunucuları
                 └───────────────────────────────────────────────────────┘
```

**Temel kural:** Harbor ve Rancher, iş yüklerinin çalıştığı cluster'dan **ayrı** platform servisleridir. Yerelde kaynak tasarrufu için üçünü tek cluster'a koyduk; şirkette böyle yapmayın.

## 2. Yerel ↔ şirket karşılıkları

| Konu | Yerelde (deneme) | Şirkette |
|---|---|---|
| Kubernetes | Rancher Desktop (k3s, tek düğüm) | RKE2: ayrı yönetim cluster'ı + iş yükü cluster'ları |
| Rancher | Aynı cluster'da, 1 replika | Yönetim cluster'ında, 3 replika (HA) |
| Harbor | Aynı cluster'da, dahili PostgreSQL/Redis | Ayrı VM (offline installer) veya ayrı cluster; HA için harici PostgreSQL, Redis ve S3/NFS depolama |
| DNS | `*.127.0.0.1.sslip.io` + CoreDNS yönlendirmesi | Kurumsal DNS kayıtları (`harbor.sirket.local`, `rancher.sirket.local`) |
| TLS | cert-manager ile öz-imzalı yerel CA | Kurumsal CA (veya cert-manager'da kurumsal ClusterIssuer) |
| Kimlik | Yerel admin parolaları | Rancher ve Harbor'da AD/LDAP veya OIDC (Keycloak/Entra ID) |
| Chart yayını | `admin` kullanıcısı ile elle | CI hattında, yalnızca push yetkili robot hesabı |
| Rancher → Harbor | Salt-okunur robot hesabı | Aynısı: salt-okunur robot hesabı |
| İmajlar | Doğrudan internetten (cr.agentgateway.dev, docker.io) | Harbor proxy cache üzerinden; cluster'ların internete çıkışı gerekmez |
| Zafiyet taraması | Trivy kapalı | Trivy açık; kritik zafiyetli imajın çekilmesi engellenebilir |
| Model | Host'taki Ollama | Cluster içi vLLM (GPU) veya kurumsal Azure OpenAI vb. |

## 3. Adım adım

### 3.1 Harbor (platform ekibi)

1. **Kurulum.** Tek sunucu için en basit yol, Harbor'ın offline installer'ıdır (Docker Compose). HA gerekiyorsa `goharbor/harbor` Helm chart'ı harici PostgreSQL, Redis ve nesne depolama ile kurulur. Yerelde kullandığımız değerler [k8s/platform/values/harbor-local.yaml](../k8s/platform/values/harbor-local.yaml) dosyasında.
2. **TLS.** Kurumsal CA'dan `harbor.sirket.local` için sertifika alın.
3. **Kimlik.** Yönetim → Yapılandırma → Kimlik Doğrulama ekranından LDAP veya OIDC bağlayın.
4. **Proje.** `agentgateway` adında özel (private) bir proje açın. Etiket değiştirilemezliği (tag immutability) kuralı ekleyin; yayınlanmış bir chart sürümünün üzerine yazılamasın.
5. **Robot hesapları.** Proje → Robot Accounts:
   - `ci`: repository **push + pull**. Yalnızca CI hattının secret'larında tutulur.
   - `rancher`: repository **pull + list**. Rancher'daki repository tanımında kullanılır.
6. **Proxy cache (imajlar için).** Registries ekranından uç noktaları tanımlayın (Docker Hub, `ghcr.io`, `cr.agentgateway.dev`) ve her biri için proxy cache projesi açın. `cr.agentgateway.dev` için genel "Docker Registry" tipini kullanın; bu uç noktanın Harbor proxy cache ile çalıştığını ilk kurulumda doğrulayın.

### 3.2 Chart'ları Harbor'a yayınlama (uygulama ekibi / CI)

Yerelde kullandığımız betik CI'da da aynen kullanılabilir:

```powershell
.\k8s\platform\publish-charts.ps1 -Registry harbor.sirket.local -Project agentgateway `
    -Username 'robot$agentgateway+ci' -Password $env:HARBOR_ROBOT_SECRET
```

Betik `agentgateway-stack` chart'ını paketleyip gönderir. Upstream `agentgateway-crds` ve `agentgateway` chart'larını da aynı projeye aynalar; böylece Rancher her şeyi tek bir kaynaktan çeker. Sürümü `v` ile başlayan chart'lara Rancher uyumluluğu için ek etiket de ekler.

CI Windows/PowerShell 5.1 üzerinde çalışacaksa dikkat: parolayı `"..." | helm registry login --password-stdin` ile pipe etmeyin. Konsol kodlaması UTF-8 ise PowerShell parolanın başına BOM ekliyor ve Harbor `401` döndürüyor. Betik bunu ele alıyor. Linux CI'da bu sorun yok.

Önerilen CI adımları:

1. `helm lint --strict`
2. `helm template` + şema doğrulaması
3. `Chart.yaml` içinde `version` artırıldı mı kontrolü
4. `publish-charts.ps1`

### 3.3 Rancher (platform ekibi)

1. **Kurulum.** Yönetim cluster'ına (RKE2, 3 düğüm) `rancher-stable/rancher` chart'ı ile kurulur: `replicas: 3`, `hostname: rancher.sirket.local`, kurumsal sertifika için `ingress.tls.source: secret` ve `privateCA: true`. Yerelde kullandığımız değerler [k8s/platform/values/rancher-local.yaml](../k8s/platform/values/rancher-local.yaml) dosyasında. Rancher 2.15.x, Kubernetes <1.37'yi destekliyor.
   - **Kurumsal CA'yı Rancher'ın güven deposuna ekleyin:** `additionalTrustedCAs: true` ve `cattle-system/tls-ca-additional` Secret'ı. Böylece Harbor gibi iç servislere repository bazında CA tanımlamaya gerek kalmaz. Bu, aşağıdaki `caBundle` tuzağından da kaçınmanın en temiz yoludur.
2. **Kimlik.** Users & Authentication → Auth Provider ekranından AD/LDAP veya OIDC bağlayın.
3. **İş yükü cluster'ı.** Cluster Management → Create ile RKE2 cluster oluşturun veya mevcut bir cluster'ı Import edin.
4. **Harbor'ı repository olarak ekleyin.** İlgili cluster'da Apps → Repositories → Create:
   - **Target:** OCI Repository
   - **URL:** Chart başına bir repository (gerekçe aşağıda):
     - `oci://harbor.sirket.local/agentgateway/agentgateway-stack`
     - `oci://harbor.sirket.local/agentgateway/agentgateway`
     - `oci://harbor.sirket.local/agentgateway/agentgateway-crds`
   - **Authentication:** Basic auth; `rancher` robot hesabı
   - GitOps tercih ediliyorsa aynı tanımı `ClusterRepo` YAML'ı olarak Fleet ile uygulayın. Örnek: [install-platform.ps1](../k8s/platform/install-platform.ps1), 8. adım.

   **Yerel denemede bulunan üç Rancher 2.15 / Harbor tuzağı:**

   | Tuzak | Belirti | Çözüm |
   |---|---|---|
   | Proje düzeyinde URL (`oci://harbor/agentgateway`) chart listesi için `/v2/_catalog` kullanıyor. Proje robotu bu çağrıda `401` aldı. | Repository boş kalır veya hata verir | Chart başına repository tanımlayın (yukarıdaki gibi) |
   | OCI repository'de `caBundle` alanı **DER** bekliyor (`x509.ParseCertificate`); alan açıklaması PEM dese de PEM verilince hata alınıyor. | `x509: malformed certificate` | Kurumsal CA'yı `additionalTrustedCAs` ile ekleyin. Alan şart ise PEM'in başlık satırları atılmış gövdesini (DER'in base64'ü) verin. |
   | Rancher OCI etiketini semver'e normalleştiriyor. Upstream agentgateway chart sürümü `v1.7.0`; Rancher `1.7.0` etiketini arıyor. | `...:1.7.0: not found` | Harbor'da aynı artefakta `1.7.0` etiketini de ekleyin. [publish-charts.ps1](../k8s/platform/publish-charts.ps1) bunu otomatik yapıyor. |

### 3.4 agentgateway kurulumu (Rancher arayüzünden)

İş yükü cluster'ında Apps → Charts ekranından şu sırayla kurun:

1. **agentgateway-crds** (namespace `agentgateway-system`)
2. **agentgateway** (controller). İmajları Harbor proxy cache'ten çekmek için `image.registry: harbor.sirket.local/<proxy-proje>` ve gerekirse `imagePullSecrets` verin.
3. **Agent Gateway Stack.** Türkçe kurulum formu açılır. Prod için:
   - `mcp.demoServer.enabled: false` (demo sunucusu npm'den paket indirir)
   - `gateway.service.type: ClusterIP` (ingress/LB arkasında) veya kurumsal LoadBalancer
   - `ollama.baseURL` veya model listesi kurumsal model servisine göre

**Gateway API CRD'leri:** RKE2/k3s'te Traefik bunları zaten kurmuş olabilir. `kubectl get crd gateways.gateway.networking.k8s.io -o jsonpath='{.metadata.annotations.gateway\.networking\.k8s\.io/bundle-version}'` ile sürümü kontrol edin; v1.6.x ise ayrıca kurmayın.

### 3.5 Prod'a çıkmadan önce

- [ ] Gateway'de kimlik doğrulama: `AgentgatewayPolicy` ile JWT (Keycloak/Entra ID) veya API key
- [ ] Gateway dinleyicisinde TLS (kurumsal sertifika)
- [ ] `agentgateway-system` için NetworkPolicy: yalnızca izinli namespace'ler gateway'e erişebilsin
- [ ] Hız sınırı ve token bütçesi değerleri kullanım profiline göre ayarlandı
- [ ] Model bağlam penceresi ≥16k (Copilot CLI tam modu ~14k token gönderiyor)
- [ ] Rancher Monitoring ile gateway metrikleri (port 15020) toplanıyor; loglar merkezi sisteme gidiyor
- [ ] Harbor'da Trivy açık ve proxy cache'ler çalışıyor; cluster'ların doğrudan internet çıkışı kapalı
- [ ] Yedekleme: Harbor veritabanı ve depolama, Rancher (`rancher-backup` operatörü)

## 4. Copilot trafiğini izleme ve hassas veri engelleme

Copilot CLI'ın (ve SDK ile yazılan ajanların) her isteğini görmek ve hassas veriyi engellemek için trafik **mutlaka gateway'den geçmeli**.

```
Copilot CLI ──BYOK──> agentgateway ──┬─> provider: copilot  → api.githubcopilot.com (GitHub modelleri)
 (COPILOT_PROVIDER_BASE_URL)         ├─> kurumsal Azure OpenAI / vLLM
     guardrail + log + kota ◄────────┘
```

1. **İstemci ayarı.** Kullanıcı makinelerine `COPILOT_PROVIDER_BASE_URL=https://agentgateway.sirket.local/v1` ve kimlik doğrulama değişkenlerini MDM/GPO ile dağıtın. Yerel karşılığı: [scripts/copilot-gateway.ps1](../scripts/copilot-gateway.ps1).
2. **GitHub modelleri.** Gateway'de `provider: Copilot` olan bir `AgentgatewayModel` tanımlayın. Kimlik bilgisi: Copilot erişimi olan bir hesabın token'ı, Kubernetes Secret olarak. Kurumsal kullanımın GitHub sözleşmesine uygunluğunu hukuk/satın alma ile teyit edin.
3. **Atlatmayı önleme.** Kurumsal güvenlik duvarında `api.githubcopilot.com` ve model sağlayıcılarına çıkışı yalnızca gateway'in IP'lerine açın. Gateway'i atlayan doğrudan trafiği agentgateway göremez; TLS şifrelidir.
4. **Guardrail katmanları.**
   - Regex: chart'taki `guardrails` değerleri (TC Kimlik, IBAN, kart → engelle; e-posta, telefon → maskele).
   - Webhook: kurumsal DLP servisi veya Türkçe PII modeli. Daha az yanlış pozitif verir; isim ve adres tespiti ile TC Kimlik checksum doğrulaması yapabilir.
   - Kapsam varsayılan olarak MCP araç çıktılarını da içerir. İç veritabanı araçlarından dönen veri de modele gitmeden taranır.
5. **İzleme.** Log satırlarındaki `agw.ai.guardrails` ve `reason=Guardrail` alanlarını merkezi log sisteminde (Loki/ELK/SIEM) alarm kuralına bağlayın. İstem içeriğini tam loglamayın; loglar da hassas veri deposuna dönüşür.
6. **İzleme paneli (agentgateway-console).** Gateway'in OTLP loglarını alıp istek, token ve engellenen/maskelenen veri özetini gösterir. Şirkette:
   - İmajı CI derler ve Harbor'a gönderir (`harbor.sirket.local/agentgateway/agentgateway-console`). Chart'ta `image.repository` ve gerekiyorsa `imagePullSecrets` ayarlanır.
   - Arayüze **OIDC (Keycloak/Entra) eklenmeden** cluster dışına açılmamalıdır; v1'de kimlik doğrulama yoktur.
   - Chart içindeki tek pod'lu PostgreSQL yerine kurumsal/yedekli bir PostgreSQL kullanın (connection string Secret'tan gelir).
   - Saklama süresi DB `AppSettings` tablosunda (`Retention:Days`, varsayılan 30 gün); KVKK saklama politikasına göre ayarlayın.
   - Aynı loglar OTLP Collector ile SIEM'e de çatallanabilir. Panel ve SIEM birbirinin alternatifi değil, birbirini tamamlar.
7. **Yakalanan isteğin akışı (`capture.enabled`, varsayılan açık).** Engellenen veya maskelenen istekte kullanıcı istemi, araç çağrıları ve araç sonuçları panelde gösterilir. Şirkette:
   - **KVKK/hukuk onayı alın.** Panel istem içeriğini maskeleyerek saklar, ancak serbest metin (ad, adres, müşteri notu) desenlere takılmaz ve düz metin olarak kalır. Onay yoksa `capture.enabled=false` ile yalnızca karar ve istek bilgileri tutulur.
   - **Gövde gateway'den panele ham gider.** Cluster içinde de olsa bu trafik düz HTTP'dir. Panelin OTLP portuna (4318) yalnızca gateway pod'larının erişebileceği bir `NetworkPolicy` yazın; mesh varsa mTLS açın.
   - **Maskeleme kuralları iki yerde.** Gateway'in kuralları `agentgateway-stack` → `guardrails`, panelinkiler `appsettings.json` → `Redaction:Rules`. Gateway'e yeni bir desen eklediğinizde panele de ekleyin. Panelde ayrıca 9+ haneli her sayıyı maskeleyen bir güvenlik ağı var.
   - **Yükseltme sırası:** Panel imajını yükseltirken önce `capture.enabled=false` ile çıkın, yeni pod hazır olunca açın. Aksi halde eski sürüm ham gövdeyi yazabilir. Helm'de ikinci adımı `--reset-values` veya açık `--set` ile yapın.
   - Saklanan mesaj ve karakter sınırları DB `AppSettings` tablosunda: `Capture:MaxMessages` (30), `Capture:MaxMessageChars` (2000).

## 5. Birden çok cluster: Fleet (GitOps)

Aynı gateway birden çok cluster'a kurulacaksa, Rancher'daki Continuous Delivery (Fleet) ile bir Git deposundaki `fleet.yaml` dosyası Harbor'daki OCI chart'ları hedef cluster'lara uygular. Sıralamayı `dependsOn` belirler: CRD → controller → stack. Bu, elle kurulumun yerini alan önerilen uzun vadeli yoldur.
