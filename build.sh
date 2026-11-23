#!/bin/bash

# Auto-detect dotnet in common Linux installation paths if not in PATH
if ! command -v dotnet &> /dev/null; then
    if [ -f "$HOME/.dotnet/dotnet" ]; then
        export DOTNET_ROOT="$HOME/.dotnet"
        export PATH="$HOME/.dotnet:$PATH"
    elif [ -f "/usr/share/dotnet/dotnet" ]; then
        export DOTNET_ROOT="/usr/share/dotnet"
        export PATH="/usr/share/dotnet:$PATH"
    elif [ -f "/snap/bin/dotnet" ]; then
        export PATH="/snap/bin:$PATH"
    elif [ -f "/root/.dotnet/dotnet" ]; then
        export DOTNET_ROOT="/root/.dotnet"
        export PATH="/root/.dotnet:$PATH"
    fi
fi

if ! command -v dotnet &> /dev/null; then
    echo "[ ERROR ] dotnet CLI tidak ditemukan di sistem!"
    echo "[ PANDUAN ] Untuk menginstall .NET 9.0 SDK di VPS Ubuntu/Debian, jalankan perintah ini di terminal VPS:"
    echo "  curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 9.0"
    echo "  export PATH=\"\$HOME/.dotnet:\$PATH\""
    echo "  echo 'export PATH=\"\$HOME/.dotnet:\$PATH\"' >> ~/.bashrc"
    echo "Lalu jalankan ./build.sh kembali."
    exit 1
fi

echo "[Univers Build System] Building Univers.dll (.NET 9.0 Release)..."
dotnet build Univers/Univers.csproj -c Release
if [ $? -eq 0 ]; then
    echo "[ SUCCESS ] Build berhasil!"
    TARGET_DIR="/home/ubuntu/Univers/ServerPlugins"
    mkdir -p "$TARGET_DIR" 2>/dev/null || true
    if [ -d "$TARGET_DIR" ]; then
        cp Univers/bin/Release/net9.0/Univers.dll "$TARGET_DIR/Univers.dll"
        echo "[ DEPLOY ] Univers.dll berhasil disalin otomatis ke: $TARGET_DIR/Univers.dll"
    else
        echo "[ INFO ] File output: Univers/bin/Release/net9.0/Univers.dll"
    fi
else
    echo "[ ERROR ] Build gagal! Periksa error di atas."
    exit 1
fi

