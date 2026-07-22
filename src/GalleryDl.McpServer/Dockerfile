# Stdio MCP server: no web server, no exposed ports. An MCP client launches this container
# with `docker compose run --rm -T gallerydl-mcpserver` and talks JSON-RPC over stdin/stdout.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
USER $APP_UID
WORKDIR /app

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["GalleryDl.McpServer/GalleryDl.McpServer.csproj", "GalleryDl.McpServer/"]
RUN dotnet restore "./GalleryDl.McpServer/GalleryDl.McpServer.csproj"
COPY . .
WORKDIR "/src/GalleryDl.McpServer"
RUN dotnet build "./GalleryDl.McpServer.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./GalleryDl.McpServer.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "GalleryDl.McpServer.dll"]
