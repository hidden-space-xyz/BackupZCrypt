# See https://aka.ms/customizecontainer to learn how to customize your debug container
# and how Visual Studio uses this Dockerfile to build images for faster debugging.

# This stage is used when running from VS in fast mode
FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS base

WORKDIR /app

RUN mkdir -p /data/backup-source /data/backup-destination /data/restore-source /data/restore-destination \
    && chown -R $APP_UID /data /app

USER $APP_UID

VOLUME ["/data/backup-source", "/data/backup-destination", "/data/restore-source", "/data/restore-destination"]

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build

ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["BackupZCrypt.Worker/BackupZCrypt.Worker.csproj", "BackupZCrypt.Worker/"]
COPY ["BackupZCrypt.Application/BackupZCrypt.Application.csproj", "BackupZCrypt.Application/"]
COPY ["BackupZCrypt.Composition/BackupZCrypt.Composition.csproj", "BackupZCrypt.Composition/"]
COPY ["BackupZCrypt.Domain/BackupZCrypt.Domain.csproj", "BackupZCrypt.Domain/"]
COPY ["BackupZCrypt.Infrastructure/BackupZCrypt.Infrastructure.csproj", "BackupZCrypt.Infrastructure/"]

RUN dotnet restore "./BackupZCrypt.Worker/BackupZCrypt.Worker.csproj"

COPY . .

WORKDIR "/src/BackupZCrypt.Worker"
RUN dotnet build "./BackupZCrypt.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied into the final stage
FROM build AS publish

ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./BackupZCrypt.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in normal mode
FROM base AS final

WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "BackupZCrypt.Worker.dll"]
