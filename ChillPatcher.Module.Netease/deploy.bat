@echo off
REM ChillPatcher.Module.Netease - Build and Deploy Script
REM 编译并部署网易云音乐模块

setlocal EnableDelayedExpansion

echo ========================================
echo 网易云音乐模块 - 编译部署脚本
echo ========================================

REM 切换到模块目录
cd /d %~dp0

REM 配置
set Configuration=Release
if not "%1"=="" set Configuration=%1

REM 当前模块目录
set ModuleDir=%~dp0
REM 项目根目录 (模块目录的父目录)
for %%i in ("%ModuleDir%..") do set ProjectRoot=%%~fi

REM 部署目标目录
set GamePath=F:\SteamLibrary\steamapps\common\wallpaper_engine\projects\myprojects\chill_with_you
set PluginPath=%GamePath%\BepInEx\plugins\ChillPatcher
REM 使用模块 ID 作为目录名 (与 DependencyLoader 期望的一致)
set ModuleId=com.chillpatcher.netease
set ModuleDeployPath=%PluginPath%\Modules\%ModuleId%
set ModuleNativePath=%ModuleDeployPath%\native\x64

REM 调试信息
echo.
echo 项目根目录: %ProjectRoot%
echo DLL 源文件: %ProjectRoot%\netease_bridge\ChillNetease.dll

REM 检查游戏目录是否存在
if not exist "%GamePath%" (
    echo ERROR: 游戏目录不存在: %GamePath%
    exit /b 1
)

REM ========== Step 1: 还原 NuGet 包 ==========
echo.
echo [1/4] 还原 NuGet 包...
dotnet restore ChillPatcher.Module.Netease.csproj
if %errorlevel% neq 0 (
    echo ERROR: NuGet 还原失败!
    exit /b 1
)

REM ========== Step 2: 编译模块 ==========
echo.
echo [2/4] 编译模块 (%Configuration%)...
dotnet build ChillPatcher.Module.Netease.csproj -c %Configuration% --no-restore
if %errorlevel% neq 0 (
    echo ERROR: 编译失败!
    exit /b 1
)

REM ========== Step 3: 创建部署目录 ==========
echo.
echo [3/5] 创建部署目录...
if not exist "%PluginPath%" (
    echo ERROR: ChillPatcher 主插件目录不存在: %PluginPath%
    echo 请先部署主插件!
    exit /b 1
)

if not exist "%ModuleDeployPath%" mkdir "%ModuleDeployPath%"
if not exist "%ModuleNativePath%" mkdir "%ModuleNativePath%"

REM ========== Step 4: 复制模块文件 ==========
echo.
echo [4/5] 复制模块文件...

REM 模块 DLL
echo   - ChillPatcher.Module.Netease.dll
copy /y "bin\ChillPatcher.Module.Netease.dll" "%ModuleDeployPath%\" >nul
if %errorlevel% neq 0 (
    echo ERROR: 复制模块 DLL 失败!
    exit /b 1
)

REM 依赖 (Newtonsoft.Json)
echo   - Newtonsoft.Json.dll
copy /y "bin\Newtonsoft.Json.dll" "%ModuleDeployPath%\" >nul 2>&1

REM ========== Step 5: 复制原生 DLL ==========
echo.
echo [5/5] 复制原生 DLL 到模块目录...

REM ChillNetease.dll (网易云桥接 DLL)
set NeteaseDllSource=%ProjectRoot%\netease_bridge\ChillNetease.dll
set NeteaseDllSource2=%ProjectRoot%\bin\native\x64\ChillNetease.dll

if exist "%NeteaseDllSource%" (
    echo   - ChillNetease.dll [从 netease_bridge]
    copy /y "%NeteaseDllSource%" "%ModuleNativePath%\" >nul
    goto :dllCopyDone
)

if exist "%NeteaseDllSource2%" (
    echo   - ChillNetease.dll [从 bin]
    copy /y "%NeteaseDllSource2%" "%ModuleNativePath%\" >nul
    goto :dllCopyDone
)

echo WARNING: ChillNetease.dll 未找到!
echo   请先运行 netease_bridge\build.bat

:dllCopyDone

echo.
echo ========================================
echo 部署完成!
echo ========================================
echo.
echo 模块目录: %ModuleDeployPath%
echo 目录内容:
dir /b "%ModuleDeployPath%"
echo.
echo 原生 DLL 目录: %ModuleNativePath%
if exist "%ModuleNativePath%\ChillNetease.dll" (
    echo   - ChillNetease.dll [OK]
) else (
    echo   - ChillNetease.dll [MISSING]
)
echo.
echo ========================================

endlocal
exit /b 0
