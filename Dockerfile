# ---- Base runtime (smaller) ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Streetpay.API/Streetpay.API.csproj", "Streetpay.API/"]
RUN dotnet restore "Streetpay.API/Streetpay.API.csproj"
COPY . .
WORKDIR "/src/Streetpay.API"
RUN dotnet build "Streetpay.API.csproj" -c $BUILD_CONFIGURATION -o /app/build

# ---- Publish stage ----
FROM build AS publish
RUN dotnet publish "Streetpay.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# ---- FINAL IMAGE (SDK for EF) ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY migrate.sh /app/migrate.sh
RUN chmod +x /app/migrate.sh

# INSTALL EF TOOL
RUN dotnet tool install --global dotnet-ef --version 8.0.10
ENV PATH="$PATH:/root/.dotnet/tools"

ENTRYPOINT ["/bin/bash", "-c", "./migrate.sh && dotnet Streetpay.API.dll"]