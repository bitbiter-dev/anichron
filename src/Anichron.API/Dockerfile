# 1. Runtime Stage (Using Alpine for a smaller, more secure footprint)
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS base
WORKDIR /app
EXPOSE 8080

# 2. Build Stage
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy projects to handle shared dependencies
COPY ["Anichron.API/Anichron.API.csproj", "Anichron.API/"]
COPY ["Anichron.Core/Anichron.Core.csproj", "Anichron.Core/"]
COPY ["Anichron.Infrastructure/Anichron.Infrastructure.csproj", "Anichron.Infrastructure/"]

# Restore based on the API project
RUN dotnet restore "Anichron.API/Anichron.API.csproj"

# Copy remaining source code
COPY . .
WORKDIR "/src/Anichron.API"
RUN dotnet build "Anichron.API.csproj" -c $BUILD_CONFIGURATION -o /app/build

# 3. Publish Stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Anichron.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish

# 4. Final Production Stage
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Use the non-privileged user provided by the base image
USER $APP_UID

ENTRYPOINT ["dotnet", "Anichron.API.dll"]