# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY backend/Projeto-Campeonato.Api/Projeto-Campeonato.Api.csproj backend/Projeto-Campeonato.Api/
# Optional cloud proxy CA; normal local builds use the system CA bundle.
RUN --mount=type=secret,id=proxy_ca \
    if [ -f /run/secrets/proxy_ca ]; then export SSL_CERT_FILE=/run/secrets/proxy_ca; fi && \
    dotnet restore backend/Projeto-Campeonato.Api
COPY backend/Projeto-Campeonato.Api/ backend/Projeto-Campeonato.Api/
RUN dotnet publish backend/Projeto-Campeonato.Api -c Release --no-restore -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Projeto-Campeonato.Api.dll"]
