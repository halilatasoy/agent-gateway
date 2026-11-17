{{/* Ortak etiketler */}}
{{- define "agentgateway-stack.labels" -}}
app.kubernetes.io/name: agentgateway-stack
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" }}
{{- end }}

{{/* Gateway'e bağlanan kaynaklar için parentRef */}}
{{- define "agentgateway-stack.parentRef" -}}
- group: gateway.networking.k8s.io
  kind: Gateway
  name: {{ .Values.gateway.name }}
  sectionName: http
{{- end }}

{{/* Demo MCP sunucusu dahil tüm MCP hedefleri */}}
{{- define "agentgateway-stack.mcpTargets" -}}
{{- $targets := list -}}
{{- if .Values.mcp.demoServer.enabled -}}
{{- $targets = append $targets (dict "name" "everything" "host" (printf "%s.%s.svc.cluster.local" .Values.mcp.demoServer.name .Release.Namespace) "port" 80 "path" "/mcp" "protocol" "StreamableHTTP") -}}
{{- end -}}
{{- range .Values.mcp.targets -}}
{{- $targets = append $targets . -}}
{{- end -}}
{{- toYaml $targets -}}
{{- end }}
