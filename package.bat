@echo off
chcp 65001 >nul
echo ========================================
echo FocusShield 打包工具
echo ========================================

set VERSION=3.1.0
set TIMESTAMP=%date:~0,4%%date:~5,2%%date:~8,2%_%time:~0,2%%time:~3,2%%time:~6,2%
set TIMESTAMP=%TIMESTAMP: =0%
set OUTPUT_NAME=FocusShield_v%VERSION%_%TIMESTAMP%.exe
set RELEASE_DIR=D:\GitWork\MMY_FocusShield\release
set TEMP_DIR=D:\GitWork\MMY_FocusShield\FocusShieldV3\temp_publish

echo 版本号: %VERSION%
echo 时间戳: %TIMESTAMP%
echo 输出文件: %OUTPUT_NAME%
echo ========================================

if not exist "%RELEASE_DIR%" mkdir "%RELEASE_DIR%"
if exist "%TEMP_DIR%" rmdir /s /q "%TEMP_DIR%"

echo.
echo 开始打包...
cd /d D:\GitWork\MMY_FocusShield\FocusShieldV3

dotnet publish -c Release -r win-x64 --self-contained true ^
    /p:PublishSingleFile=true ^
    /p:IncludeNativeLibrariesForSelfExtract=true ^
    /p:EnableCompressionInSingleFile=true ^
    -o "%TEMP_DIR%"

if %errorlevel% neq 0 (
    echo.
    echo ✗ 打包失败！
    pause
    exit /b 1
)

echo ✓ 打包成功！

echo.
echo 复制文件到release目录...
copy "%TEMP_DIR%\FocusShield.exe" "%RELEASE_DIR%\%OUTPUT_NAME%" >nul

if exist "%RELEASE_DIR%\%OUTPUT_NAME%" (
    echo ✓ 文件已保存: %RELEASE_DIR%\%OUTPUT_NAME%
    for %%A in ("%RELEASE_DIR%\%OUTPUT_NAME%") do echo   文件大小: %%~zA bytes
) else (
    echo ✗ 文件复制失败！
    pause
    exit /b 1
)

echo.
echo 清理临时文件...
if exist "%TEMP_DIR%" rmdir /s /q "%TEMP_DIR%"
echo ✓ 临时文件已清理

echo.
echo ========================================
echo release 目录内容:
echo ========================================
dir "%RELEASE_DIR%\*.exe" | findstr /r ".exe"

echo.
echo ========================================
echo 打包完成！
echo ========================================
echo.
echo 打开release目录...
explorer "%RELEASE_DIR%"

pause
