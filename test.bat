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
if not exist "%ROOT%TestResults" mkdir "%ROOT%TestResults" >nul 2>&1

set "LOG=%ROOT%logs\test-%CONFIG%.log"
set "REPORT=%ROOT%TestResults\test-report.txt"

echo CIPHER automated tests > "%LOG%"
echo Configuration: %CONFIG% >> "%LOG%"
echo Started: %DATE% %TIME% >> "%LOG%"

echo [CIPHER] Running automated tests (%CONFIG%)...
dotnet test "%ROOT%CIPHER.sln" --configuration %CONFIG% --logger "trx;LogFileName=CIPHER.Tests.trx" --results-directory "%ROOT%TestResults" >> "%LOG%" 2>&1
set "TEST_EXIT=%ERRORLEVEL%"

echo CIPHER test report > "%REPORT%"
echo Date: %DATE% %TIME% >> "%REPORT%"
echo Configuration: %CONFIG% >> "%REPORT%"
echo Exit code: %TEST_EXIT% >> "%REPORT%"
echo TRX report: %ROOT%TestResults\CIPHER.Tests.trx >> "%REPORT%"
echo Detailed log: %LOG% >> "%REPORT%"

if not "%TEST_EXIT%"=="0" (
  echo [ERROR] Tests failed with exit code %TEST_EXIT%. Full log: %LOG%
  type "%LOG%"
  exit /b %TEST_EXIT%
)

echo [PASSED] Tests completed. Report: %REPORT%
echo [INFO] Log: %LOG%
exit /b 0
