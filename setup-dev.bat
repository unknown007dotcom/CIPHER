@echo off
setlocal EnableExtensions DisableDelayedExpansion

set "ROOT=%~dp0"
if not exist "%ROOT%CIPHER.sln" (
  echo [ERROR] CIPHER.sln was not found next to setup-dev.bat.
  echo [ERROR] Run this script from the CIPHER repository root.
  exit /b 2
)

if not exist "%ROOT%logs" mkdir "%ROOT%logs" >nul 2>&1
set "LOG=%ROOT%logs\setup-dev.log"
set "MISSING=0"
set "WARNINGS=0"

echo CIPHER developer environment check > "%LOG%"
echo Started: %DATE% %TIME% >> "%LOG%"

call :Log "CIPHER developer environment check"
call :Log "Repository: %ROOT%"

ver >> "%LOG%" 2>&1
call :Log "Windows version information was written to the log."

if /I not "%PROCESSOR_ARCHITECTURE%"=="AMD64" (
  call :Log "[ERROR] CIPHER v1 targets Windows x64. PROCESSOR_ARCHITECTURE=%PROCESSOR_ARCHITECTURE%"
  set "MISSING=1"
) else (
  call :Log "[OK] Architecture: AMD64"
)

where git >nul 2>&1
if errorlevel 1 (
  call :Log "[ERROR] Git was not found on PATH."
  set "MISSING=1"
) else (
  call :Log "[OK] Git found."
  git --version >> "%LOG%" 2>&1
)

where dotnet >nul 2>&1
if errorlevel 1 (
  call :Log "[ERROR] .NET SDK was not found on PATH. Install the .NET 10 SDK from Microsoft."
  set "MISSING=1"
) else (
  dotnet --list-sdks > "%TEMP%\cipher-sdks.txt" 2>&1
  findstr /R /C:"^10\." "%TEMP%\cipher-sdks.txt" >nul 2>&1
  if errorlevel 1 (
    call :Log "[ERROR] A .NET 10 SDK was not found. Installed SDKs are recorded in the log."
    type "%TEMP%\cipher-sdks.txt" >> "%LOG%"
    set "MISSING=1"
  ) else (
    call :Log "[OK] .NET 10 SDK found."
    type "%TEMP%\cipher-sdks.txt" >> "%LOG%"
  )
  del "%TEMP%\cipher-sdks.txt" >nul 2>&1
)

where wix >nul 2>&1
if errorlevel 1 (
  where candle.exe >nul 2>&1
  if errorlevel 1 (
    call :Log "[WARN] WiX Toolset was not found. It is required only for the installer stage."
    set "WARNINGS=1"
  ) else (
    call :Log "[OK] WiX candle.exe found."
  )
) else (
  call :Log "[OK] WiX command-line tool found."
  wix --version >> "%LOG%" 2>&1
)

for %%D in (artifacts logs TestResults dependencies) do (
  if not exist "%ROOT%%%D" mkdir "%ROOT%%%D" >nul 2>&1
  if exist "%ROOT%%%D" (
    call :Log "[OK] Prepared directory: %%D"
  ) else (
    call :Log "[ERROR] Could not create directory: %%D"
    set "MISSING=1"
  )
)

if "%~1"=="--install-missing" (
  call :Log "[WARN] Automatic installation was requested but is not performed by this check-only stage."
  call :Log "[WARN] Install missing tools from their official publishers, then run setup-dev.bat again."
  set "WARNINGS=1"
)

if "%MISSING%"=="1" (
  call :Log "[FAILED] One or more required developer tools are missing. See %LOG%"
  exit /b 2
)

if "%WARNINGS%"=="1" (
  call :Log "[PASSED WITH WARNINGS] Core development tools are ready. See %LOG%"
  exit /b 1
)

call :Log "[PASSED] Developer environment is ready. See %LOG%"
exit /b 0

:Log
echo %~1
echo %~1 >> "%LOG%"
exit /b 0
