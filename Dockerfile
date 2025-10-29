# ---- Base runtime image ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 443

# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy csproj and restore
COPY ["Streetpay.API/Streetpay.API.csproj", "Streetpay.API/"]
RUN dotnet restore "Streetpay.API/Streetpay.API.csproj"

# Copy all code
COPY . .

# Build
WORKDIR "/src/Streetpay.API"
RUN dotnet build "Streetpay.API.csproj" -c $BUILD_CONFIGURATION -o /app/build

# ---- Publish stage ----
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Streetpay.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# ---- Final image ----
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Run on port 8080 (Render expects this)
ENTRYPOINT ["dotnet", "Streetpay.API.dll"]