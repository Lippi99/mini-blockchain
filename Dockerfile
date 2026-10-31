# MiniChain node image. Build with: docker build -t minichain-node .
# (Alternatively, without this file: dotnet publish src/MiniChain.Node -c Release /t:PublishContainer)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, in its own layer, so changing code does not re-download packages.
COPY Directory.Build.props .editorconfig ./
COPY src/MiniChain.Core/MiniChain.Core.csproj src/MiniChain.Core/
COPY src/MiniChain.Node/MiniChain.Node.csproj src/MiniChain.Node/
RUN dotnet restore src/MiniChain.Node/MiniChain.Node.csproj

COPY src/MiniChain.Core/ src/MiniChain.Core/
COPY src/MiniChain.Node/ src/MiniChain.Node/
RUN dotnet publish src/MiniChain.Node/MiniChain.Node.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./

# The chain and the node's own wallet live in /data; mount a volume there to keep them.
RUN mkdir /data && chown "$APP_UID" /data
ENV MiniChain__DataDirectory=/data
VOLUME /data

# The image listens on port 8080 (ASPNETCORE_HTTP_PORTS) and runs as the non-root "app" user.
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "MiniChain.Node.dll"]
