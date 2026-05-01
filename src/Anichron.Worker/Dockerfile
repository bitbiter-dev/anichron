# 1. Final Runtime Stage
FROM mcr.microsoft.com/dotnet/runtime:10.0-noble AS base
WORKDIR /app
ARG TARGETARCH

# intel-media-va-driver-non-free does not exist in arm64 apt repos; skip it there.
# TARGETARCH is injected automatically by BuildKit.
RUN apt-get update && apt-get install -y \
        ffmpeg \
        libexif12 \
    && if [ "$TARGETARCH" = "amd64" ]; then \
         apt-get install -y intel-media-va-driver-non-free vainfo; \
       fi \
    && rm -rf /var/lib/apt/lists/*

# 2. Build Stage
# $BUILDPLATFORM = CI runner arch (amd64). dotnet publish produces arch-neutral IL,
# so compiling natively avoids QEMU emulation overhead without affecting the output.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy both projects (Worker depends on Core)
COPY ["Directory.Packages.props", "."]
COPY ["Directory.Build.props", "."]
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

ENTRYPOINT ["dotnet", "Anichron.Worker.dll"]