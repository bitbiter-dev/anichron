# 1. Runtime Stage (Using Alpine for a smaller, more secure footprint)
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS base
WORKDIR /app
EXPOSE 8080

# 2. UI Stage
FROM --platform=$BUILDPLATFORM node:26.13.0-alpine AS ui
WORKDIR /ui
RUN npm install --global pnpm@12.16.1
COPY ["Anichron.UI/package.json", "Anichron.UI/pnpm-lock.yaml", "Anichron.UI/pnpm-workspace.yaml", "./"]
RUN pnpm install --frozen-lockfile
COPY ["Anichron.UI/", "./"]
RUN pnpm build

# 3. Build Stage
# $BUILDPLATFORM = CI runner arch (amd64). dotnet publish produces arch-neutral IL,
# so compiling natively avoids QEMU emulation overhead without affecting the output.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy projects to handle shared dependencies
COPY ["Directory.Packages.props", "."]
COPY ["Directory.Build.props", "."]
COPY ["Anichron.API/Anichron.API.csproj", "Anichron.API/"]
COPY ["Anichron.Core/Anichron.Core.csproj", "Anichron.Core/"]
COPY ["Anichron.Infrastructure/Anichron.Infrastructure.csproj", "Anichron.Infrastructure/"]

# Lock files must be present before restore, otherwise the restore layer
# resolves its own graph and is then overwritten by COPY . . below.
COPY ["Anichron.API/packages.lock.json", "Anichron.API/"]
COPY ["Anichron.Core/packages.lock.json", "Anichron.Core/"]
COPY ["Anichron.Infrastructure/packages.lock.json", "Anichron.Infrastructure/"]

# Restore based on the API project
RUN dotnet restore "Anichron.API/Anichron.API.csproj"

# Copy remaining source code
COPY . .
WORKDIR "/src/Anichron.API"
RUN dotnet build "Anichron.API.csproj" -c $BUILD_CONFIGURATION -o /app/build

# 4. Publish Stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Anichron.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish

# 5. Final Production Stage
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=ui /ui/dist ./wwwroot
RUN mkdir configuration && chown $APP_UID configuration

# Use the non-privileged user provided by the base image
USER $APP_UID

ENTRYPOINT ["dotnet", "Anichron.API.dll"]