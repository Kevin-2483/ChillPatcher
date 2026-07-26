@echo off
REM Chill 项目统一构建入口
REM 用法: build <命令> [选项]
REM
REM 命令:
REM   mod        构建 ChillPatcher BepInEx 插件
REM   player     构建 OmniMixPlayer 播放器
REM   all        构建全部
REM
REM 选项:
REM   --full     完整构建 (clean + restore + 原生插件)
REM   --skip-flutter  跳过快闪 GUI

python "%~dp0scripts\ci_build.py" %*
if %errorlevel% neq 0 (
    echo.
    echo Build failed!
    pause
    exit /b %errorlevel%
)
