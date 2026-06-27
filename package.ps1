# FocusShield 打包脚本
# 功能: 打包为单文件EXE，输出到release文件夹，文件名包含版本号和时间戳

param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [bool]$SelfContained = $true
)

# 项目路径
$ProjectDir = "D:\GitWork\MMY_FocusShield\FocusShieldV3"
$ProjectFile = "FocusShield.csproj"
$OutputDir = "D:\GitWork\MMY_FocusShield\release"

# 读取版本号
$CSProjPath = Join-Path $ProjectDir $ProjectFile
[xml]$CSProj = Get-Content $CSProjPath
$Version = $CSProj.Project.PropertyGroup.Version
if (-not $Version) {
    $Version = "3.1.0"
}

# 生成时间戳
$Timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$OutputFileName = "FocusShield_v$($Version)_$($Timestamp).exe"
$OutputPath = Join-Path $OutputDir $OutputFileName

# 临时发布目录
$TempPublishDir = Join-Path $ProjectDir "temp_publish"
if (Test-Path $TempPublishDir) {
    Remove-Item -Path $TempPublishDir -Recurse -Force
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "FocusShield 打包脚本" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "版本号: $Version" -ForegroundColor Yellow
Write-Host "时间戳: $Timestamp" -ForegroundColor Yellow
Write-Host "输出文件: $OutputFileName" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Cyan

# 创建输出目录
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    Write-Host "✓ 创建输出目录: $OutputDir" -ForegroundColor Green
}

# 构建打包参数
$PublishArgs = @(
    "publish",
    "-c", $Configuration,
    "-r", $Runtime,
    "/p:PublishSingleFile=true",
    "/p:IncludeNativeLibrariesForSelfExtract=true",
    "/p:EnableCompressionInSingleFile=true",
    "-o", $TempPublishDir
)

if ($SelfContained) {
    $PublishArgs += "--self-contained"
    $PublishArgs += "true"
} else {
    $PublishArgs += "--self-contained"
    $PublishArgs += "false"
}

# 执行打包
Write-Host "`n开始打包..." -ForegroundColor Yellow
$ProjectPath = Join-Path $ProjectDir $ProjectFile
& dotnet $PublishArgs $ProjectPath

if ($LASTEXITCODE -ne 0) {
    Write-Host "✗ 打包失败！" -ForegroundColor Red
    exit 1
}

Write-Host "✓ 打包成功！" -ForegroundColor Green

# 查找生成的EXE文件
$PublishedExe = Get-ChildItem -Path $TempPublishDir -Filter "*.exe" | Where-Object { $_.Name -eq "FocusShield.exe" } | Select-Object -First 1

if (-not $PublishedExe) {
    Write-Host "✗ 未找到生成的EXE文件！" -ForegroundColor Red
    exit 1
}

# 复制到输出目录
Write-Host "`n复制文件到release目录..." -ForegroundColor Yellow
Copy-Item -Path $PublishedExe.FullName -Destination $OutputPath -Force

if (Test-Path $OutputPath) {
    $FileSize = (Get-Item $OutputPath).Length / 1MB
    Write-Host "✓ 文件已保存: $OutputPath" -ForegroundColor Green
    Write-Host "  文件大小: $([math]::Round($FileSize, 2)) MB" -ForegroundColor Cyan
} else {
    Write-Host "✗ 文件复制失败！" -ForegroundColor Red
    exit 1
}

# 清理临时目录
Write-Host "`n清理临时文件..." -ForegroundColor Yellow
if (Test-Path $TempPublishDir) {
    Remove-Item -Path $TempPublishDir -Recurse -Force
    Write-Host "✓ 临时文件已清理" -ForegroundColor Green
}

# 列出release目录中的所有文件
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "release 目录内容:" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Get-ChildItem -Path $OutputDir -Filter "*.exe" | Sort-Object LastWriteTime -Descending | Format-Table Name, @{Name="大小(MB)";Expression={[math]::Round($_.Length/1MB, 2)}}, LastWriteTime

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "打包完成！" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green

# 打开release目录
Start-Process $OutputDir
