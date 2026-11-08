@echo off
setlocal EnableExtensions DisableDelayedExpansion

set "ROOT=%~dp0"
if not exist "%ROOT%CIPHER.sln" (
  echo [ERROR] CIPHER.sln was not found. Refusing to clean an unexpected directory.
  exit /b 2
)

if not exist "%ROOT%logs" mkdir "%ROOT%logs" >nul 2>&1
set "LOG=%ROOT%logs\clean.log"

echo CIPHER clean > "%LOG%"
echo Started: %DATE% %TIME% >> "%LOG%"
echo [CIPHER] Removing bin and obj directories below src only...

for /d /r "%ROOT%src" %%D in (bin obj) do (
  if exist "%%~fD" (
    echo Removing %%~fD >> "%LOG%"
    rd /s /q "%%~fD"
  )
)

if exist "%ROOT%TestResults" (
  echo Removing TestResults >> "%LOG%"
  rd /s /q "%ROOT%TestResults"
)

if exist "%ROOT%.vs" (
  echo Removing .vs >> "%LOG%"
  rd /s /q "%ROOT%.vs"
)

echo [PASSED] Temporary build files were removed.
echo [INFO] Source code, dependencies, artifacts, releases, and user data were not removed.
echo [INFO] Log: %LOG%
exit /b 0
