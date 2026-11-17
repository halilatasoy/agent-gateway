# Chart'ları bir OCI registry'ye (Harbor) yayınlar:
#   - agentgateway-stack (bu depo)                -> paketlenip gönderilir
#   - agentgateway-crds, agentgateway (upstream)  -> çekilip aynı projeye aynalanır
# Yerelde install-platform.ps1 çağırır; şirkette CI hattında aynı betik kullanılabilir.
param(
    [Parameter(Mandatory)] [string]$Registry,          # örn. harbor.sirket.local
    [string]$Project = 'agentgateway',
    [Parameter(Mandatory)] [string]$Username,          # CI'da push yetkili robot hesabı
    [Parameter(Mandatory)] [string]$Password,
    [string]$CaFile,                                   # kurum CA'sı sistemde güvenilir değilse
    [string]$AgentgatewayVersion = 'v1.7.0',
    [switch]$SkipUpstream,
    # Yalnızca CRL/OCSP adresi olmayan yerel CA için: Windows curl.exe (Schannel) iptal kontrolünü kapatır.
    [switch]$NoRevocationCheck
)
# PowerShell 5.1: helm stderr'e yazdığında 'Stop' betiği keser; her komut $LASTEXITCODE ile kontrol edilir.
$ErrorActionPreference = 'Continue'
# Bu depodaki chart'lar: yapılandırma katmanı + izleme paneli
$chartsRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'charts'
$localCharts = @('agentgateway-stack', 'agentgateway-console')
$tls = @(); if ($CaFile) { $tls = @('--ca-file', $CaFile) }
$work = Join-Path $env:TEMP "agw-charts-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
New-Item -ItemType Directory -Force $work | Out-Null

function Assert-Exit([string]$What) { if ($LASTEXITCODE -ne 0) { throw "$What başarısız" } }

# PowerShell 5.1 (.NET Framework): konsol giriş kodlaması BOM'lu UTF-8 ise hem pipe ('pw' | helm ...) hem
# Process.StandardInput, stdin'e önce UTF-8 BOM yazar; Harbor parolayı BOM ile alıp 401 döner.
# Çözüm: süreç başlarken [Console]::InputEncoding geçici olarak BOM'suz UTF-8 yapılır. Parola komut satırında görünmez.
function Invoke-HelmLogin([string]$Registry, [string]$Username, [string]$Password, [string[]]$Extra) {
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = (Get-Command helm).Source
    $psi.Arguments = (@('registry', 'login', $Registry, '-u', $Username, '--password-stdin') + $Extra |
        ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }) -join ' '
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $origEncoding = [Console]::InputEncoding
    try {
        [Console]::InputEncoding = New-Object Text.UTF8Encoding $false
        $p = [Diagnostics.Process]::Start($psi)
        $p.StandardInput.Write($Password)
        $p.StandardInput.Close()
        $p.WaitForExit()
    } finally {
        [Console]::InputEncoding = $origEncoding
    }
    $global:LASTEXITCODE = $p.ExitCode
}

try {
    Write-Host "==> $Registry oturum açılıyor ($Username)"
    Invoke-HelmLogin $Registry $Username $Password $tls
    Assert-Exit 'helm registry login'

    foreach ($chart in $localCharts) {
        Write-Host "==> $chart paketleniyor"
        helm package (Join-Path $chartsRoot $chart) -d $work
        Assert-Exit "helm package $chart"
    }

    if (-not $SkipUpstream) {
        foreach ($name in 'agentgateway-crds', 'agentgateway') {
            Write-Host "==> upstream $name $AgentgatewayVersion çekiliyor"
            helm pull "oci://cr.agentgateway.dev/charts/$name" --version $AgentgatewayVersion -d $work
            Assert-Exit "helm pull $name"
        }
    }

    foreach ($pkg in Get-ChildItem $work -Filter '*.tgz') {
        Write-Host "==> gönderiliyor: $($pkg.Name) -> oci://$Registry/$Project"
        helm push $pkg.FullName "oci://$Registry/$Project" @tls
        Assert-Exit "helm push $($pkg.Name)"

        # Rancher (2.15) OCI etiketini semver'e normalleştirir: 'v1.7.0' için '1.7.0' etiketini arar ve bulamaz.
        # Sürümü 'v' ile başlayan chart'lara Harbor'da aynı artefakta normalleştirilmiş ikinci etiket eklenir.
        $meta = helm show chart $pkg.FullName
        $name = (($meta | Select-String '^name:\s*(.+)$').Matches[0].Groups[1].Value).Trim().Trim('"')
        $version = (($meta | Select-String '^version:\s*(.+)$').Matches[0].Groups[1].Value).Trim().Trim('"')
        if ($version -like 'v*') {
            $normalized = $version.Substring(1)
            # JSON BOM'suz bir dosyadan verilir: PowerShell 5.1 argümanlardaki çift tırnakları bozar, pipe ise BOM ekler.
            $body = [IO.Path]::GetTempFileName()
            [IO.File]::WriteAllText($body, "{`"name`":`"$normalized`"}")
            $curlArgs = @('-s', '-o', 'NUL', '-w', '%{http_code}', '-u', "${Username}:${Password}", '-X', 'POST',
                '-H', 'content-type: application/json', '--data-binary', "@$body",
                "https://$Registry/api/v2.0/projects/$Project/repositories/$name/artifacts/$version/tags")
            if ($CaFile) { $curlArgs = @('--cacert', $CaFile) + $curlArgs }
            if ($NoRevocationCheck) { $curlArgs = @('--ssl-no-revoke') + $curlArgs }
            try { $code = & curl.exe @curlArgs } finally { Remove-Item $body -ErrorAction SilentlyContinue }
            if ($code -notin '201', '409') { throw "Harbor'da $name`:$normalized etiketi eklenemedi (HTTP $code)" }
            Write-Host "    Rancher uyumluluğu: $name`:$version -> ek etiket $normalized (HTTP $code)"
        }
    }
} finally {
    # Helm kimlik bilgisini Docker credential helper'ına (Windows'ta Kimlik Bilgileri Yöneticisi) yazar; iş bitince sil.
    helm registry logout $Registry 2>$null | Out-Null
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
