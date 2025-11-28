# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5000

# non-root user
RUN adduser --system --disabled-password farmer
USER farmer

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
#ARG BUILD_CONFIGURATION=Debug
#WORKDIR /src
#COPY ["Directory.Packages.props", "."]
#COPY ["Directory.Build.props", "."]
#COPY ["Directory.Build.targets", "."]
#COPY ["BotFarm/BotFarm.csproj", "BotFarm/"]
#COPY ["BotFarm.Core/BotFarm.Core.csproj", "BotFarm.Core/"]
##RUN dotnet restore "./BotFarm/BotFarm.csproj"
COPY . /repo
WORKDIR /repo/BotFarm
#RUN dotnet build "./BotFarm.csproj" -c $BUILD_CONFIGURATION -o /app/build
RUN dotnet restore BotFarm.csproj

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./BotFarm.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app

# Create directories for logs and backups
RUN mkdir -p /app/logs /app/backups /app/tmp

# Install curl for health checks (optional)
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish .
ENTRYPOINT dotnet BotFarm.dll