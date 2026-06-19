@echo off
REM MORO VelocityX - Publish Script for Windows
REM Build and publish for Windows x64 with self-contained runtime

echo.
echo 🔨 Building MOROVelocityX...
dotnet clean
if errorlevel 1 goto error
dotnet restore
if errorlevel 1 goto error

echo.
echo 📦 Publishing for Windows x64 (Self-Contained)...
dotnet publish ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:PublishReadyToRun=true ^
  -o ./publish
if errorlevel 1 goto error

echo.
echo ✅ Publish Complete!
echo 📍 Location: ./publish/MOROVelocityX.exe
echo.
echo ✨ Ready to use on Windows!
echo.
pause
goto end

:error
echo.
echo ❌ Error during build/publish!
pause
goto end

:end
