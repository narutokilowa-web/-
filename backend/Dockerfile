# Build context: repository root.  docker build -f backend/Dockerfile --target api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# Optional extra CA certificates (TLS-inspecting proxies). See deploy/certs/README.md.
COPY deploy/certs/ /usr/local/share/ca-certificates/extra/
RUN update-ca-certificates
WORKDIR /src
COPY backend/ backend/
COPY tools/ tools/
RUN dotnet restore backend/src/Mfop.Api/Mfop.Api.csproj \
 && dotnet restore backend/src/Mfop.Migrator/Mfop.Migrator.csproj
RUN dotnet publish backend/src/Mfop.Api/Mfop.Api.csproj -c Release -o /out/api --no-restore \
 && dotnet publish backend/src/Mfop.Migrator/Mfop.Migrator.csproj -c Release -o /out/migrator --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
COPY --from=build /out/api .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "Mfop.Api.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS migrator
WORKDIR /app
COPY --from=build /out/migrator .
USER app
ENTRYPOINT ["dotnet", "Mfop.Migrator.dll"]
