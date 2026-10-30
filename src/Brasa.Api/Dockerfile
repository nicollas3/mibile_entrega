# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0.403 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Brasa.Domain/ src/Brasa.Domain/
COPY src/Brasa.Contracts/ src/Brasa.Contracts/
COPY src/Brasa.Infrastructure/ src/Brasa.Infrastructure/
COPY src/Brasa.Api/ src/Brasa.Api/
# A CA da sessão, quando necessária, existe só neste RUN. Nunca desative TLS.
RUN --mount=type=secret,id=proxy_ca \
    if [ -s /run/secrets/proxy_ca ]; then cat /etc/ssl/certs/ca-certificates.crt /run/secrets/proxy_ca > /tmp/brasa-ca.pem; export SSL_CERT_FILE=/tmp/brasa-ca.pem; fi; \
    dotnet restore src/Brasa.Api --locked-mode && \
    dotnet publish src/Brasa.Api -c Release --no-restore -o /out; \
    result=$?; rm -f /tmp/brasa-ca.pem; exit "$result"
FROM mcr.microsoft.com/dotnet/aspnet:10.0.14
WORKDIR /app
COPY --from=build /out .
RUN mkdir -p /app/.local/mail && chown -R $APP_UID /app/.local
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet","Brasa.Api.dll"]
