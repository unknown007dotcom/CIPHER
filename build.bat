@echo off
setlocal EnableExtensions DisableDelayedExpansion

set "ROOT=%~dp0"
set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Release"

if not exist "%ROOT%CIPHER.sln" (
  echo [ERROR] CIPHER.sln was not found. Run this script from the CIPHER repository root.
  exit /b 2
)

where dotnet >nul 2>&1
if errorlevel 1 (
  echo [ERROR] The .NET SDK was not found on PATH. Run setup-dev.bat for details.
  exit /b 2
)

if not exist "%ROOT%logs" mkdir "%ROOT%logs" >nul 2>&1
set "LOG=%ROOT%logs\build-%CONFIG%.log"

echo CIPHER build > "%LOG%"
echo Configuration: %CONFIG% >> "%LOG%"
echo Started: %DATE% %TIME% >> "%LOG%"

echo [CIPHER] Restoring CIPHER.sln (%CONFIG%)...
dotnet restore "%ROOT%CIPHER.sln" >> "%LOG%" 2>&1
if errorlevel 1 (
  echo [ERROR] NuGet restore failed. Full log: %LOG%
  type "%LOG%"
  exit /b 3
)

echo [CIPHER] Building CIPHER.sln (%CONFIG%)...
dotnet build "%ROOT%CIPHER.sln" --configuration %CONFIG% --no-restore >> "%LOG%" 2>&1
if errorlevel 1 (
  echo [ERROR] Build failed. Full log: %LOG%
  type "%LOG%"
  exit /b 4
)

echo [PASSED] Build completed. Log: %LOG%
echo Build completed: %DATE% %TIME% >> "%LOG%"
exit /b 0
