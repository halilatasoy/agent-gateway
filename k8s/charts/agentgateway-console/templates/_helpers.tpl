{{- define "agc.fullname" -}}
{{- .Release.Name | trunc 63 | trimSuffix "-" -}}
{{- end }}

{{- define "agc.labels" -}}
app.kubernetes.io/name: agentgateway-console
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" }}
{{- end }}

{{- define "agc.selector" -}}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/component: {{ .component }}
{{- end }}

{{- define "agc.postgresName" -}}
{{- printf "%s-postgres" (include "agc.fullname" .) -}}
{{- end }}

{{- define "agc.dbSecretName" -}}
{{- printf "%s-db" (include "agc.fullname" .) -}}
{{- end }}
