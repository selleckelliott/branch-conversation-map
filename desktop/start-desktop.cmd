@echo off
cd /d "%~dp0\.."
if not exist "desktop\Branch.Desktop\bin\Release\net8.0-windows\Branch.Desktop.exe" (
  dotnet build "desktop\Branch.Desktop\Branch.Desktop.csproj" -c Release --configfile "desktop\Branch.Desktop\NuGet.Config"
  if errorlevel 1 (
    echo Build failed. Install the .NET 8 SDK and try again.
    pause
    exit /b 1
  )
)
start "Branch" "desktop\Branch.Desktop\bin\Release\net8.0-windows\Branch.Desktop.exe"
