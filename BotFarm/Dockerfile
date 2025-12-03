# Stage 1: Base runtime (Alpine for smaller size)
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS base
WORKDIR /app
EXPOSE 5000

# Create non-root user
RUN adduser --disabled-password --home /app --gecos '' farmer && chown -R farmer /app
USER farmer

# Stage 2: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy solution and project files first (better caching)
COPY ["Directory.Packages.props", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["BotFarm/BotFarm.csproj", "BotFarm/"]
COPY ["BotFarm.Core/BotFarm.Core.csproj", "BotFarm.Core/"]

# Restore dependencies (cached if project files don't change)
RUN dotnet restore "BotFarm/BotFarm.csproj"

# Copy remaining source code
COPY . .

# Stage 3: Publish 
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
WORKDIR "/src/BotFarm"

RUN dotnet publish "BotFarm.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# Stage 4: Final runtime image
FROM base AS final
WORKDIR /app

# Create required directories
USER root
RUN mkdir -p /app/logs /app/backups /app/tmp && \
    chown -R farmer:farmer /app/logs /app/backups /app/tmp
USER farmer

# Copy published application
COPY --from=publish --chown=farmer:farmer /app/publish .

# Health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost:5000/health || exit 1

# Environment variables
ENV ASPNETCORE_URLS=http://+:5000 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_EnableDiagnostics=0

ENTRYPOINT ["dotnet", "BotFarm.dll"]