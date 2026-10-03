# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/Tamiza.Api/Tamiza.Api.csproj src/Tamiza.Api/
COPY src/dbUp/Tamiza.DbUp.csproj src/dbUp/
RUN dotnet restore src/Tamiza.Api/Tamiza.Api.csproj
COPY src/ src/
RUN dotnet publish src/Tamiza.Api/Tamiza.Api.csproj --configuration Release --no-restore --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# curl is used by the Compose healthcheck.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /var/lib/tamiza/keys \
    && chown app:app /var/lib/tamiza/keys
WORKDIR /app
COPY --from=build /app .
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Tamiza.Api.dll"]
