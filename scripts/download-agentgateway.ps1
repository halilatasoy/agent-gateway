# agentgateway Windows binary'lerini GitHub release'inden indirir ve SHA256 ile doğrular.
# Gereksinim: gh CLI (oturum açık).
param([string]$Version = 'v1.7.0')
$ErrorActionPreference = 'Stop'
$bin = Join-Path (Split-Path -Parent $PSScriptRoot) 'bin'
New-Item -ItemType Directory -Force $bin | Out-Null

gh release download $Version -R agentgateway/agentgateway -D $bin --clobber `
    -p 'agentgateway-windows-amd64.exe*' -p 'agctl-windows-amd64.exe*'
if ($LASTEXITCODE -ne 0) { throw 'İndirme başarısız.' }

foreach ($name in 'agentgateway', 'agctl') {
    $file = Join-Path $bin "$name-windows-amd64.exe"
    $expected = ((Get-Content "$file.sha256" -Raw).Trim() -split '\s+')[0]
    $actual = (Get-FileHash $file -Algorithm SHA256).Hash
    if ($expected -ne $actual) { throw "Checksum uyuşmuyor: $file" }
    Move-Item -Force $file (Join-Path $bin "$name.exe")
    Remove-Item "$file.sha256"
    Write-Host "Doğrulandı: $name.exe ($Version)"
}
