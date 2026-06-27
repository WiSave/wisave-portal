## syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

ARG GITHUB_PACKAGES_USERNAME=JacobChwastek
ARG BUILD_CONFIGURATION=Debug

COPY NuGet.Config ./
COPY Directory.Packages.props ./
COPY src/WiSave.Portal/WiSave.Portal.csproj src/WiSave.Portal/
COPY src/WiSave.Portal.Contracts/WiSave.Portal.Contracts.csproj src/WiSave.Portal.Contracts/
COPY src/WiSave.Portal.Migrations/WiSave.Portal.Migrations.csproj src/WiSave.Portal.Migrations/

RUN --mount=type=bind,from=wisave_expenses_contracts_package,source=.,target=/host-wisave-expenses-contracts,readonly \
    mkdir -p /root/.nuget/packages/wisave.expenses.contracts && \
    cp -a /host-wisave-expenses-contracts/. /root/.nuget/packages/wisave.expenses.contracts/

RUN --mount=type=bind,from=wisave_incomes_contracts_package,source=.,target=/host-wisave-incomes-contracts,readonly \
    mkdir -p /root/.nuget/packages/wisave.incomes.contracts && \
    cp -a /host-wisave-incomes-contracts/. /root/.nuget/packages/wisave.incomes.contracts/

RUN --mount=type=secret,id=github_packages_token,required=false \
    if [ -s /run/secrets/github_packages_token ]; then \
        dotnet nuget update source wisave \
            --username "${GITHUB_PACKAGES_USERNAME}" \
            --password "$(cat /run/secrets/github_packages_token)" \
            --store-password-in-clear-text \
            --configfile NuGet.Config; \
    fi && \
    dotnet restore src/WiSave.Portal/WiSave.Portal.csproj --configfile NuGet.Config

COPY src/ src/
RUN dotnet publish src/WiSave.Portal/WiSave.Portal.csproj -c "${BUILD_CONFIGURATION}" -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "WiSave.Portal.dll"]
