@echo off
setlocal EnableExtensions
pushd "%~dp0"

echo ==================================================
echo   Univers MultiWorld Launcher (Production / VPS)
echo ==================================================
echo   Gateway Proxy : Port 7777 (Arahkan Player kesini)
echo   Master Hub    : Port 7778 (Internal)
echo   Child Worlds  : Port 7779+ (Auto-Start / Sleep)
echo ==================================================

set "TSHOCK_DIR="
if exist "tshock_path.txt" set /p TSHOCK_DIR=<tshock_path.txt
if defined TSHOCK_DIR set "TSHOCK_DIR=%TSHOCK_DIR:"=%"

if not defined TSHOCK_DIR if exist "..\TShock.Server.exe" set "TSHOCK_DIR=.."
if not defined TSHOCK_DIR if exist "TShock.Server.exe" set "TSHOCK_DIR=."

if not defined TSHOCK_DIR (
    echo [ERROR] Folder TShock tidak ditemukan. Isi Univers\tshock_path.txt dengan path folder TShock.
    popd
    pause
    exit /b 1
)

for %%I in ("%TSHOCK_DIR%") do (
    set "LEAF_NAME=%%~nxI"
    set "PARENT_DIR=%%~dpI"
)
if "%PARENT_DIR:~-1%"=="\" set "PARENT_DIR=%PARENT_DIR:~0,-1%"

if /I "%LEAF_NAME%"=="ServerPlugins" (
    set "TSHOCK_ROOT=%PARENT_DIR%"
) else (
    set "TSHOCK_ROOT=%TSHOCK_DIR%"
)

set "SERVER_EXE="
if exist "%TSHOCK_ROOT%\TShock.Server.exe" set "SERVER_EXE=%TSHOCK_ROOT%\TShock.Server.exe"
if not defined SERVER_EXE if exist "%TSHOCK_ROOT%\TerrariaServer.exe" set "SERVER_EXE=%TSHOCK_ROOT%\TerrariaServer.exe"
if not defined SERVER_EXE (
    echo [ERROR] TShock.Server.exe tidak ditemukan di "%TSHOCK_ROOT%".
    popd
    pause
    exit /b 1
)

set "WORLD_FILE="
if exist "tshock_world.txt" set /p WORLD_FILE=<tshock_world.txt
if defined WORLD_FILE set "WORLD_FILE=%WORLD_FILE:"=%"

if defined WORLD_FILE if exist "%WORLD_FILE%" (
    "%SERVER_EXE%" -world "%WORLD_FILE%" -port 7778 -players 32 -noupnp
    goto :done
)

if exist "%TSHOCK_ROOT%\serverconfig.txt" (
    "%SERVER_EXE%" -config "%TSHOCK_ROOT%\serverconfig.txt" -port 7778
    goto :done
)

"%SERVER_EXE%" -port 7778 -noupnp

:done
popd
