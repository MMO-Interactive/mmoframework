@echo off
setlocal

set "ROOT_DIR=%~dp0"
cd /d "%ROOT_DIR%"

echo Building server host...
dotnet build "Server\MMONetworking.ServerHost\MMONetworking.ServerHost.csproj"
if errorlevel 1 (
    echo Build failed.
    exit /b 1
)

echo Starting MMO networking host...
start "MMO Networking Host" cmd /k "cd /d ""%ROOT_DIR%"" && dotnet run --project Server\MMONetworking.ServerHost\MMONetworking.ServerHost.csproj"

echo Waiting for services to boot...
timeout /t 3 /nobreak >nul

echo Opening management dashboard...
start "" "http://127.0.0.1:7080/"

echo Startup complete.
echo Gateway:   tcp://127.0.0.1:7000
echo Dashboard: http://127.0.0.1:7080/
echo Zone 1:    TCP 7101 / UDP 7201
echo Zone 2:    TCP 7102 / UDP 7202

endlocal
