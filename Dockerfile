# ==============================================================================
# ZIN STONE Quotation System - Production Dockerfile for Render Web Service
# ==============================================================================

# 1. Build Stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files and restore dependencies
COPY ["backend/FactoryQuoteApi.csproj", "backend/"]
RUN dotnet restore "backend/FactoryQuoteApi.csproj"

# Copy source and publish
COPY backend/ backend/
WORKDIR "/src/backend"
RUN dotnet publish "FactoryQuoteApi.csproj" -c Release -o /app/publish /p:UseAppHost=false

# 2. Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Expose Render default port and listen on 0.0.0.0
ENV PORT=8080
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV DOTNET_RUNNING_IN_CONTAINER=true

# Install LibreOffice and Arabic/Unicode fonts for native high-fidelity Linux PDF generation
RUN apt-get update && apt-get install -y --no-install-recommends \
    libreoffice \
    libreoffice-writer \
    fonts-noto-core \
    fonts-noto-extra \
    fonts-kacst \
    fonts-dejavu-core \
    fontconfig \
    ca-certificates \
    curl \
    && fc-cache -f -v \
    && rm -rf /var/lib/apt/lists/*

# Copy published application
COPY --from=build /app/publish .

# Create persistent/cache directory structure
RUN mkdir -p /app/App_Data/quotes /app/Templates

# Healthcheck for container runtimes
HEALTHCHECK --interval=30s --timeout=10s --start-period=15s --retries=3 \
  CMD curl -f http://localhost:${PORT:-8080}/api/health || exit 1

ENTRYPOINT ["dotnet", "FactoryQuoteApi.dll"]
