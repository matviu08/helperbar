@echo off
if not exist "%~dp0dist\HelperBar\HelperBar.exe" (
  echo Build the application first with Build.ps1.
  pause
  exit /b 1
)
start "" "%~dp0dist\HelperBar\HelperBar.exe"
