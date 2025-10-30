# ---- Base runtime ----
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

# ---- Final runtime ----
FROM base AS final
WORKDIR /app

# Copy publish output
COPY --from=publish /app/publish .

# Copy your database explicitly
COPY Streetpay.API/streetpay.db ./streetpay.db

# Make sure the app can write to DB
RUN chmod 777 streetpay.db || true

ENTRYPOINT ["dotnet", "Streetpay.API.dll"]
