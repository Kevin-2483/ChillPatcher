@echo off
setlocal EnableDelayedExpansion

:: ChillNetease.dll Build Script
:: This script clones go-musicfox, copies netease_bridge, and builds the DLL

:: 设置 Go 和 GCC 路径
set "PATH=C:\Program Files\Go\bin;C:\TDM-GCC-64\bin;%PATH%"
set CGO_ENABLED=1
set GOOS=windows
set GOARCH=amd64

:: 配置
set "SCRIPT_DIR=%~dp0"
set "PROJECT_ROOT=%SCRIPT_DIR%.."
set "BUILD_DIR=%PROJECT_ROOT%\build\netease_bridge"
set "GOMUSICFOX_URL=https://github.com/go-musicfox/go-musicfox.git"
set "GOMUSICFOX_BRANCH=master"
set "OUTPUT_DIR=%PROJECT_ROOT%\bin\native\x64"

echo ===========================================
echo ChillNetease.dll Build Script
echo ===========================================
echo.

:: 检查 Go 版本
echo [1/5] Checking Go version...
go version
if errorlevel 1 (
    echo ERROR: Go not found! Please install Go from https://go.dev/
    exit /b 1
)

:: 检查 GCC 版本
echo.
echo [2/5] Checking GCC version...
gcc --version | findstr "gcc"
if errorlevel 1 (
    echo ERROR: GCC not found! Please install TDM-GCC or MinGW-w64
    exit /b 1
)

:: 检查 Git
echo.
echo [3/5] Checking Git...
git --version | findstr "git"
if errorlevel 1 (
    echo ERROR: Git not found! Please install Git
    exit /b 1
)

:: 准备构建目录
echo.
echo [4/5] Preparing build directory...
if not exist "%BUILD_DIR%" mkdir "%BUILD_DIR%"
cd /d "%BUILD_DIR%"

:: 克隆或更新 go-musicfox
echo.
if exist "go-musicfox\.git" (
    echo Updating go-musicfox repository...
    cd go-musicfox
    git pull --ff-only
    if errorlevel 1 (
        echo WARNING: git pull failed, continuing with existing code...
    )
    cd ..
) else (
    echo Cloning go-musicfox repository...
    if exist "go-musicfox" rmdir /s /q "go-musicfox"
    git clone --depth 1 --branch %GOMUSICFOX_BRANCH% %GOMUSICFOX_URL% go-musicfox
    if errorlevel 1 (
        echo ERROR: Failed to clone go-musicfox!
        exit /b 1
    )
)

:: 复制我们的 netease_bridge 代码
echo.
echo Copying netease_bridge source code...
if not exist "go-musicfox\netease_bridge" mkdir "go-musicfox\netease_bridge"
copy /Y "%SCRIPT_DIR%*.go" "go-musicfox\netease_bridge\" >nul
copy /Y "%SCRIPT_DIR%.gitignore" "go-musicfox\netease_bridge\" 2>nul

:: 进入 go-musicfox 目录
cd go-musicfox

:: 构建 DLL
echo.
echo [5/5] Building DLL...
go build -buildmode=c-shared -o netease_bridge\ChillNetease.dll -ldflags "-s -w" ./netease_bridge

if errorlevel 1 (
    echo.
    echo ERROR: Build failed!
    exit /b 1
)

echo.
echo ===========================================
echo Build successful!
echo ===========================================

:: 复制输出文件
echo.
echo Copying output files...
if not exist "%OUTPUT_DIR%" mkdir "%OUTPUT_DIR%"

copy /Y "netease_bridge\ChillNetease.dll" "%OUTPUT_DIR%\"
copy /Y "netease_bridge\ChillNetease.h" "%OUTPUT_DIR%\" 2>nul

:: 同时复制到 netease_bridge 目录（方便调试）
copy /Y "netease_bridge\ChillNetease.dll" "%SCRIPT_DIR%\"
copy /Y "netease_bridge\ChillNetease.h" "%SCRIPT_DIR%\" 2>nul

echo.
echo Output files:
echo   - %OUTPUT_DIR%\ChillNetease.dll
echo   - %SCRIPT_DIR%ChillNetease.dll (debug copy)
echo.

:: 清理构建目录 (可选，取消注释以启用)
:: echo Cleaning up build directory...
:: cd /d "%PROJECT_ROOT%"
:: rmdir /s /q "%BUILD_DIR%"

echo Done!
pause
