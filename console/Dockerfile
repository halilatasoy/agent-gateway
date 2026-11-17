# Agent Gateway İzleme Paneli (webds Dockerfile deseni)
# Derleme: nerdctl --namespace k8s.io build -t agentgateway-console:0.1.0 console/

# --- Derleme aşaması ---
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Önce yalnızca proje dosyası: restore katmanı önbellekte kalsın
COPY ["AgentGatewayConsole.Web/AgentGatewayConsole.Web.csproj", "AgentGatewayConsole.Web/"]
RUN dotnet restore "AgentGatewayConsole.Web/AgentGatewayConsole.Web.csproj"

COPY AgentGatewayConsole.Web/ AgentGatewayConsole.Web/
RUN dotnet publish "AgentGatewayConsole.Web/AgentGatewayConsole.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

# --- Çalışma aşaması ---
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# 8080: arayüz (Ingress arkasında) · 4318: OTLP/HTTP alıcısı (yalnızca cluster içi)
EXPOSE 8080 4318

# Güvenlik: aspnet imajındaki root olmayan kullanıcı. Sayısal UID ($APP_UID=1654) kullanılır;
# isimle ("app") verilirse Kubernetes runAsNonRoot kontrolünü doğrulayamaz.
USER $APP_UID

ENTRYPOINT ["dotnet", "AgentGatewayConsole.Web.dll"]
