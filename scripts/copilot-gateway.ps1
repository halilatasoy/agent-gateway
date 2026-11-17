# GitHub Copilot CLI'ı agentgateway üzerinden çalıştırır:
#   - Model trafiği (BYOK)  -> <Gateway>/v1   (agentgateway -> Ollama / bulut sağlayıcı)
#   - MCP araçları          -> <Gateway>/mcp  (sunucu adı: gw)
# Ayarlar kullanıcının ~/.copilot klasörüne değil, tools\copilot-cli\home altına yazılır.
#
# Kullanım:
#   .\scripts\copilot-gateway.ps1                                   # etkileşimli oturum
#   .\scripts\copilot-gateway.ps1 -p "merhaba" -s                   # tek seferlik istem
#   .\scripts\copilot-gateway.ps1 -Gateway http://localhost:4000    # yerel standalone gateway
# Betiğin kendi seçenekleri: -Gateway <url>, -Model <ad>, -MaxPromptTokens <n>, -MaxOutputTokens <n>, -Online
# Geri kalan tüm argümanlar Copilot CLI'a olduğu gibi iletilir.
#
# Not: Bilinçli olarak param() bloğu yok. param() olsaydı PowerShell, Copilot'a ait argümanları betiğin
# parametrelerine konumsal olarak bağlar ve [Parameter()] kullanılınca '-p' bayrağını -PipelineVariable sanar.
$ErrorActionPreference = 'Stop'

$Gateway = 'http://localhost:8080'
# Copilot CLI'ın tam modu ~14k token prompt gönderir; model bağlamı en az 16k olmalı.
$Model = 'qwen3-14b-24k'
# CLI'ın sistem mesajı + ~41 araç tanımı, bütçenin "kullanılabilir" kısmına sığmalı; 20000/4096 ile
# 'compaction_static_context_blocked' uyarısı alınıp hiç istek gönderilmedi. 24k bağlamlı model için:
$MaxPromptTokens = 24000
$MaxOutputTokens = 2048
# Varsayılan: GitHub'a hiç bağlanma (giriş/abonelik gerekmez). -Online ile GitHub hesabı da kullanılır.
$Offline = $true

$passThru = New-Object System.Collections.Generic.List[string]
for ($i = 0; $i -lt $args.Count; $i++) {
    $a = [string]$args[$i]
    if ($a -eq '-Gateway') { $Gateway = [string]$args[++$i] }
    elseif ($a -eq '-Model') { $Model = [string]$args[++$i] }
    elseif ($a -eq '-MaxPromptTokens') { $MaxPromptTokens = [int]$args[++$i] }
    elseif ($a -eq '-MaxOutputTokens') { $MaxOutputTokens = [int]$args[++$i] }
    elseif ($a -eq '-Online') { $Offline = $false }
    else { $passThru.Add($a) }
}

$root = Split-Path -Parent $PSScriptRoot
$copilot = Join-Path $root 'tools\copilot-cli\node_modules\.bin\copilot.cmd'
if (-not (Test-Path $copilot)) {
    throw "Copilot CLI bulunamadı. Kurulum: npm install @github/copilot@1.0.105 --prefix `"$(Join-Path $root 'tools\copilot-cli')`""
}

$gatewayUrl = $Gateway.TrimEnd('/')
$copilotHome = Join-Path $root 'tools\copilot-cli\home'   # $home PowerShell'de salt okunur otomatik değişkendir
New-Item -ItemType Directory -Force $copilotHome | Out-Null

# MCP: gateway'in birleştirdiği tüm MCP hedefleri tek "gw" sunucusu olarak görünür.
$mcpConfig = @{ mcpServers = @{ gw = @{ type = 'http'; url = "$gatewayUrl/mcp"; tools = @('*') } } } |
    ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $copilotHome 'mcp-config.json'), $mcpConfig)   # BOM'suz UTF-8

$env:COPILOT_HOME = $copilotHome
$env:COPILOT_OFFLINE = if ($Offline) { 'true' } else { 'false' }
$env:COPILOT_PROVIDER_BASE_URL = "$gatewayUrl/v1"
$env:COPILOT_PROVIDER_TYPE = 'openai'
$env:COPILOT_PROVIDER_WIRE_API = 'completions'
$env:COPILOT_MODEL = $Model
$env:COPILOT_PROVIDER_MAX_PROMPT_TOKENS = "$MaxPromptTokens"
$env:COPILOT_PROVIDER_MAX_OUTPUT_TOKENS = "$MaxOutputTokens"

Write-Host "Copilot CLI -> model: $gatewayUrl/v1 ($Model), MCP: $gatewayUrl/mcp, offline: $Offline" -ForegroundColor Cyan
# PowerShell 5.1: native komut stderr'e yazınca 'Stop' betiği keser (NativeCommandError); sonuç çıkış kodundan okunur.
$ErrorActionPreference = 'Continue'
# --no-auto-update: sabitlenmiş sürüm (1.0.105) kendiliğinden yükseltilmesin.
& $copilot --no-auto-update @passThru
exit $LASTEXITCODE
