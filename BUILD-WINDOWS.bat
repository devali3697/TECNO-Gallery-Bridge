@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET 8 SDK is required. Install it from https://dotnet.microsoft.com/download/dotnet/8.0
  pause
  exit /b 1
)
echo Building TECNO Gallery Bridge for Windows 10/11 x64...
dotnet publish TECNO.GalleryBridge.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
if errorlevel 1 (
  echo BUILD FAILED. Please send the error output for diagnosis.
  pause
  exit /b 1
)
echo BUILD COMPLETE. Open the publish folder to run TECNO.GalleryBridge.exe
explorer "%CD%\publish"
pause
