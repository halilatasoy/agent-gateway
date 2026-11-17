# Yerel "platform" kurulumu: şirketteki Rancher + Harbor düzeninin küçük bir kopyası.
#   1) cert-manager            2) yerel CA + sertifikalar     3) CoreDNS yönlendirmesi
#   4) Harbor (registry)       5) Rancher Manager (arayüz)    6) Harbor projesi + robot hesabı
#   7) chart'ları Harbor'a yayınla                            8) Harbor'ı Rancher'a OCI repository olarak ekle
# Önkoşul: k8s\setup-local-cluster.ps1 ile Rancher Desktop cluster'ı hazır; kubectl ve helm PATH'te.
# Tüm adımlar tekrar çalıştırılabilir (idempotent).
param(
    # *.127.0.0.1.sslip.io -> 127.0.0.1 (Rancher Desktop, Traefik'in 443 portunu localhost'a yönlendirir).
    [string]$Domain = '127.0.0.1.sslip.io',
    [string]$CertManagerVersion = 'v1.21.3',
    [string]$HarborChartVersion = '1.19.4',
    [string]$RancherVersion = '2.15.3',
    [string]$HarborProject = 'agentgateway'
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$localDir = Join-Path $here '.local'          # git'e girmez: yerel CA sertifikası
New-Item -ItemType Directory -Force $localDir | Out-Null
$caFile = Join-Path $localDir 'local-ca.crt'
$harborHost = "harbor.$Domain"
$rancherHost = "rancher.$Domain"
$harborUrl = "https://$harborHost"

function Invoke-Step([string]$Title, [scriptblock]$Action) {
    Write-Host "==> $Title" -ForegroundColor Cyan
    # PowerShell 5.1: native komut stderr'e yazınca 'Stop' betiği keser; adım içinde 'Continue'.
    $ErrorActionPreference = 'Continue'
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "Adım başarısız: $Title" }
}

function Assert-Exit([string]$What) { if ($LASTEXITCODE -ne 0) { throw "$What başarısız (çıkış kodu $LASTEXITCODE)" } }

function New-RandomPassword([int]$Length = 24) {
    $chars = [char[]]'abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789'
    $bytes = New-Object byte[] $Length
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    # Harbor parola kuralı: en az bir büyük harf, bir küçük harf ve bir rakam.
    'Aa1' + (-join ($bytes | ForEach-Object { $chars[$_ % $chars.Length] }))
}

function Get-SecretValue([string]$Namespace, [string]$Name, [string]$Key) {
    $b64 = kubectl get secret $Name -n $Namespace -o "jsonpath={.data.$($Key.Replace('.', '\.'))}"
    Assert-Exit "secret $Namespace/$Name okuma"
    [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($b64))
}

function Test-SecretExists([string]$Namespace, [string]$Name) {
    $found = kubectl get secret $Name -n $Namespace --ignore-not-found -o name
    [bool]$found
}

function Invoke-HarborApi([string]$Method, [string]$Path, [string]$Json, [string]$User, [string]$Pass) {
    # curl.exe + dosya: PowerShell 5.1 native argümanlardaki çift tırnakları bozduğu için JSON dosyadan gönderilir.
    $resp = [IO.Path]::GetTempFileName()
    # --ssl-no-revoke: Windows curl.exe (Schannel) iptal kontrolü yapar; yerel CA'nın CRL/OCSP adresi
    # olmadığı için bu kontrol başarısız olur. Kurumsal CA'da (CRL adresi olan) bu bayrağa gerek yoktur.
    $curlArgs = @('-s', '--ssl-no-revoke', '-o', $resp, '-w', '%{http_code}', '--cacert', $caFile, '-u', "${User}:${Pass}",
        '-X', $Method, "$harborUrl$Path", '-H', 'content-type: application/json')
    $bodyFile = $null
    if ($Json) {
        $bodyFile = [IO.Path]::GetTempFileName()
        [IO.File]::WriteAllText($bodyFile, $Json)
        $curlArgs += @('--data-binary', "@$bodyFile")
    }
    $code = & curl.exe @curlArgs
    $text = Get-Content $resp -Raw
    Remove-Item $resp -ErrorAction SilentlyContinue
    if ($bodyFile) { Remove-Item $bodyFile -ErrorAction SilentlyContinue }
    [pscustomobject]@{ Code = [int]$code; Body = $text }
}

