# agentgateway yerel deneme + Copilot SDK entegrasyonu

[agentgateway](https://github.com/agentgateway/agentgateway) (v1.7.0) bu makinede **standalone** modda, Kubernetes olmadan çalışıyor. LLM trafiği yerel Ollama'ya, MCP trafiği bir test MCP sunucusuna yönlendiriliyor. `copilot-probe/` klasöründeki uygulama, GitHub Copilot SDK'nın bu gateway üzerinden çalışıp çalışmadığını test ediyor.

Sonuçlar ve öneriler: [docs/findings.md](docs/findings.md)
Kubernetes/Rancher kurulumu (Helm chart, yerel Harbor + Rancher arayüzü): [k8s/README.md](k8s/README.md)
Şirket ortamına taşıma rehberi: [docs/kurumsal-kurulum.md](docs/kurumsal-kurulum.md)
İzleme paneli (istek, token, engellenen/maskelenen hassas veri): [console/README.md](console/README.md)

## Gereksinimler

- `gh` CLI (oturum açık): binary'yi indirmek için
- Node.js 22+: MCP test sunucusu (`npx`) için
- .NET 8 SDK: `copilot-probe` için
- Ollama ve `qwen3:14b` modeli
- Copilot CLI'ın tam ajan modunu test etmek için 24k bağlamlı türev model:
  `ollama create qwen3-14b-24k -f standalone/ollama/qwen3-14b-24k.Modelfile`

## Kurulum

```powershell
.\scripts\download-agentgateway.ps1          # bin\agentgateway.exe + bin\agctl.exe, SHA256 doğrulamalı
```

## Çalıştırma (iki ayrı terminal)

```powershell
.\scripts\start-mcp-everything.ps1           # MCP test sunucusu  -> http://localhost:3005/mcp
.\scripts\start-gateway.ps1                  # agentgateway       -> http://localhost:4000
```

| Uç nokta | Adres |
|---|---|
| LLM (OpenAI uyumlu) | `http://localhost:4000/v1/chat/completions`, model listesi `/v1/models` |
| MCP (tüm hedefler tek sunucu) | `http://localhost:4000/mcp` |
| UI (LLM ve MCP playground) | `http://localhost:4000/ui/` |

Gateway `standalone/config.yaml` dosyasını izler. Model veya MCP hedefi eklediğinizde yeniden başlatmaya gerek yoktur.

## Hızlı test

```powershell
curl.exe http://localhost:4000/v1/chat/completions -H "content-type: application/json" `
  -d '{\"model\":\"qwen3:14b\",\"messages\":[{\"role\":\"user\",\"content\":\"Merhaba\"}]}'
```

## Copilot SDK probe

```powershell
cd copilot-probe
dotnet run -- --scenario all                                              # llm + mcp + full, Empty mod
dotnet run -- --scenario full --mode CopilotCli --model qwen3-14b-24k     # Copilot CLI tam ajan modu
```

| Senaryo | LLM yolu | MCP yolu |
|---|---|---|
| `llm` | gateway → Ollama | — |
| `mcp` | doğrudan Ollama | gateway → MCP sunucusu |
| `full` | gateway → Ollama | gateway → MCP sunucusu |

Probe, gateway erişim logunu (`logs/gateway.log`) okuyarak isteklerin gerçekten gateway'den geçtiğini kontrol eder. Bunun için gateway'in çıktısını o dosyaya yönlendirin:

```powershell
$env:SESSION_KEY = Get-Content standalone\.session-key   # start-gateway.ps1 ilk çalıştırmada üretir
.\bin\agentgateway.exe -f standalone\config.yaml *> logs\gateway.log
```

Log dosyası yoksa log kontrolü atlanır ve yalnızca SDK olaylarına bakılır.

## Copilot CLI (gateway üzerinden)

GitHub Copilot CLI 1.0.105, proje içine kuruludur (`tools/copilot-cli`, global kurulum yok). Kurmak için:

```powershell
npm install @github/copilot@1.0.105 --prefix tools\copilot-cli
```

Çalıştırmak için:

```powershell
.\scripts\copilot-gateway.ps1                         # etkileşimli oturum
.\scripts\copilot-gateway.ps1 -p "merhaba" -s         # tek seferlik istem
.\scripts\copilot-gateway.ps1 -Gateway http://localhost:4000   # yerel standalone gateway
```

Betik şunları yapar:

- Model trafiğini gateway'e yönlendirir (`COPILOT_PROVIDER_BASE_URL=<gateway>/v1`, varsayılan model `qwen3-14b-24k`).
- Gateway'in MCP uç noktasını `gw` adıyla ekler.
- GitHub'a hiç bağlanmaz (`COPILOT_OFFLINE=true`); giriş veya abonelik gerekmez.
- Ayarları `~/.copilot` yerine `tools/copilot-cli/home` altında tutar.

Trafiği izlemek için Rancher'da **Workloads → agentgateway-proxy → View Logs** ekranını açın ya da şu komutu çalıştırın: `kubectl logs -f deploy/agentgateway-proxy -n agentgateway-system`.

## Notlar

- **Kimlik doğrulama yok.** Bu yüzden gateway yalnızca `127.0.0.1`'e bağlanıyor. Yine de yönetim portları (`15020` stats, `15021` readiness) tüm arayüzleri dinliyor.
- MCP oturum ID şifreleme anahtarı git dışındaki `standalone/.session-key` dosyasında durur; `start-gateway.ps1` onu `SESSION_KEY` ortam değişkeni olarak verir, dosya yoksa üretir. Gateway'i `SESSION_KEY` olmadan doğrudan başlatırsanız anahtarı `config.yaml`'a yazar; o değişikliği commit etmeyin.
- Probe, Copilot runtime verisini `~/.copilot` yerine `copilot-probe/.copilot-home/` klasörüne yazar.
