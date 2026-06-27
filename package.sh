#!/bin/bash
# FocusShield 打包脚本 (Bash版本)
# 功能: 打包为单文件EXE，输出到release文件夹

set -e

# 配置
PROJECT_DIR="D:/GitWork/MMY_FocusShield/FocusShieldV3"
RELEASE_DIR="D:/GitWork/MMY_FocusShield/release"
VERSION="3.1.0"
TIMESTAMP=$(date +"%Y%m%d_%H%M%S")
OUTPUT_NAME="FocusShield_v${VERSION}_${TIMESTAMP}.exe"
TEMP_DIR="D:/GitWork/MMY_FocusShield/FocusShieldV3/temp_publish"

echo "========================================"
echo "FocusShield 打包脚本"
echo "========================================"
echo "版本号: $VERSION"
echo "时间戳: $TIMESTAMP"
echo "输出文件: $OUTPUT_NAME"
echo "========================================"

# 创建release目录
if [ ! -d "$RELEASE_DIR" ]; then
    mkdir -p "$RELEASE_DIR"
    echo "✓ 创建release目录"
fi

# 清理临时目录
if [ -d "$TEMP_DIR" ]; then
    rm -rf "$TEMP_DIR"
fi

# 打包命令
echo ""
echo "开始打包..."
cd "$PROJECT_DIR"

dotnet publish -c Release -r win-x64 \
    --self-contained true \
    /p:PublishSingleFile=true \
    /p:IncludeNativeLibrariesForSelfExtract=true \
    /p:EnableCompressionInSingleFile=true \
    -o "$TEMP_DIR"

if [ $? -ne 0 ]; then
    echo "✗ 打包失败！"
    exit 1
fi

echo "✓ 打包成功！"

# 查找EXE文件
EXE_PATH="$TEMP_DIR/FocusShield.exe"
if [ ! -f "$EXE_PATH" ]; then
    echo "✗ 未找到生成的EXE文件！"
    exit 1
fi

# 复制到release目录
echo ""
echo "复制文件到release目录..."
cp "$EXE_PATH" "$RELEASE_DIR/$OUTPUT_NAME"

if [ -f "$RELEASE_DIR/$OUTPUT_NAME" ]; then
    FILE_SIZE=$(du -h "$RELEASE_DIR/$OUTPUT_NAME" | cut -f1)
    echo "✓ 文件已保存: $RELEASE_DIR/$OUTPUT_NAME"
    echo "  文件大小: $FILE_SIZE"
else
    echo "✗ 文件复制失败！"
    exit 1
fi

# 清理临时目录
echo ""
echo "清理临时文件..."
if [ -d "$TEMP_DIR" ]; then
    rm -rf "$TEMP_DIR"
    echo "✓ 临时文件已清理"
fi

# 列出release目录
echo ""
echo "========================================"
echo "release 目录内容:"
echo "========================================"
ls -lh "$RELEASE_DIR"/*.exe 2>/dev/null || echo "无EXE文件"

echo ""
echo "========================================"
echo "打包完成！"
echo "========================================"

# 打开release目录 (Windows)
explorer "$RELEASE_DIR" 2>/dev/null || true
