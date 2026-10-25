@echo off
setlocal
cd /d "%~dp0"

if exist KeyRemapper.exe goto run

echo Building Key Remapper (first run only, takes a few seconds)...
set "CSC=%windir%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%windir%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" goto nocsc

"%CSC%" /nologo /target:winexe /out:KeyRemapper.exe /win32manifest:app.manifest /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll KeyRemapper.cs
if errorlevel 1 goto buildfail

:run
start "" "%~dp0KeyRemapper.exe"
exit /b 0

:nocsc
echo The .NET Framework compiler was not found on this PC.
pause
exit /b 1

:buildfail
echo.
echo Build failed. Please copy the messages above and send them to whoever is helping you.
pause
exit /b 1
