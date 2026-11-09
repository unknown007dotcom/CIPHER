@echo off
setlocal EnableExtensions DisableDelayedExpansion

set "ROOT=%~dp0"
if not exist "%ROOT%CIPHER.sln" (
  echo [ERROR] CIPHER.sln was not found. Run this script from the CIPHER repository root.
  exit /b 2
)

if not exist "%ROOT%logs" mkdir "%ROOT%logs" >nul 2>&1
set "LOG=%ROOT%logs\install-dependencies.log"
set "MISSING=0"

echo CIPHER offline dependency check > "%LOG%"
echo Started: %DATE% %TIME% >> "%LOG%"

call :Log "Checking bundled offline dependencies. No downloads will be attempted."

if not exist "%ROOT%dependencies\manifest.sha256" (
  call :Log "[ERROR] Missing dependencies\manifest.sha256"
  set "MISSING=1"
) else (
  call :Log "[OK] Found dependencies\manifest.sha256"
)

for %%D in (vosk espeak-ng playwright licenses) do (
  if not exist "%ROOT%dependencies\%%D" (
    call :Log "[ERROR] Missing dependencies\%%D"
    set "MISSING=1"
  ) else (
    call :Log "[OK] Found dependencies\%%D"
  )
)

if not exist "%ROOT%dependencies\vosk\vosk-model-small-en-us-0.15\conf\model.conf" (
  call :Log "[ERROR] Missing dependencies\vosk\vosk-model-small-en-us-0.15\conf\model.conf"
  set "MISSING=1"
)

if not exist "%ROOT%dependencies\espeak-ng\espeak-ng.exe" (
  call :Log "[ERROR] Missing dependencies\espeak-ng\espeak-ng.exe"
  set "MISSING=1"
)

if "%MISSING%"=="1" (
  call :Log "[FAILED] The offline dependency bundle is incomplete. CIPHER.msi must not be produced yet."
  call :Log "[INFO] Stage it with: pwsh scripts\stage-dependencies.ps1  (see dependencies\README.md)"
  exit /b 2
)

if not exist "%ROOT%scripts\verify-dependencies.ps1" (
  call :Log "[ERROR] Missing scripts\verify-dependencies.ps1"
  exit /b 2
)

call :Log "Verifying SHA-256 manifest..."
powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%scripts\verify-dependencies.ps1" -Root "%ROOT%dependencies" >> "%LOG%" 2>&1
if errorlevel 1 (
  call :Log "[FAILED] SHA-256 manifest verification failed. See %LOG%"
  exit /b 2
)

call :Log "[PASSED] Bundled dependencies are present and hash-verified. See %LOG%"
exit /b 0

:Log
echo %~1
echo %~1 >> "%LOG%"
exit /b 0
