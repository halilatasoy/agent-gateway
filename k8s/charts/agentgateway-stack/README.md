# Agent Gateway Stack

[agentgateway](https://agentgateway.dev) üzerinde LLM ve MCP trafiği için yapılandırma katmanı:

- **Gateway** (`gatewayClassName: agentgateway`) ve proxy Service ayarı (`AgentgatewayParameters`)
- **AgentgatewayModel**: OpenAI uyumlu `/v1` uç noktasında sunulan modeller (Ollama, OpenAI, Anthropic, Azure...)
- **AgentgatewayBackend + HTTPRoute**: MCP hedeflerini `/mcp` altında tek sanal MCP sunucusu olarak birleştirir
- **AgentgatewayPolicy**: gateway genelinde istek ve token hız sınırı
- İsteğe bağlı **demo MCP sunucusu** (`server-everything`)

## Önkoşullar

Bu chart yalnızca yapılandırma içerir. Önce şunlar kurulmalıdır:

1. Gateway API CRD'leri (v1.6.x)
2. `oci://cr.agentgateway.dev/charts/agentgateway-crds` (v1.7.0)
3. `oci://cr.agentgateway.dev/charts/agentgateway` (v1.7.0), `agentgateway-system` namespace'ine

Kurulum sırası ve betik için deponun `k8s/README.md` dosyasına bakın.

## Bulut sağlayıcı anahtarı

```bash
kubectl create secret generic openai-secret -n agentgateway-system --from-literal=Authorization=$OPENAI_API_KEY
```

`values.yaml` dosyasında modele `apiKey.existingSecret: openai-secret` verin.

## Copilot SDK ile kullanım

- `ProviderConfig.BaseUrl` → `http://<gateway>:<port>/v1`
- `McpHttpServerConfig.Url` → `http://<gateway>:<port>/mcp`

Copilot CLI tam ajan modu yaklaşık 13k token prompt gönderir. Kullanılan modelin bağlam penceresi en az 16k olmalıdır.

## Güvenlik

Kimlik doğrulama yapılandırılmamıştır. Gateway'i cluster dışına açmadan önce JWT veya API key politikası ekleyin.
