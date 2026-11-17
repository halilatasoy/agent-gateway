# Yerel Kubernetes ortamını (WSL2 + Rancher Desktop) kurar ve agentgateway'i yükler.
# Betik kaldığı yerden devam eder; her çalıştırmada eksik olan ilk adımı yapar:
#   Adım 1  WSL2           -> YÖNETİCİ PowerShell gerekir, sonra bilgisayarı yeniden başlatın.
#   Adım 2  Rancher Desktop -> normal kullanıcı; yalnızca bu kullanıcıya kurulur (yönetici gerekmez).
#   Adım 3  Kubernetes      -> rdctl ile containerd + k3s başlatılır.
#   Adım 4  agentgateway    -> k8s\install.ps1
param(
    # v1.25.0 henüz 2 haftalık değil; 2 haftadan eski son sürüm kullanılıyor.
    [string]$RancherDesktopVersion = '1.24.0',
    # Sürüm verilmezse Rancher Desktop ilk açılışta çok eski bir sürüm (1.25) seçebiliyor;
    # chart ve Gateway API v1.6 için >=1.30 gerekir.
    [string]$KubernetesVersion = '1.36.6'
)
$ErrorActionPreference = 'Stop'
$env:WSL_UTF8 = '1'

# PowerShell 5.1: yerel bir komutun stderr çıktısı 'Stop' altında NativeCommandError olarak betiği keser
# (örn. WSL kurulu değilken 'wsl.exe --version'). Yerel komutlar bu sarmalayıcıyla çalışır;
# başarı $LASTEXITCODE ile kontrol edilir.
function Invoke-Native([scriptblock]$Command) {
    $ErrorActionPreference = 'Continue'
    & $Command
}

function Test-Admin {
    ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

# --- Adım 1: WSL2 ---------------------------------------------------------------
Invoke-Native { wsl.exe --version *> $null }
if ($LASTEXITCODE -ne 0) {
    if (-not (Test-Admin)) {
        Write-Host 'WSL kurulu değil. Bu adım yönetici yetkisi ister:' -ForegroundColor Yellow
        Write-Host '  Başlat > PowerShell > "Yönetici olarak çalıştır" ile açıp bu betiği tekrar çalıştırın.'
        exit 1
    }
    Write-Host '==> WSL2 kuruluyor (dağıtım olmadan)...'
    Invoke-Native { wsl.exe --install --no-distribution }
    if ($LASTEXITCODE -ne 0) { throw 'WSL kurulumu başarısız.' }
    Write-Host ''
    Write-Host 'WSL kuruldu. Bilgisayarı YENİDEN BAŞLATIN, sonra betiği normal PowerShell ile tekrar çalıştırın.' -ForegroundColor Green
    exit 0
}
Write-Host "WSL hazır: $(Invoke-Native { wsl.exe --version } | Select-Object -First 1)"

# --- Adım 2: Rancher Desktop (kullanıcıya özel) -----------------------------------
$rdRoot = Join-Path $env:LOCALAPPDATA 'Programs\Rancher Desktop'
$rdctl = Join-Path $rdRoot 'resources\resources\win32\bin\rdctl.exe'
if (-not (Test-Path $rdctl)) {
    $tmp = Join-Path $env:TEMP "rancher-desktop-$RancherDesktopVersion"
    New-Item -ItemType Directory -Force $tmp | Out-Null
    $msi = "Rancher.Desktop.Setup.$RancherDesktopVersion.msi"
    Write-Host "==> Rancher Desktop $RancherDesktopVersion indiriliyor..."
    Invoke-Native { gh release download "v$RancherDesktopVersion" -R rancher-sandbox/rancher-desktop -D $tmp --clobber -p $msi -p "$msi.sha512sum" }
    if ($LASTEXITCODE -ne 0) { throw 'İndirme başarısız (gh CLI oturumu açık mı?).' }

    $expected = ((Get-Content (Join-Path $tmp "$msi.sha512sum") -Raw).Trim() -split '\s+')[0]
    $actual = (Get-FileHash (Join-Path $tmp $msi) -Algorithm SHA512).Hash
    if ($expected -ne $actual) { throw "SHA512 uyuşmuyor: $msi" }
    Write-Host 'SHA512 doğrulandı.'

    Write-Host '==> Kuruluyor (yalnızca bu kullanıcı)...'
    $p = Start-Process msiexec.exe -Wait -PassThru -ArgumentList @(
        '/i', "`"$(Join-Path $tmp $msi)`"", '/qn', 'ALLUSERS=2', 'MSIINSTALLPERUSER=1',
        '/l*v', "`"$(Join-Path $tmp 'install.log')`"")
    if ($p.ExitCode -ne 0) { throw "msiexec çıkış kodu $($p.ExitCode). Log: $(Join-Path $tmp 'install.log')" }
    if (-not (Test-Path $rdctl)) { throw "rdctl bulunamadı: $rdctl" }
}

# --- Adım 3: Kubernetes ------------------------------------------------------------
Write-Host '==> Rancher Desktop başlatılıyor (containerd + Kubernetes); ilk açılış birkaç dakika sürebilir...'
Invoke-Native {
    # Not: --application.admin-access Windows'ta desteklenmez (rdctl hata verip uygulamayı başlatmaz).
    & $rdctl start --container-engine.name containerd --kubernetes.enabled --kubernetes.version $KubernetesVersion `
        --application.start-in-background --no-modal-dialogs `
        --wait --wait-timeout 20m
}
if ($LASTEXITCODE -ne 0) { Write-Host 'rdctl start uyarı verdi; Kubernetes hazır mı kontrol ediliyor...' -ForegroundColor Yellow }

$env:Path = "$(Split-Path $rdctl);$env:Path"   # Rancher Desktop'ın kubectl ve helm'i
Invoke-Native { kubectl config use-context rancher-desktop }
if ($LASTEXITCODE -ne 0) { throw 'rancher-desktop kubectl context bulunamadı.' }
Invoke-Native { kubectl wait --for=condition=Ready node --all --timeout=600s }
if ($LASTEXITCODE -ne 0) { throw 'Kubernetes düğümü hazır olmadı.' }

# --- Adım 4: agentgateway ----------------------------------------------------------
& (Join-Path $PSScriptRoot 'install.ps1')