function Wait-Until([string]$What, [int]$TimeoutSec, [scriptblock]$Condition) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (& $Condition) { return }
        Start-Sleep -Seconds 5
    }
    throw "Zaman aşımı: $What"
}

Write-Host "Cluster context: $(kubectl config current-context)"

# --- 1) cert-manager -------------------------------------------------------------------
Invoke-Step "cert-manager $CertManagerVersion" {
    helm upgrade -i cert-manager cert-manager --repo https://charts.jetstack.io --version $CertManagerVersion `
        -n cert-manager --create-namespace --set crds.enabled=true --wait --timeout 5m
}

# --- 2) Yerel CA + sertifikalar --------------------------------------------------------
Invoke-Step 'Yerel CA (cert-manager ClusterIssuer: local-ca)' {
    kubectl apply -f (Join-Path $here 'manifests\local-ca.yaml'); Assert-Exit 'local-ca apply'
    kubectl wait --for=condition=Ready certificate/local-ca -n cert-manager --timeout=120s
}
Invoke-Step 'Harbor ve Rancher sertifikaları' {
    foreach ($ns in 'harbor', 'cattle-system') {
        kubectl create namespace $ns --dry-run=client -o yaml | kubectl apply -f -; Assert-Exit "namespace $ns"
    }
    @"
apiVersion: cert-manager.io/v1
kind: Certificate
metadata:
  name: harbor-tls
  namespace: harbor
spec:
  secretName: harbor-tls
  dnsNames: ["$harborHost"]
  issuerRef: { name: local-ca, kind: ClusterIssuer, group: cert-manager.io }
---
apiVersion: cert-manager.io/v1
kind: Certificate
metadata:
  name: tls-rancher-ingress
  namespace: cattle-system
spec:
  secretName: tls-rancher-ingress
  dnsNames: ["$rancherHost"]
  issuerRef: { name: local-ca, kind: ClusterIssuer, group: cert-manager.io }
"@ | kubectl apply -f -
    Assert-Exit 'certificate apply'
    kubectl wait --for=condition=Ready certificate/harbor-tls -n harbor --timeout=120s; Assert-Exit 'harbor-tls'
    kubectl wait --for=condition=Ready certificate/tls-rancher-ingress -n cattle-system --timeout=120s
}
Invoke-Step "CA sertifikası dışa aktarılıyor -> $caFile" {
    $pem = Get-SecretValue 'cert-manager' 'local-ca' 'tls.crt'
    [IO.File]::WriteAllText($caFile, $pem)
    kubectl create secret generic tls-ca -n cattle-system --from-file="cacerts.pem=$caFile" `
        --dry-run=client -o yaml | kubectl apply -f -
}

# --- 3) CoreDNS: cluster içinden de aynı hostname'ler Traefik'e gitsin ------------------
Invoke-Step "CoreDNS yönlendirmesi (*.$Domain -> Traefik)" {
    @"
apiVersion: v1
kind: ConfigMap
metadata:
  name: coredns-custom
  namespace: kube-system
data:
  # Pod içinden $harborHost 127.0.0.1'e (pod'un kendisine) çözülürdü; Traefik servisine yönlendir.
  local-platform.override: |
    rewrite name exact $harborHost traefik.kube-system.svc.cluster.local answer auto
    rewrite name exact $rancherHost traefik.kube-system.svc.cluster.local answer auto
"@ | kubectl apply -f -
    Assert-Exit 'coredns-custom apply'
    $traefikIp = kubectl get svc traefik -n kube-system -o 'jsonpath={.spec.clusterIP}'
    Write-Host "Traefik ClusterIP: $traefikIp; CoreDNS yeniden yüklemesi bekleniyor (en fazla ~2 dk)..."
    Wait-Until 'CoreDNS yönlendirmesi' 180 {
        $out = kubectl run dns-check --rm -i --restart=Never --quiet --image=curlimages/curl:8.11.1 `
            --command -- sh -c "getent hosts $harborHost" 2>$null
        "$out" -match [regex]::Escape($traefikIp)
    }
    Write-Host "Pod içinden $harborHost -> $traefikIp"
    $global:LASTEXITCODE = 0
}

