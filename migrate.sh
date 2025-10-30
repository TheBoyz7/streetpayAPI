#!/bin/bash
echo "Applying EF Core Migrations..."

# Ensure csproj path points to where it actually exists
dotnet ef database update --project /src/Streetpay.API/Streetpay.API.csproj --startup-project /app --verbose

if [ $? -eq 0 ]; then
  echo "✅ Database ready! Starting API..."
else
  echo "❌ Migration failed!" >&2
  exit 1
fi
