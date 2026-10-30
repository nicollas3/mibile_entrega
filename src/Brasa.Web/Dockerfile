# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0.403 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Brasa.Contracts/ src/Brasa.Contracts/
COPY src/Brasa.Client/ src/Brasa.Client/
COPY src/Brasa.Web/ src/Brasa.Web/
RUN --mount=type=secret,id=proxy_ca \
    if [ -s /run/secrets/proxy_ca ]; then cat /etc/ssl/certs/ca-certificates.crt /run/secrets/proxy_ca > /tmp/brasa-ca.pem; export SSL_CERT_FILE=/tmp/brasa-ca.pem; fi; \
    dotnet restore src/Brasa.Web --locked-mode && \
    dotnet publish src/Brasa.Web -c Release --no-restore -o /out; \
    result=$?; rm -f /tmp/brasa-ca.pem; exit "$result"
FROM mcr.microsoft.com/dotnet/aspnet:10.0.14
WORKDIR /app
COPY --from=build /out .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet","Brasa.Web.dll"]
