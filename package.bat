@echo off
setlocal EnableExtensions DisableDelayedExpansion

set "ROOT=%~dp0"
set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Release"

if not exist "%ROOT%CIPHER.sln" (
  echo [ERROR] CIPHER.sln was not found. Run this script from the CIPHER repository root.
  exit /b 2
)

if not exist "%ROOT%logs" mkdir "%ROOT%logs" >nul 2>&1
set "LOG=%ROOT%logs\package-%CONFIG%.log"

echo CIPHER packaging check > "%LOG%"
echo Started: %DATE% %TIME% >> "%LOG%"

echo [CIPHER] Checking offline dependency bundle...
call "%ROOT%install-dependencies.bat"
if errorlevel 1 (
  echo [ERROR] Packaging is blocked because the offline dependency bundle is incomplete.
  echo [ERROR] No MSI was created. See %ROOT%logs\install-dependencies.log
  exit /b 2
)

if not exist "%ROOT%src\CIPHER.Installer\CIPHER.Installer.wixproj" (
  echo [ERROR] The WiX installer project does not exist yet.
  echo [ERROR] No MSI was created. The offline MSI is implemented in Stage 10.
  echo [INFO] Log: %LOG%
  exit /b 3
)

where wix >nul 2>&1
if errorlevel 1 (
  where candle.exe >nul 2>&1
  if errorlevel 1 (
    echo [ERROR] WiX Toolset was not found. No MSI was created.
    exit /b 4
  )
)

echo [CIPHER] Building application before packaging...
call "%ROOT%build.bat" %CONFIG%
if errorlevel 1 (
  echo [ERROR] Application build failed. Packaging stopped.
  exit /b 5
)

echo [ERROR] Installer project validation is present, but the final WiX authoring step is not implemented yet.
echo [ERROR] No MSI was created.
exit /b 6
