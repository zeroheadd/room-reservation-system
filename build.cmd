@echo off
REM ---------------------------------------------------------------------------
REM Build helper for this Windows checkout.
REM
REM The .NET toolchain is localised with three settings so it never writes
REM outside the repository:
REM   DOTNET_CLI_HOME - first-run sentinels and tool state
REM   NUGET_PACKAGES  - the restored package cache
REM   TEMP/TMP        - MSBuild scratch space
REM
REM On hosts where the .NET CLI cannot do TLS itself, start the bridge first:
REM   node tools\nuget-bridge.js
REM and see NuGet.Config.
REM
REM Usage:
REM   build.cmd build      build the API and the tests  (default)
REM   build.cmd test       run the backend test suite
REM   build.cmd run        run the API on http://localhost:5080
REM   build.cmd clean      remove build output
REM   build.cmd <anything> anything else is passed straight to dotnet
REM ---------------------------------------------------------------------------

setlocal
set "REPO=%~dp0"
set "DOTNET=%REPO%.dotnet\dotnet.exe"
set "SOLUTION=%REPO%RoomReservation.slnx"

if not exist "%DOTNET%" (
  echo [build] .NET SDK not found at %DOTNET%
  echo [build] Install it with:
  echo [build]   dotnet-install.ps1 -Channel 9.0 -InstallDir "%REPO%.dotnet"
  exit /b 1
)

set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_NOLOGO=1"
set "DOTNET_CLI_HOME=%REPO%.dotnet-home"
set "NUGET_PACKAGES=%REPO%.nuget\packages"
set "APPDATA=%REPO%.appdata\Roaming"
set "USERPROFILE=%REPO%.appdata"
set "TEMP=%REPO%.tmp"
set "TMP=%REPO%.tmp"

set "COMMAND=%~1"
if "%COMMAND%"=="" set "COMMAND=build"

if /i "%COMMAND%"=="build" (
  "%DOTNET%" build "%SOLUTION%" || exit /b 1
  exit /b 0
)

if /i "%COMMAND%"=="test" (
  "%DOTNET%" test "%SOLUTION%" --no-restore
  exit /b %ERRORLEVEL%
)

if /i "%COMMAND%"=="run" (
  set "ASPNETCORE_ENVIRONMENT=Development"
  set "ASPNETCORE_URLS=http://localhost:5080"
  "%DOTNET%" run --project "%REPO%src\RoomReservation.Api\RoomReservation.Api.csproj"
  exit /b %ERRORLEVEL%
)

if /i "%COMMAND%"=="clean" (
  "%DOTNET%" clean "%SOLUTION%"
  exit /b %ERRORLEVEL%
)

"%DOTNET%" %*
exit /b %ERRORLEVEL%
