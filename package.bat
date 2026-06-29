@echo off
chcp 65001 >nul
echo ========================================
echo FocusShield Packaging Tool
echo ========================================

set VERSION=3.1.0
set TIMESTAMP=%date:~0,4%%date:~5,2%%date:~8,2%_%time:~0,2%%time:~3,2%%time:~6,2%
set TIMESTAMP=%TIMESTAMP: =0%
set OUTPUT_NAME=FocusShield_v%VERSION%_%TIMESTAMP%.exe
set RELEASE_DIR=D:\GitWork\MMY_FocusShield\release
set PROJECT_DIR=D:\GitWork\MMY_FocusShield\FocusShieldV3
set TEMP_DIR=%PROJECT_DIR%\temp_publish

echo Version: %VERSION%
echo Timestamp: %TIMESTAMP%
echo Output: %OUTPUT_NAME%
echo ========================================

if not exist "%RELEASE_DIR%" mkdir "%RELEASE_DIR%"
if exist "%TEMP_DIR%" rmdir /s /q "%TEMP_DIR%"

echo.
echo Building...
cd /d "%PROJECT_DIR%"

dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "%TEMP_DIR%"

if %errorlevel% neq 0 (
    echo.
    echo Build failed!
    pause
    exit /b 1
)

echo Build succeeded!

echo.
echo Copying to release...
copy "%TEMP_DIR%\FocusShield.exe" "%RELEASE_DIR%\%OUTPUT_NAME%" >nul

if exist "%RELEASE_DIR%\%OUTPUT_NAME%" (
    echo File saved: %RELEASE_DIR%\%OUTPUT_NAME%
) else (
    echo Copy failed!
    pause
    exit /b 1
)

echo.
echo Cleaning up...
if exist "%TEMP_DIR%" rmdir /s /q "%TEMP_DIR%"
echo Cleanup done.

echo.
echo ========================================
echo Release files:
echo ========================================
dir "%RELEASE_DIR%\*.exe" /b

echo.
echo ========================================
echo Done!
echo ========================================
echo.
explorer "%RELEASE_DIR%"
pause
