# MCP test sunucusu (server-everything) - Streamable HTTP, port 3005.
# agentgateway bu sunucuyu standalone/config.yaml içindeki "everything" hedefi olarak çoğaltır.
$ErrorActionPreference = 'Stop'
$env:PORT = '3005'
Write-Host "MCP test sunucusu başlatılıyor: http://localhost:3005/mcp"
# Sürüm, k8s chart'ındaki demo sunucusuyla aynı tutulur (2.x Node >=24 ister; 22'de uyarı verip çalışır).
npx -y @modelcontextprotocol/server-everything@2.1.0 streamableHttp
