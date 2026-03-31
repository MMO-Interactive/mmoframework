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

echo Cleaning up stale zone server processes...
taskkill /f /im ZoneServer.exe >nul 2>nul

echo Starting MMO networking host with Unity zone processes forced on...
start "MMO Networking Host (Unity Zones)" cmd /k "cd /d ""%ROOT_DIR%"" && set MMO_USE_UNITY_ZONE_PROCESS=true && set MMO_UNITY_ZONE_EXE=%ROOT_DIR%Builds\ZoneServer\Win64\ZoneServer.exe && dotnet run --project Server\MMONetworking.ServerHost\MMONetworking.ServerHost.csproj"

echo Waiting for services to boot...
timeout /t 3 /nobreak >nul

echo Opening management dashboard...
start "" "http://127.0.0.1:7080/"

echo Startup complete.
echo Gateway:   tcp://127.0.0.1:7000
echo Dashboard: http://127.0.0.1:7080/
echo Zone mode: external Unity zone processes
echo Unity EXE: %ROOT_DIR%Builds\ZoneServer\Win64\ZoneServer.exe

endlocal
