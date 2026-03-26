FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy both project files for restore
COPY src/WiSave.Portal/WiSave.Portal.csproj src/WiSave.Portal/
COPY src/WiSave.Portal.Migrations/WiSave.Portal.Migrations.csproj src/WiSave.Portal.Migrations/
RUN dotnet restore src/WiSave.Portal/WiSave.Portal.csproj

# Copy everything and publish
COPY src/ src/
RUN dotnet publish src/WiSave.Portal/WiSave.Portal.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "WiSave.Portal.dll"]
