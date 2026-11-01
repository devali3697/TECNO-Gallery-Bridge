@echo off
setlocal
cd /d "%~dp0"
if not exist "publish\TECNO.GalleryBridge.exe" (
  echo Application not built. Run BUILD-WINDOWS.bat first.
  pause
  exit /b 1
)
echo Starting TECNO Gallery Bridge...
start /wait "" "%CD%\publish\TECNO.GalleryBridge.exe"
echo Application exit code: %ERRORLEVEL%
if exist "%LOCALAPPDATA%\TECNO Gallery Bridge\startup-error.log" (
  echo.
  echo Startup error log:
  type "%LOCALAPPDATA%\TECNO Gallery Bridge\startup-error.log"
)
echo.
pause
