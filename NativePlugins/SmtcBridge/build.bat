@echo off
REM ChillPatcher SMTC Bridge - Build Script for Windows
REM Builds x64 version (SMTC only available on 64-bit Windows 10+)

setlocal

set BUILD_DIR=%~dp0build
set INSTALL_DIR=%~dp0..\..\bin\native

echo ========================================
echo ChillPatcher SMTC Bridge Build Script
echo ========================================

REM 检查 CMake
where cmake >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: CMake not found in PATH
    echo Please install CMake: https://cmake.org/download/
    exit /b 1
)

REM 检查 Visual Studio
where cl >nul 2>&1
if %errorlevel% neq 0 (
    echo Searching for Visual Studio...
    call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvarsall.bat" x64 >nul 2>&1
    if %errorlevel% neq 0 (
        call "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\VC\Auxiliary\Build\vcvarsall.bat" x64 >nul 2>&1
    )
)

REM ========== 构建 x64 版本 ==========
echo.
echo Building x64 version...
set BUILD_X64=%BUILD_DIR%\x64
mkdir "%BUILD_X64%" 2>nul
cd /d "%BUILD_X64%"

cmake -A x64 -DCMAKE_BUILD_TYPE=Release ../..
if %errorlevel% neq 0 (
    echo ERROR: CMake configuration failed for x64
    exit /b 1
)

cmake --build . --config Release
if %errorlevel% neq 0 (
    echo ERROR: Build failed for x64
    exit /b 1
)

echo x64 build successful!

REM ========== 复制到目标目录 ==========
echo.
echo Installing binaries...
set OUTPUT_DIR=%~dp0..\..\bin\native
mkdir "%OUTPUT_DIR%\x64" 2>nul

copy /Y "%BUILD_X64%\bin\Release\ChillSmtcBridge.dll" "%OUTPUT_DIR%\x64\" >nul

echo.
echo ========================================
echo Build Complete!
echo ========================================
echo x64 DLL: %OUTPUT_DIR%\x64\ChillSmtcBridge.dll
echo ========================================

cd /d %~dp0
exit /b 0