# --- 4) Harbor ---------------------------------------------------------------------------
Invoke-Step "Harbor (chart $HarborChartVersion)" {
    if (-not (Test-SecretExists 'harbor' 'harbor-admin')) {
        kubectl create secret generic harbor-admin -n harbor --from-literal="HARBOR_ADMIN_PASSWORD=$(New-RandomPassword)"
        Assert-Exit 'harbor-admin secret'
    }
    helm upgrade -i harbor harbor --repo https://helm.goharbor.io --version $HarborChartVersion -n harbor `
        -f (Join-Path $here 'values\harbor-local.yaml') `
        --set "expose.ingress.hosts.core=$harborHost" --set "externalURL=$harborUrl" `
        --wait --timeout 15m
}

# --- 5) Rancher Manager ------------------------------------------------------------------
Invoke-Step "Rancher Manager $RancherVersion" {
    helm upgrade -i rancher rancher --repo https://releases.rancher.com/server-charts/stable --version $RancherVersion `
        -n cattle-system -f (Join-Path $here 'values\rancher-local.yaml') --set "hostname=$rancherHost" `
        --wait --timeout 15m
}

# --- 6) Harbor projesi + Rancher için salt-okunur robot hesabı -----------------------------
$harborPw = Get-SecretValue 'harbor' 'harbor-admin' 'HARBOR_ADMIN_PASSWORD'
Invoke-Step "Harbor projesi: $HarborProject" {
    Wait-Until 'Harbor API' 300 { (Invoke-HarborApi GET '/api/v2.0/ping' $null 'admin' $harborPw).Code -eq 200 }
    $r = Invoke-HarborApi POST '/api/v2.0/projects' "{`"project_name`":`"$HarborProject`",`"public`":false}" 'admin' $harborPw
    if ($r.Code -notin 201, 409) { throw "Proje oluşturulamadı: HTTP $($r.Code) $($r.Body)" }
    Write-Host $(if ($r.Code -eq 201) { 'Proje oluşturuldu.' } else { 'Proje zaten var.' })
    $global:LASTEXITCODE = 0
}
Invoke-Step 'Harbor robot hesabı (Rancher, yalnızca pull)' {
    if (Test-SecretExists 'cattle-system' 'harbor-robot-rancher') { Write-Host 'Robot Secret zaten var.'; return }
    # Secret yoksa eski robotu silip yeniden oluştur (robot parolası yalnızca oluşturulurken gösterilir).
    $list = Invoke-HarborApi GET '/api/v2.0/robots?page_size=100' $null 'admin' $harborPw
    # PS 5.1: ConvertFrom-Json diziyi pipeline'a tek nesne olarak verir; değişken üzerinden dolaşılır.
    $existing = $list.Body | ConvertFrom-Json
    foreach ($old in $existing) {
        if ($old.name -like "*$HarborProject+rancher") {
            Invoke-HarborApi DELETE "/api/v2.0/robots/$($old.id)" $null 'admin' $harborPw | Out-Null
        }
    }
    $body = "{`"name`":`"rancher`",`"description`":`"Rancher ClusterRepo (salt okunur)`",`"duration`":-1,`"level`":`"project`",`"disable`":false," +
        "`"permissions`":[{`"kind`":`"project`",`"namespace`":`"$HarborProject`",`"access`":[{`"resource`":`"repository`",`"action`":`"pull`"},{`"resource`":`"repository`",`"action`":`"list`"}]}]}"
    $r = Invoke-HarborApi POST '/api/v2.0/robots' $body 'admin' $harborPw
    if ($r.Code -ne 201) { throw "Robot oluşturulamadı: HTTP $($r.Code) $($r.Body)" }
    $robot = $r.Body | ConvertFrom-Json
    kubectl create secret generic harbor-robot-rancher -n cattle-system --type=kubernetes.io/basic-auth `
        --from-literal="username=$($robot.name)" --from-literal="password=$($robot.secret)"
    Assert-Exit 'robot secret'
    Write-Host "Robot oluşturuldu: $($robot.name)"
}

# --- 7) Chart'ları Harbor'a yayınla ---------------------------------------------------------
Invoke-Step "Chart'lar Harbor'a yayınlanıyor (oci://$harborHost/$HarborProject)" {
    & (Join-Path $here 'publish-charts.ps1') -Registry $harborHost -Project $HarborProject `
        -Username admin -Password $harborPw -CaFile $caFile -NoRevocationCheck
}

