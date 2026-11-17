# agentgateway'i standalone modda başlatır.
#   LLM : http://localhost:4000/v1/chat/completions
#   MCP : http://localhost:4000/mcp
#   UI  : http://localhost:4000/ui/
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'bin\agentgateway.exe'
$config = Join-Path $root 'standalone\config.yaml'

if (-not (Test-Path $exe)) {
    throw "agentgateway.exe bulunamadı: $exe. README'deki indirme adımını uygulayın."
}

# MCP oturum anahtarı git dışında tutulur. SESSION_KEY verilince gateway anahtarı
# config.yaml'a yazmaz; dosya yoksa ilk çalıştırmada rastgele üretilir.
$keyFile = Join-Path $root 'standalone\.session-key'
if (-not (Test-Path $keyFile)) {
    $bytes = New-Object byte[] 32
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $hex = -join ($bytes | ForEach-Object { $_.ToString('x2') })
    [IO.File]::WriteAllText($keyFile, $hex)
}
$env:SESSION_KEY = ([IO.File]::ReadAllText($keyFile)).Trim()

& $exe -f $config --validate-only
if ($LASTEXITCODE -ne 0) { throw 'Yapılandırma geçersiz, gateway başlatılmadı.' }

Write-Host 'agentgateway başlatılıyor... UI: http://localhost:4000/ui/'
& $exe -f $config
