#!/bin/bash
set -e
echo "Applying EF Core Migrations..."

# Ensure we run ef against the project source in /src (we copied repo to /src in migrate stage)
# Use --no-build to avoid attempting to rebuild the project from the publish output
dotnet ef database update --project /src/Streetpay.API/Streetpay.API.csproj --startup-project /src/Streetpay.API --no-build --verbose || {
  echo "Migration failed or already applied (non-zero exit)."
  exit 1
}

echo "Migrations applied."
