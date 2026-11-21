@echo off
setlocal EnableExtensions
pushd "%~dp0"

echo ==================================================
echo   Univers Core 2.0 - Sync, Build, Deploy ^& Start
echo ==================================================

if exist "..\.git" (
    echo [1/4] Menarik update terbaru dari GitHub...
    git -C .. pull --ff-only
    goto :build
)
if exist ".git" (
    echo [1/4] Menarik update terbaru dari GitHub...
    git pull --ff-only
    goto :build
)
echo [1/4] Folder git tidak ditemukan, melewati git pull.

:build
echo [2/4] Mengompilasi Univers.csproj (.NET 9.0 Release)...
dotnet build Univers.csproj -c Release
if errorlevel 1 (
    echo.
    echo [ ERROR ] Build gagal! Periksa pesan error di atas.
    popd
    pause
    exit /b 1
)

set "OUT_DIR=bin\Release\net9.0"
set "TSHOCK_DIR="
if exist "tshock_path.txt" set /p TSHOCK_DIR=<tshock_path.txt
if not defined TSHOCK_DIR goto :no_deploy

set "TSHOCK_DIR=%TSHOCK_DIR:"=%"
if "%TSHOCK_DIR%"=="" goto :no_deploy

for %%I in ("%TSHOCK_DIR%") do (
    set "LEAF_NAME=%%~nxI"
    set "PARENT_DIR=%%~dpI"
)
if "%PARENT_DIR:~-1%"=="\" set "PARENT_DIR=%PARENT_DIR:~0,-1%"

if /I "%LEAF_NAME%"=="ServerPlugins" (
    set "PLUGINS_DIR=%TSHOCK_DIR%"
    set "TSHOCK_ROOT=%PARENT_DIR%"
) else (
    set "TSHOCK_ROOT=%TSHOCK_DIR%"
    set "PLUGINS_DIR=%TSHOCK_DIR%\ServerPlugins"
)

:check_running
tasklist /FI "IMAGENAME eq TShock.Server.exe" 2>nul | find /I "TShock.Server.exe" >nul
if not errorlevel 1 (
    echo [!] TShock.Server.exe masih berjalan. Ketik "exit" di console server dulu supaya DLL tidak terkunci.
    pause
    goto :check_running
)
tasklist /FI "IMAGENAME eq TerrariaServer.exe" 2>nul | find /I "TerrariaServer.exe" >nul
if not errorlevel 1 (
    echo [!] TerrariaServer.exe masih berjalan. Ketik "exit" di console server dulu supaya DLL tidak terkunci.
    pause
    goto :check_running
)

if not exist "%PLUGINS_DIR%" mkdir "%PLUGINS_DIR%"
echo [3/4] Menyalin plugin Univers.dll ke "%PLUGINS_DIR%"...
if exist "%PLUGINS_DIR%\Skynomi.dll" del /F /Q "%PLUGINS_DIR%\Skynomi.dll" >nul
if exist "%PLUGINS_DIR%\Skynomi.pdb" del /F /Q "%PLUGINS_DIR%\Skynomi.pdb" >nul
copy /Y "%OUT_DIR%\Univers.dll" "%PLUGINS_DIR%\" >nul
if exist "%OUT_DIR%\Univers.pdb" copy /Y "%OUT_DIR%\Univers.pdb" "%PLUGINS_DIR%\" >nul
if exist "%OUT_DIR%\LiteDB.dll" copy /Y "%OUT_DIR%\LiteDB.dll" "%PLUGINS_DIR%\" >nul
echo [ OK ] Univers.dll dan LiteDB.dll berhasil diperbarui di ServerPlugins.

set "SERVER_EXE="
if exist "%TSHOCK_ROOT%\TShock.Server.exe" set "SERVER_EXE=%TSHOCK_ROOT%\TShock.Server.exe"
if not defined SERVER_EXE if exist "%TSHOCK_ROOT%\TerrariaServer.exe" set "SERVER_EXE=%TSHOCK_ROOT%\TerrariaServer.exe"
if not defined SERVER_EXE (
    echo [4/4] Selesai! Jalankan server TShock kamu untuk mulai debug.
    goto :done
)

set "WORLD_FILE="
if exist "tshock_world.txt" set /p WORLD_FILE=<tshock_world.txt
if defined WORLD_FILE set "WORLD_FILE=%WORLD_FILE:"=%"
if defined WORLD_FILE if exist "%WORLD_FILE%" (
    echo [4/4] Menjalankan Master Hub (Port 7778) + Gateway Proxy (Port 7777)...
    start "TShock Hub (7778) - Univers MultiWorld" /D "%TSHOCK_ROOT%" "%SERVER_EXE%" -world "%WORLD_FILE%" -port 7778 -players 8 -noupnp
    goto :done
)

if exist "%TSHOCK_ROOT%\serverconfig.txt" (
    echo [4/4] Menjalankan Master Hub (Port 7778) + Gateway Proxy (Port 7777) via serverconfig.txt...
    start "TShock Hub (7778) - Univers MultiWorld" /D "%TSHOCK_ROOT%" "%SERVER_EXE%" -config "%TSHOCK_ROOT%\serverconfig.txt" -port 7778
    goto :done
)

set "AUTO_WORLD="
if exist "%TSHOCK_ROOT%\Worlds\*.wld" (
    for /f "delims=" %%W in ('dir /b /on "%TSHOCK_ROOT%\Worlds\*.wld" 2^>nul') do (
        if not defined AUTO_WORLD set "AUTO_WORLD=%TSHOCK_ROOT%\Worlds\%%W"
    )
)
if not defined AUTO_WORLD if exist "%USERPROFILE%\Documents\My Games\Terraria\Worlds\*.wld" (
    for /f "delims=" %%W in ('dir /b /on "%USERPROFILE%\Documents\My Games\Terraria\Worlds\*.wld" 2^>nul') do (
        if not defined AUTO_WORLD set "AUTO_WORLD=%USERPROFILE%\Documents\My Games\Terraria\Worlds\%%W"
    )
)

if defined AUTO_WORLD (
    echo [4/4] Menjalankan Master Hub (Port 7778) + Gateway Proxy (Port 7777) dengan World #1: "%AUTO_WORLD%"...
    start "TShock Hub (7778) - Univers MultiWorld" /D "%TSHOCK_ROOT%" "%SERVER_EXE%" -world "%AUTO_WORLD%" -port 7778 -players 8 -noupnp
    goto :done
)

echo [4/4] Membuka Master Hub TShock (Port 7778) + Gateway Proxy (Port 7777)...
start "TShock Hub (7778) - Univers MultiWorld" /D "%TSHOCK_ROOT%" "%SERVER_EXE%" -port 7778 -noupnp
goto :done

:no_deploy
echo [3/4] Build sukses! Output ada di: %OUT_DIR%\Univers.dll
echo [INFO] Buat file Univers\tshock_path.txt berisi path folder TShock kamu
echo        agar otomatis copy ke ServerPlugins dan auto-start server.

:done
popd
pause
