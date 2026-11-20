#!/bin/bash
# 悬停翻译 macOS 版构建脚本
# 用法：在终端里执行  bash build.sh
# 需要 Xcode Command Line Tools（终端里执行 xcode-select --install 安装）

set -euo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_NAME="HoverTranslate"
BUILD="$DIR/build"
APP="$BUILD/$APP_NAME.app"

echo "==> 检查编译环境"
if ! command -v swiftc >/dev/null 2>&1; then
  echo "找不到 swiftc。请先在终端执行：xcode-select --install"
  echo "装完后重新运行本脚本。"
  exit 1
fi
swiftc --version | head -n 1

echo "==> 清理旧产物"
rm -rf "$BUILD"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

echo "==> 编译 Swift 源码"
swiftc -O \
  -o "$APP/Contents/MacOS/$APP_NAME" \
  "$DIR/Sources/"*.swift \
  -framework Cocoa -framework ApplicationServices

echo "==> 写入 Info.plist"
cp "$DIR/Info.plist" "$APP/Contents/Info.plist"

if [ -f "$DIR/icon.png" ]; then
  echo "==> 生成应用图标"
  ICONSET="$BUILD/AppIcon.iconset"
  mkdir -p "$ICONSET"
  for s in 16 32 128 256 512; do
    sips -z "$s" "$s" "$DIR/icon.png" --out "$ICONSET/icon_${s}x${s}.png" >/dev/null 2>&1
    d=$((s * 2))
    sips -z "$d" "$d" "$DIR/icon.png" --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null 2>&1
  done
  iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns" 2>/dev/null || true
  rm -rf "$ICONSET"
fi

echo "==> ad-hoc 签名"
codesign --force --deep --sign - "$APP" 2>/dev/null || echo "跳过签名（本机运行不受影响）"

echo ""
echo "==> 构建完成：$APP"
echo ""
echo "下一步："
echo "  1. open \"$BUILD\"  把 HoverTranslate.app 拖进「应用程序」"
echo "  2. 双击启动，菜单栏出现气泡图标"
echo "  3. 首次使用会提示授权：系统设置 → 隐私与安全性 → 辅助功能，勾选 HoverTranslate"
echo ""