# --- 8) Harbor'ı Rancher'a OCI repository olarak ekle ---------------------------------------
Invoke-Step 'Rancher ClusterRepo (Harbor OCI)' {
    $robotUser = Get-SecretValue 'cattle-system' 'harbor-robot-rancher' 'username'
    $robotPass = Get-SecretValue 'cattle-system' 'harbor-robot-rancher' 'password'
    # Proje düzeyinde tek repo için Harbor'ın katalog API'si (/v2/_catalog) robot hesaba açık olmalı;
    # değilse her chart için ayrı ClusterRepo tanımlanır.
    $catalog = Invoke-HarborApi GET '/v2/_catalog' $null $robotUser $robotPass
    $useCatalog = $catalog.Code -eq 200 -and $catalog.Body -match [regex]::Escape("$HarborProject/agentgateway-stack")
    Write-Host "Harbor katalog API (robot): HTTP $($catalog.Code) -> $(if ($useCatalog) { 'tek repo' } else { 'chart başına repo' })"
    $repos = if ($useCatalog) { @{ 'harbor-agentgateway' = "oci://$harborHost/$HarborProject" } } else {
        @{
            'harbor-agentgateway-stack' = "oci://$harborHost/$HarborProject/agentgateway-stack"
            'harbor-agentgateway-console' = "oci://$harborHost/$HarborProject/agentgateway-console"
            'harbor-agentgateway'       = "oci://$harborHost/$HarborProject/agentgateway"
            'harbor-agentgateway-crds'  = "oci://$harborHost/$HarborProject/agentgateway-crds"
        }
    }
    # Dikkat: Rancher 2.15'in OCI istemcisi caBundle'ı x509.ParseCertificate ile okur, yani DER bekler
    # (alan açıklamasında PEM yazmasına rağmen). PEM gövdesi zaten DER'in base64 hâlidir.
    $caB64 = ((Get-Content $caFile) | Where-Object { $_ -notmatch '^-----' }) -join ''
    foreach ($name in $repos.Keys) {
        @"
apiVersion: catalog.cattle.io/v1
kind: ClusterRepo
metadata:
  name: $name
spec:
  url: $($repos[$name])
  caBundle: $caB64
  clientSecret:
    name: harbor-robot-rancher
    namespace: cattle-system
"@ | kubectl apply -f -
        Assert-Exit "ClusterRepo $name"
    }
    foreach ($name in $repos.Keys) {
        # OCI repo'larda koşul tipi 'OCIDownloaded', HTTP repo'larda 'Downloaded'. jsonpath filtresinde çift tırnak
        # kullanılmaz: PowerShell 5.1 native argümanlardaki iç çift tırnakları siler.
        Wait-Until "ClusterRepo $name indirildi" 180 {
            $conds = kubectl get clusterrepo $name -o 'jsonpath={range .status.conditions[*]}{.type}={.status};{end}'
            "$conds" -match '(^|;)(OCI)?Downloaded=True'
        }
        Write-Host "ClusterRepo $name hazır."
    }
    $global:LASTEXITCODE = 0
}

Write-Host ''
Write-Host '================ Platform hazır ================' -ForegroundColor Green
Write-Host "Rancher : https://$rancherHost"
Write-Host "  İlk giriş parolası: kubectl get secret bootstrap-secret -n cattle-system -o go-template='{{.data.bootstrapPassword|base64decode}}'"
Write-Host "Harbor  : $harborUrl   (kullanıcı: admin)"
Write-Host "  Parola: kubectl get secret harbor-admin -n harbor -o go-template='{{.data.HARBOR_ADMIN_PASSWORD|base64decode}}'"
Write-Host "Yerel CA: $caFile"
Write-Host '  Tarayıcı uyarısını kaldırmak için (isteğe bağlı): certutil -user -addstore Root "<yukarıdaki dosya>"'
