# agentgateway'i Kubernetes'e uçtan uca kurar:
#   1) Gateway API CRD'leri  2) agentgateway-crds  3) agentgateway (controller)  4) agentgateway-stack (bu depo)
# Gereksinim: kubectl ve helm PATH'te, kubectl doğru cluster context'ini gösteriyor.
param(
    [string]$GatewayApiVersion = '1.6.3',
    [string]$AgentgatewayVersion = 'v1.7.0',
    [string]$Namespace = 'agentgateway-system',
    [string]$ValuesFile
)
$ErrorActionPreference = 'Stop'
$chart = Join-Path $PSScriptRoot 'charts\agentgateway-stack'

function Invoke-Step([string]$Title, [scriptblock]$Action) {
    Write-Host "==> $Title"
    # PowerShell 5.1: helm/kubectl stderr'e yazdığında (örn. helm'in "Pulled:" satırı) 'Stop' altında
    # NativeCommandError betiği keser. Adım içinde 'Continue'; başarı $LASTEXITCODE ile kontrol edilir.
    $ErrorActionPreference = 'Continue'
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "Adım başarısız: $Title" }
}

Invoke-Step 'Cluster bağlantısı' {
    Write-Host "Cluster context: $(kubectl config current-context)"
    kubectl cluster-info
}

Invoke-Step "Gateway API CRD'leri v$GatewayApiVersion" {
    # k3s/RKE2'de Traefik chart'ı (traefik-crd) Gateway API CRD'lerini zaten kurup sahiplenebilir.
    # Üzerine kubectl ile uygulamak alan sahipliği çakışmasına yol açar; sürüm yeterliyse adımı atla.
    $existing = kubectl get crd gateways.gateway.networking.k8s.io --ignore-not-found `
        -o jsonpath='{.metadata.annotations.gateway\.networking\.k8s\.io/bundle-version}'
    if ($LASTEXITCODE -ne 0) { return }
    if ($existing) {
        $owner = kubectl get crd gateways.gateway.networking.k8s.io `
            -o jsonpath='{.metadata.annotations.meta\.helm\.sh/release-name}'
        $ownerText = if ($owner) { "helm release: $owner" } else { 'helm dışı' }
        if ([version]($existing.TrimStart('v')) -ge [version]$GatewayApiVersion) {
            Write-Host "Gateway API $existing zaten kurulu ($ownerText); adım atlandı."
            return
        }
        throw "Kurulu Gateway API $existing, gereken v$GatewayApiVersion'dan eski ($ownerText). Önce sahibi üzerinden yükseltin."
    }
    kubectl apply --server-side -f "https://github.com/kubernetes-sigs/gateway-api/releases/download/v$GatewayApiVersion/standard-install.yaml"
}
Invoke-Step "agentgateway-crds $AgentgatewayVersion" {
    helm upgrade -i --create-namespace -n $Namespace --version $AgentgatewayVersion `
        agentgateway-crds oci://cr.agentgateway.dev/charts/agentgateway-crds
}
Invoke-Step "agentgateway controller $AgentgatewayVersion" {
    helm upgrade -i -n $Namespace --version $AgentgatewayVersion --wait --timeout 5m `
        agentgateway oci://cr.agentgateway.dev/charts/agentgateway
}
Invoke-Step 'GatewayClass kabul bekleniyor' {
    kubectl wait --for=condition=Accepted gatewayclass/agentgateway --timeout=120s
}

$stackArgs = @('upgrade', '-i', '-n', $Namespace, 'agentgateway-stack', $chart)
if ($ValuesFile) { $stackArgs += @('-f', $ValuesFile) }
Invoke-Step 'agentgateway-stack' { helm @stackArgs }

$gatewayName = 'agentgateway-proxy'
Invoke-Step 'Gateway hazır bekleniyor' {
    kubectl wait --for=condition=Programmed "gateway/$gatewayName" -n $Namespace --timeout=180s
}

Write-Host ''
kubectl get gateway,agentgatewaymodel,agentgatewaybackend,httproute,pods -n $Namespace
Write-Host ''
Write-Host "Test için: kubectl port-forward -n $Namespace deploy/$gatewayName 8080:8080"
