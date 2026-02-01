# 1. Final Runtime Stage
FROM mcr.microsoft.com/dotnet/runtime:10.0-bookworm-slim AS base
WORKDIR /app

# Install system dependencies for Media Processing
# intel-media-va-driver-non-free enables QuickSync hardware acceleration
RUN apt-get update && apt-get install -y \
    ffmpeg \
    libexif12 \
    intel-media-va-driver-non-free \
    vainfo \
    && rm -rf /var/lib/apt/lists/*

# 2. Build Stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy both projects (Worker depends on Core)
COPY ["Anichron.Worker/Anichron.Worker.csproj", "Anichron.Worker/"]
COPY ["Anichron.Core/Anichron.Core.csproj", "Anichron.Core/"]
COPY ["Anichron.Infrastructure/Anichron.Infrastructure.csproj", "Anichron.Infrastructure/"]

RUN dotnet restore "./Anichron.Worker/Anichron.Worker.csproj"
COPY . .
WORKDIR "/src/Anichron.Worker"
RUN dotnet build "./Anichron.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/build

# 3. Publish Stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Anichron.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# 4. Final Stage
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Verify Intel GPU access (useful for logs)
RUN vainfo

ENTRYPOINT ["dotnet", "Anichron.Worker.dll"]