#!/bin/bash
echo "Applying EF Core Migrations..."
dotnet ef database update --project Streetpay.API/Streetpay.API.csproj --verbose
if [ $? -eq 0 ]; then
  echo "Database ready! Starting API..."
else
  echo "Migration failed!" >&2
  exit 1
fi