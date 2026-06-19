#!/bin/bash

# MORO VelocityX - Publish Script
# Build and publish for Windows x64 with self-contained runtime

set -e  # Exit on error

echo "🔨 Building MOROVelocityX..."
dotnet clean
dotnet restore

echo "📦 Publishing for Windows x64 (Self-Contained)..."
dotnet publish \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=true \
  -o ./publish

echo ""
echo "✅ Publish Complete!"
echo "📍 Location: ./publish/MOROVelocityX.exe"
echo "💾 Size: $(du -h ./publish/MOROVelocityX.exe | cut -f1)"
echo ""
echo "✨ Ready to use on Windows!"
