#!/bin/bash
# 构建常驻 bundle 筱义.app：Swift 菜单栏（菜单栏 + --notify 两个入口）。
# 用法：build-app.sh <输出目录> [ctl 可执行文件路径]
#
# daemon / shoyee-ctl / 桌面端的可执行文件由 install-app.sh 发布进同一个 bundle；
# 本地开发时也可以用 SHOYEE_CTL / SHOYEE_DAEMON 指到 bin/ 里的产物，不必装到 /Applications。
#
# 签名优先级：SHOYEE_CODESIGN_IDENTITY → 本机第一张 Apple Development 证书 → BorderTools Local Dev → ad-hoc。
# UNUserNotificationCenter 要求进程有 bundle id 且签名稳定，否则 TCC 每次都会当新应用。
set -euo pipefail

if ! command -v swiftc >/dev/null 2>&1; then
    echo "缺少 swiftc，跳过 筱义.app 构建（系统通知在运行时会报可操作错误）。"
    exit 0
fi

OUT_DIR="${1:?缺少输出目录}"
SOURCE_DIR="$(cd "$(dirname "$0")" && pwd)/Native"
APP="$OUT_DIR/筱义.app"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
# 入口必须是 main.swift（顶层代码），其余文件同批编译
swiftc -O "$SOURCE_DIR"/*.swift -o "$APP/Contents/MacOS/shoyee"
cp "$SOURCE_DIR/Info.plist" "$APP/Contents/Info.plist"
if [ -f "$SOURCE_DIR/Resources/Shoyee.icns" ]; then
    cp "$SOURCE_DIR/Resources/Shoyee.icns" "$APP/Contents/Resources/Shoyee.icns"
fi
if [ -f "$SOURCE_DIR/Resources/Secretary.icns" ]; then
    cp "$SOURCE_DIR/Resources/Secretary.icns" "$APP/Contents/Resources/Secretary.icns"
fi
# 菜单栏图标等零散资源
find "$SOURCE_DIR/Resources" -maxdepth 1 -name '*.png' -exec cp {} "$APP/Contents/Resources/" \; 2>/dev/null || true

# 可选：把已构建好的 shoyee-ctl 一起塞进来（install-app.sh 走这条）
# 参数给目录 = 框架依赖发布的整套文件（apphost + dll + runtimeconfig），只拷可执行文件跑不起来。
if [ "${2:-}" != "" ] && [ -d "$2" ]; then
    ditto "$2"/ "$APP/Contents/MacOS/"
    find "$APP/Contents/MacOS" -name '*.pdb' -delete
    chmod +x "$APP/Contents/MacOS/shoyee-ctl"
elif [ "${2:-}" != "" ] && [ -f "$2" ]; then
    cp "$2" "$APP/Contents/MacOS/shoyee-ctl"
    chmod +x "$APP/Contents/MacOS/shoyee-ctl"
fi

IDENTITY="${SHOYEE_CODESIGN_IDENTITY:-}"
if [ -z "$IDENTITY" ]; then
    IDENTITY="$(security find-identity -v -p codesigning 2>/dev/null \
        | sed -n 's/.*"\(Apple Development:[^"]*\)".*/\1/p' \
        | head -n 1)"
fi
if [ -z "$IDENTITY" ]; then
    # 本机没有 Apple 证书时，沿用与筱义.app 相同的自签名身份，至少保证 TCC 记录稳定。
    IDENTITY="$(security find-identity -v -p codesigning 2>/dev/null \
        | sed -n 's/.*"\(BorderTools Local Dev\)".*/\1/p' \
        | head -n 1)"
fi

if [ -n "$IDENTITY" ]; then
    codesign --force --deep --sign "$IDENTITY" "$APP" >/dev/null 2>&1 || true
else
    codesign --force --deep --sign - "$APP" >/dev/null 2>&1 || true
fi
