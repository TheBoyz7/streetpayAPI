# ---- Base runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["Streetpay.API/Streetpay.API.csproj", "Streetpay.API/"]
RUN dotnet restore "Streetpay.API/Streetpay.API.csproj"

# Copy everything and build
COPY . .
WORKDIR "/src/Streetpay.API"
RUN dotnet build "Streetpay.API.csproj" -c $BUILD_CONFIGURATION -o /app/build

# ---- Publish stage ----
FROM build AS publish
RUN dotnet publish "Streetpay.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# ---- Final runtime ----
FROM base AS final
WORKDIR /app

# Copy published output
COPY --from=publish /app/publish .

# Copy RSA key files so the cryptography service can find them
COPY Streetpay.API/Keys /app/Keys

# Copy your database explicitly (optional for SQLite)
COPY Streetpay.API/streetpay.db ./streetpay.db

# Set safe permissions
RUN chmod -R 755 /app/Keys && chmod 777 streetpay.db || true

# ---- Start app ----
ENTRYPOINT ["dotnet", "Streetpay.API.dll"]
