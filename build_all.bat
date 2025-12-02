@echo off
REM ChillPatcher - Complete Build Script
REM Builds both Native Plugins and C# Plugin

setlocal

echo ========================================
echo ChillPatcher Complete Build Script
echo ========================================

REM 切换到项目根目录
cd /d %~dp0

REM ========== Step 1: Build Native FLAC Decoder ==========
echo.
echo [1/3] Building Native FLAC Decoder...
cd NativePlugins\FlacDecoder
call build.bat
if %errorlevel% neq 0 (
    echo ERROR: Native FLAC decoder build failed!
    exit /b 1
)
cd ..\..

REM ========== Step 2: Build Native SMTC Bridge ==========
echo.
echo [2/3] Building Native SMTC Bridge...
cd NativePlugins\SmtcBridge
call build.bat
if %errorlevel% neq 0 (
    echo WARNING: Native SMTC bridge build failed (optional feature)
    echo Continuing with C# build...
)
cd ..\..

REM ========== Step 3: Build C# Plugin ==========
echo.
echo [3/3] Building C# Plugin...
dotnet build -c Release
if %errorlevel% neq 0 (
    echo ERROR: C# build failed!
    exit /b 1
)

echo.
echo ========================================
echo Build Complete!
echo ========================================
echo.
echo Outputs:
echo   - ChillPatcher.dll: bin\ChillPatcher.dll
echo   - Native x64:       ^<game^>\BepInEx\plugins\ChillPatcher\native\x64\ChillFlacDecoder.dll
echo   - Native x64:       ^<game^>\BepInEx\plugins\ChillPatcher\native\x64\ChillSmtcBridge.dll
echo.
echo Next: Copy to game directory or run the game to test!
echo ========================================

exit /b 0
