#!/bin/bash
echo "[Univers Build System] Building Univers.dll (.NET 9.0 Release)..."
dotnet build Univers/Univers.csproj -c Release
if [ $? -eq 0 ]; then
    echo "[ SUCCESS ] Build berhasil!"
    TARGET_DIR="/home/ubuntu/Univers/ServerPlugins"
    mkdir -p "$TARGET_DIR"
    cp Univers/bin/Release/net9.0/Univers.dll "$TARGET_DIR/Univers.dll"
    echo "[ DEPLOY ] Univers.dll berhasil disalin otomatis ke: $TARGET_DIR/Univers.dll"
else
    echo "[ ERROR ] Build gagal! Periksa error di atas."
    exit 1
fi
