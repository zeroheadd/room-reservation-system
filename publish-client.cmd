@echo off
REM ---------------------------------------------------------------------------
REM Builds the React client and copies the bundle into the API's wwwroot, so a
REM single `build.cmd run` serves both the SPA and the API on port 5080.
REM
REM During development you normally do not need this: run `npm run dev` in
REM client\ and let Vite proxy /api to the backend.
REM ---------------------------------------------------------------------------

setlocal
set "REPO=%~dp0"
set "CLIENT=%REPO%client"
set "WWWROOT=%REPO%src\RoomReservation.Api\wwwroot"

pushd "%CLIENT%"
call npm install --no-audit --no-fund || (popd & exit /b 1)
call npm run build || (popd & exit /b 1)
popd

if exist "%WWWROOT%" rmdir /s /q "%WWWROOT%"
mkdir "%WWWROOT%"
xcopy /e /i /y /q "%CLIENT%\dist\*" "%WWWROOT%" >nul || exit /b 1

echo [publish-client] Client copied to %WWWROOT%
exit /b 0
