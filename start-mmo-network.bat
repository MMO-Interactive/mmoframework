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

echo Building asset server...
dotnet build "Server\MMONetworking.AssetServer\MMONetworking.AssetServer.csproj"
if errorlevel 1 (
    echo Asset server build failed.
    exit /b 1
)

echo Cleaning up stale zone server processes...
taskkill /f /im ZoneServer.exe >nul 2>nul
echo Cleaning up stale asset server processes...
taskkill /f /fi "WINDOWTITLE eq MMO Asset Server*" >nul 2>nul

echo Starting asset server...
start "MMO Asset Server" cmd /k "cd /d ""%ROOT_DIR%"" && dotnet run --project Server\MMONetworking.AssetServer\MMONetworking.AssetServer.csproj"

echo Waiting for asset server to boot...
timeout /t 3 /nobreak >nul

echo Starting MMO networking host...
start "MMO Networking Host" cmd /k "cd /d ""%ROOT_DIR%"" && set MMO_USE_UNITY_ZONE_PROCESS=false && dotnet run --project Server\MMONetworking.ServerHost\MMONetworking.ServerHost.csproj"

echo Waiting for services to boot...
timeout /t 3 /nobreak >nul

echo Opening management dashboard...
start "" "http://127.0.0.1:7080/"

echo Startup complete.
echo Assets:    http://127.0.0.1:7095/
echo Gateway:   tcp://127.0.0.1:7000
echo Dashboard: http://127.0.0.1:7080/
echo Zone 1:    TCP 7101 / UDP 7201
echo Zone 2:    TCP 7102 / UDP 7202

endlocal
