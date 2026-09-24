FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src

COPY backend/Portal.Domain/Portal.Domain.csproj backend/Portal.Domain/
COPY backend/Portal.Application/Portal.Application.csproj backend/Portal.Application/
COPY backend/Portal.Infrastructure/Portal.Infrastructure.csproj backend/Portal.Infrastructure/
COPY backend/Portal.Api/Portal.Api.csproj backend/Portal.Api/
COPY frontend/public/logo-horizontal.png frontend/public/logo-horizontal.png
RUN dotnet restore backend/Portal.Api/Portal.Api.csproj

FROM restore AS build
COPY backend/ backend/
RUN dotnet publish backend/Portal.Api/Portal.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM build AS migrate
COPY .config/dotnet-tools.json .config/dotnet-tools.json
RUN dotnet tool restore
ENTRYPOINT ["dotnet", "tool", "run", "dotnet-ef"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0

RUN mkdir -p /home/app/.aspnet/DataProtection-Keys \
    && chown -R "$APP_UID:$APP_UID" /home/app/.aspnet

COPY --from=build --chown=$APP_UID:$APP_UID /app/publish/ ./

EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Portal.Api.dll"]
