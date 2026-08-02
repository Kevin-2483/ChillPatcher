@echo off
setlocal
cd /d "%~dp0"
set CGO_ENABLED=1
set GOOS=windows
set GOARCH=amd64
if not defined CC where gcc >nul 2>&1 && set CC=gcc
go build -buildmode=c-shared -trimpath -ldflags "-s -w" -o ChillKugou.dll .
if errorlevel 1 exit /b 1
if not exist "..\..\OmniMixPlayer\modules\Kugou\native\x64" mkdir "..\..\OmniMixPlayer\modules\Kugou\native\x64"
copy /y ChillKugou.dll "..\..\OmniMixPlayer\modules\Kugou\native\x64\ChillKugou.dll" >nul
exit /b 0
