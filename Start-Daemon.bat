@echo off
start "" wscript.exe "%~dp0start-hotkey-daemon.vbs"
echo Global Hotkey Daemon started in background.
timeout /t 2 >nul
