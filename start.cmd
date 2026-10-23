@echo off
cd /d "%~dp0"
echo Starting Branch at http://127.0.0.1:5180
echo Keep this window open while using Branch. Press Ctrl+C to stop.
node server.cjs 5180
if errorlevel 1 pause
