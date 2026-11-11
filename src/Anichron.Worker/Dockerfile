# syntax=docker/dockerfile:1
# 1. Final Runtime Stage
FROM mcr.microsoft.com/dotnet/runtime:10.0-noble AS base
WORKDIR /app
ARG TARGETARCH

# OpenCvSharp's native library needs GTK 3 on both architectures, libtesseract and libwebpdemux on arm64, libatomic on amd64 (ADR 0009).
# Intel QuickSync needs the iHD VA driver and the oneVPL runtime; neither exists in the arm64 apt repos.
# TARGETARCH is injected automatically by BuildKit.
RUN apt-get update && apt-get install -y --no-install-recommends \
        ffmpeg \
        libexif12 \
        libgtk-3-0t64 \
    && if [ "$TARGETARCH" = "arm64" ]; then \
         apt-get install -y --no-install-recommends libtesseract5 libwebpdemux2; \
       fi \
    && if [ "$TARGETARCH" = "amd64" ]; then \
         apt-get install -y --no-install-recommends libatomic1 intel-media-va-driver-non-free libmfx-gen1.2; \
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

# Lock files must be present before restore, otherwise the restore layer
# resolves its own graph and is then overwritten by COPY . . below.
COPY ["Anichron.Worker/packages.lock.json", "Anichron.Worker/"]
COPY ["Anichron.Core/packages.lock.json", "Anichron.Core/"]
COPY ["Anichron.Infrastructure/packages.lock.json", "Anichron.Infrastructure/"]

RUN dotnet restore "./Anichron.Worker/Anichron.Worker.csproj"
COPY . .
WORKDIR "/src/Anichron.Worker"
RUN --mount=type=secret,id=sixlabors_license,env=SixLaborsLicenseKey \
    dotnet build "./Anichron.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/build

# 3. Publish Stage
# The publish output carries native libraries for every platform; keep only the target's runtimes folder.
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG TARGETARCH
RUN --mount=type=secret,id=sixlabors_license,env=SixLaborsLicenseKey \
    dotnet publish "./Anichron.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false \
    && case "$TARGETARCH" in \
         amd64) target=linux-x64 ;; \
         arm64) target=linux-arm64 ;; \
         *) echo "unsupported TARGETARCH '$TARGETARCH'" >&2; exit 1 ;; \
       esac \
    && test -d "/app/publish/runtimes/$target" \
    && find /app/publish/runtimes -mindepth 1 -maxdepth 1 ! -name "$target" -exec rm -r {} +

# 4. Final Stage
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "Anichron.Worker.dll"]