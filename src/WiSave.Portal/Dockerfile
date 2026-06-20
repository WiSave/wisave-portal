## syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

ARG GITHUB_PACKAGES_USERNAME=JacobChwastek

COPY NuGet.Config ./
COPY Directory.Packages.props ./
COPY src/WiSave.Portal/WiSave.Portal.csproj src/WiSave.Portal/
COPY src/WiSave.Portal.Contracts/WiSave.Portal.Contracts.csproj src/WiSave.Portal.Contracts/
COPY src/WiSave.Portal.Migrations/WiSave.Portal.Migrations.csproj src/WiSave.Portal.Migrations/
RUN --mount=type=secret,id=github_packages_token \
    dotnet nuget update source wisave \
        --username "${GITHUB_PACKAGES_USERNAME}" \
        --password "$(cat /run/secrets/github_packages_token)" \
        --store-password-in-clear-text \
        --configfile NuGet.Config && \
    dotnet restore src/WiSave.Portal/WiSave.Portal.csproj --configfile NuGet.Config

# Copy everything and publish
COPY src/ src/
RUN dotnet publish src/WiSave.Portal/WiSave.Portal.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "WiSave.Portal.dll"]
