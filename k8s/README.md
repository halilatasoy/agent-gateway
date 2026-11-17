# Kubernetes / Rancher kurulumu

agentgateway, Kubernetes'te dört Helm/kubectl adımıyla kurulur. İlk üçü upstream, sonuncusu bu depodaki yapılandırma chart'ıdır:

| # | Ne | Kaynak | Kapsam |
|---|---|---|---|
| 1 | Gateway API CRD'leri v1.6.3 | `kubernetes-sigs/gateway-api` release YAML | cluster |
| 2 | `agentgateway-crds` v1.7.0 | `oci://cr.agentgateway.dev/charts/agentgateway-crds` | cluster |
| 3 | `agentgateway` v1.7.0 (controller) | `oci://cr.agentgateway.dev/charts/agentgateway` | `agentgateway-system` |
| 4 | `agentgateway-stack` | [charts/agentgateway-stack](charts/agentgateway-stack) | `agentgateway-system` |

Neden tek chart değil: Helm, bir CRD'yi ve o CRD'yi kullanan kaynakları aynı release'de kuramaz (`no matches for kind`). Bu yüzden CRD'ler ve yapılandırma ayrı release'ler. Rancher'da bunlar ayrı uygulamalar olarak kurulur; Fleet kullanılıyorsa `dependsOn` ile sıralanır.

## Yerel deneme: WSL2 + Rancher Desktop

```powershell
.\k8s\setup-local-cluster.ps1
```

Betik eksik olan ilk adımı yapar ve kaldığı yerden devam eder:

1. **WSL2.** Yönetici PowerShell gerekir. Kurulumdan sonra bilgisayarı yeniden başlatın.
2. **Rancher Desktop v1.24.0.** Yalnızca bu kullanıcıya kurulur, yönetici gerekmez. MSI SHA512 ile doğrulanır.
3. **Kubernetes.** `rdctl start` ile containerd + k3s başlatılır.
4. **agentgateway.** [install.ps1](install.ps1) çalışır.

Cluster zaten varsa yalnızca 4. adım yeterlidir:

```powershell
.\k8s\install.ps1                          # varsayılan değerlerle
.\k8s\install.ps1 -ValuesFile my-values.yaml
```

## Test

```powershell
curl.exe http://localhost:8080/v1/models
# LoadBalancer'ın localhost'a yönlendirilmediği ortamlarda:
kubectl port-forward -n agentgateway-system deploy/agentgateway-proxy 8080:8080
```

Copilot SDK probe'unu cluster'a karşı çalıştırmak için:

```powershell
kubectl logs -f deploy/agentgateway-proxy -n agentgateway-system > logs\k8s-proxy.log   # ayrı terminalde
cd copilot-probe
dotnet run -- --scenario all --gateway http://localhost:8080 --gateway-log ..\logs\k8s-proxy.log
```

Log kanıtı istemiyorsanız `--gateway-log none` verin.

## Notlar

- **Ollama erişimi.** Rancher Desktop'ın ağ tüneli sayesinde `host.rancher-desktop.internal`, host'un `127.0.0.1` adresine ulaşır (doğrulandı). Ollama'yı ağa açmaya gerek yoktur. Gerçek cluster'da modeli cluster içinde (Ollama/vLLM) çalıştırıp `ollama.baseURL` değerini o servise yöneltin.
- **Erişim.** Rancher Desktop, LoadBalancer Service'lerini Windows'ta `localhost`'a yönlendirir. Gateway port-forward olmadan `http://localhost:8080` adresinden erişilebilir.
- **Gateway API zaten kurulu olabilir.** k3s/RKE2'de Traefik chart'ı (`traefik-crd`) Gateway API CRD'lerini kendisi kurar. `install.ps1` sürüm yeterliyse bu adımı atlar.

## Yerel platform: Harbor + Rancher arayüzü

Şirketteki düzeni yerelde görmek için aynı cluster'a cert-manager, Harbor ve Rancher Manager kurulur. Chart'lar Harbor'a yayınlanır ve Harbor, Rancher'a OCI repository olarak eklenir:

```powershell
.\k8s\platform\install-platform.ps1
```

| Arayüz | Adres | Giriş |
|---|---|---|
| Rancher | https://rancher.127.0.0.1.sslip.io | `kubectl get secret bootstrap-secret -n cattle-system -o go-template='{{.data.bootstrapPassword\|base64decode}}'` |
| Harbor | https://harbor.127.0.0.1.sslip.io | `admin` / `kubectl get secret harbor-admin -n harbor -o go-template='{{.data.HARBOR_ADMIN_PASSWORD\|base64decode}}'` |

Sertifikalar yerel bir CA ile imzalıdır (`k8s/platform/.local/local-ca.crt`). Tarayıcı uyarısını kaldırmak için bu CA'yı kullanıcı güven deposuna ekleyebilirsiniz: `certutil -user -addstore Root <dosya>`.

Rancher'da **Apps → Charts** ekranında Harbor'dan gelen `Agent Gateway Stack`, `agentgateway` ve `agentgateway-crds` görünür. CLI ile kurulmuş release'ler de **Apps → Installed Apps** altında listelenir.

## İzleme paneli (agentgateway-console)

Kaç istek gittiği, token kullanımı ve hassas verinin ne zaman engellendiği veya maskelendiği bu panelden izlenir. Gateway, erişim loglarını OTLP ile panele gönderir; panel bunları PostgreSQL'de tutar. Ayrıntı: [console/README.md](../console/README.md).

```powershell
nerdctl --namespace k8s.io build -t agentgateway-console:0.3.0 console
helm upgrade -i agentgateway-console k8s\charts\agentgateway-console -n agentgateway-system
```

Var olan bir kurulumu yükseltirken [console/README.md](../console/README.md) içindeki iki adımlı sırayı izleyin.

- Adres: https://console.127.0.0.1.sslip.io. Yerel CA'ya güvenilmiyorsa: `kubectl port-forward -n agentgateway-system svc/agentgateway-console 8085:80` → http://localhost:8085
- Rancher'da **Apps → Charts → Agent Gateway İzleme Paneli** olarak da görünür (Harbor üzerinden).

## Rancher'a taşıma (sonraki adım)

Ayrıntılı rehber: [docs/kurumsal-kurulum.md](../docs/kurumsal-kurulum.md). Özetle:

1. Chart'ları kurumsal Harbor'a yayınlayın: `k8s/platform/publish-charts.ps1`, CI'da push yetkili robot hesabıyla.
2. Rancher'da **Apps → Repositories → Create** ekranından chart başına bir OCI repository ekleyin.
3. **Agent Gateway Stack** uygulaması, [questions.yaml](charts/agentgateway-stack/questions.yaml) sayesinde Türkçe bir kurulum formuyla açılır.

**Alternatif:** CRD kurma yetkisinin olmadığı ortamlarda upstream `oci://cr.agentgateway.dev/charts/agentgateway-standalone` chart'ı da yayında (1.7.0). Bu chart Gateway API veya CRD gerektirmez ve yerelde kullandığımız `config.yaml` biçimini doğrudan alır.
