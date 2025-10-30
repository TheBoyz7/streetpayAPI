# ---- Base runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

# ---- Build stage (SDK) ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# copy csproj and restore (leverage cache)
COPY ["Streetpay.API/Streetpay.API.csproj", "Streetpay.API/"]
RUN dotnet restore "Streetpay.API/Streetpay.API.csproj"

# copy everything and build
COPY . .
WORKDIR "/src/Streetpay.API"
RUN dotnet build "Streetpay.API.csproj" -c $BUILD_CONFIGURATION -o /app/build

# ---- Publish stage (still SDK) ----
FROM build AS publish
RUN dotnet publish "Streetpay.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# ---- Migration runner (optional: run migrations here while SDK available) ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS migrate
WORKDIR /app

# copy published output
COPY --from=publish /app/publish .

# copy migrations and project sources (so dotnet ef can build)
# NOTE: we copy the repo into /src so ef has the project and migration C# files
COPY . /src

# make migrate.sh available and executable
COPY migrate.sh /app/migrate.sh
RUN chmod +x /app/migrate.sh

# install ef tool for runtime migration (only in this stage)
RUN dotnet tool install --global dotnet-ef --version 8.0.10
ENV PATH="$PATH:/root/.dotnet/tools"

# run migrations at image build-time (optional). If you want to avoid failing builds when DB already exists, handle in script.
RUN /app/migrate.sh || true

# ---- Final runtime image (small) ----
FROM base AS final
WORKDIR /app

# copy published output only (no source)
COPY --from=publish /app/publish .

# runtime entrypoint
ENTRYPOINT ["dotnet", "Streetpay.API.dll"]
