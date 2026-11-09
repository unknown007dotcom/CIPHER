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

echo [CIPHER] Publishing CIPHER.App (win-x64, %CONFIG%, self-contained)...
dotnet publish "%ROOT%src\CIPHER.App\CIPHER.App.csproj" -c %CONFIG% -r win-x64 --self-contained true -o "%ROOT%artifacts\publish\win-x64" >> "%LOG%" 2>&1
if errorlevel 1 (
  echo [ERROR] dotnet publish failed. See %LOG%
  exit /b 4
)

echo [CIPHER] Building CIPHER.msi (%CONFIG%)...
dotnet build "%ROOT%src\CIPHER.Installer\CIPHER.Installer.wixproj" -c %CONFIG% >> "%LOG%" 2>&1
if errorlevel 1 (
  echo [ERROR] WiX MSI build failed. See %LOG%
  exit /b 5
)

set "MSI="
for /r "%ROOT%src\CIPHER.Installer\bin" %%F in (CIPHER.msi) do if not defined MSI set "MSI=%%F"
if not defined MSI (
  echo [ERROR] The WiX build reported success but CIPHER.msi was not found. See %LOG%
  exit /b 6
)

if not exist "%ROOT%artifacts" mkdir "%ROOT%artifacts" >nul 2>&1
copy /y "%MSI%" "%ROOT%artifacts\CIPHER.msi" >nul
echo [PASSED] CIPHER.msi built successfully: %ROOT%artifacts\CIPHER.msi
echo [INFO] Harvested MSI: %MSI%
echo [INFO] Log: %LOG%
exit /b 0
