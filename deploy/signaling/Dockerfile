# Signaling service container. Multi-stage: build with the SDK, run on the ASP.NET runtime.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY RemoteDesktop.sln ./
COPY src/RemoteDesktop.Core/ src/RemoteDesktop.Core/
COPY src/RemoteDesktop.Signaling/ src/RemoteDesktop.Signaling/
RUN dotnet restore src/RemoteDesktop.Signaling/RemoteDesktop.Signaling.csproj
RUN dotnet publish src/RemoteDesktop.Signaling/RemoteDesktop.Signaling.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# Runs as a non-root user; TLS is terminated by the reverse proxy in front (see README).
USER $APP_UID
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

# Required secrets are supplied at runtime (never baked into the image):
#   Signaling__SessionTokenKeyBase64   (>= 32 bytes, base64)
#   Signaling__Turn__SharedSecret      (matches coturn static-auth-secret)
#   Signaling__Turn__Uris__0           (e.g. turns:turn.example.com:5349?transport=tcp)
ENTRYPOINT ["dotnet", "RemoteDesktop.Signaling.dll"]
