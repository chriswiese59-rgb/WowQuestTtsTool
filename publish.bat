@echo off
REM ============================================================
REM WowQuestTtsTool - Release Publish Script
REM ============================================================
REM Erstellt eine self-contained Windows-EXE mit allen DLLs
REM ============================================================

setlocal

REM Konfiguration - HIER ANPASSEN:
set "PUBLISH_DIR=D:\Tools\WowQuestTtsTool"
set "PROJECT_DIR=%~dp0"

echo.
echo ============================================================
echo   WowQuestTtsTool - Release Build
echo ============================================================
echo.
echo Zielverzeichnis: %PUBLISH_DIR%
echo.

REM Pruefen ob dotnet verfuegbar ist
where dotnet >nul 2>&1
if %errorlevel% neq 0 (
    echo FEHLER: dotnet wurde nicht gefunden!
    echo Bitte installiere das .NET SDK von: https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

REM Alte Publish-Dateien loeschen (optional)
if exist "%PUBLISH_DIR%" (
    echo Loesche alte Version...
    rmdir /s /q "%PUBLISH_DIR%" 2>nul
)

echo.
echo Starte Build und Publish...
echo.

REM Publish-Befehl
dotnet publish "%PROJECT_DIR%WowQuestTtsTool.csproj" ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=false ^
    -p:PublishReadyToRun=true ^
    -o "%PUBLISH_DIR%"

if %errorlevel% neq 0 (
    echo.
    echo ============================================================
    echo   FEHLER: Build fehlgeschlagen!
    echo ============================================================
    pause
    exit /b 1
)

REM Erstelle notwendige Unterordner
echo.
echo Erstelle Unterordner...
if not exist "%PUBLISH_DIR%\data" mkdir "%PUBLISH_DIR%\data"
if not exist "%PUBLISH_DIR%\config" mkdir "%PUBLISH_DIR%\config"
if not exist "%PUBLISH_DIR%\audio\deDE" mkdir "%PUBLISH_DIR%\audio\deDE"

echo.
echo ============================================================
echo   BUILD ERFOLGREICH!
echo ============================================================
echo.
echo Die EXE befindet sich in: %PUBLISH_DIR%
echo.
echo Du kannst die App jetzt starten mit:
echo   %PUBLISH_DIR%\WowQuestTtsTool.exe
echo.
echo ============================================================

REM Optional: Ordner oeffnen
set /p OPEN_FOLDER="Ordner im Explorer oeffnen? (j/n): "
if /i "%OPEN_FOLDER%"=="j" (
    explorer "%PUBLISH_DIR%"
)

endlocal
pause
