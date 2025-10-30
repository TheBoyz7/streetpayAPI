# ---- Final stage ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS final
WORKDIR /app

# Copy published app
COPY --from=publish /app/publish .

# Copy entire source (needed for EF migrations)
COPY . /src

# Copy migrate script
COPY migrate.sh /app/migrate.sh
RUN chmod +x /app/migrate.sh

# Install EF CLI
RUN dotnet tool install --global dotnet-ef --version 8.0.10
ENV PATH="$PATH:/root/.dotnet/tools"

ENTRYPOINT ["/bin/bash", "-c", "./migrate.sh && dotnet Streetpay.API.dll"]
