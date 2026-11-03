#!/bin/bash
# 构建 Release 并把唯一的 App 安装到 /Applications/筱义.app。
# 用法：src/Shoyee.Menu/install-app.sh
#
# bundle 布局（一个 App，全部东西都装在里面）：
#   筱义.app/Contents/MacOS/shoyee-desktop          Avalonia 桌面端（CFBundleExecutable，按需启动）
#   筱义.app/Contents/MacOS/shoyee                  Swift 菜单栏（也是 --notify 入口）
#   筱义.app/Contents/MacOS/shoyee-ctl              菜单栏调用的轻量命令
#   筱义.app/Contents/Resources/daemon/             daemon 的完整构建输出（含 speech-stt）
#
# 只有一个登录项：com.shoyee.daemon（见 tools/install-launchd.sh）。菜单栏由 daemon 启动时拉起，
# 桌面端由菜单栏按需启动；桌面进程启动时把 activation policy 设成 regular，Dock 只在窗口打开时出现。
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
BUILD="$ROOT/src/Shoyee.Daemon/bin/Release/net10.0"
CTL_DIR="$ROOT/src/Shoyee.Ctl/bin/Release/net10.0"
CTL="$CTL_DIR/shoyee-ctl"
DESKTOP_TMP="$ROOT/src/Shoyee.Desktop/bin/Release/publish-osx"
APP="$BUILD/筱义.app"
DEST="/Applications/筱义.app"
OLD="/Applications/筱.app"
OLD_DESKTOP="/Applications/筱 桌面端.app"

dotnet build "$ROOT/src/Shoyee.Daemon/Shoyee.Daemon.csproj" -c Release
dotnet build "$ROOT/src/Shoyee.Ctl/Shoyee.Ctl.csproj" -c Release
rm -rf "$DESKTOP_TMP"
dotnet publish "$ROOT/src/Shoyee.Desktop/Shoyee.Desktop.csproj" \
    -c Release -r osx-arm64 --self-contained false -o "$DESKTOP_TMP"

if [ ! -f "$CTL" ]; then
    echo "构建后没有找到 $CTL" >&2
    exit 1
fi

# 先重建 bundle 外壳（Swift 菜单栏 + ctl）
bash "$ROOT/src/Shoyee.Menu/build-app.sh" "$BUILD" "$CTL_DIR"

if [ ! -d "$APP" ]; then
    echo "构建后没有找到 $APP" >&2
    exit 1
fi

# daemon 的完整构建输出进 bundle（apphost 需要同目录的 dll / runtimeconfig / 模板 / speech-stt）
mkdir -p "$APP/Contents/Resources/daemon"
ditto "$BUILD" "$APP/Contents/Resources/daemon"
# 构建输出里还带着它自己生成的 App，别让它递归进 bundle
rm -rf "$APP/Contents/Resources/daemon/筱义.app" "$APP/Contents/Resources/daemon/筱.app"

# 系统设置「后台项目」显示的是 bundle 显示名；可执行文件仍统一小写，避免进程名难看。
mv "$APP/Contents/Resources/daemon/Shoyee.Daemon" "$APP/Contents/Resources/daemon/shoyee-daemon"

# 桌面端 = 同一个 bundle 的主可执行文件：发布文件直接铺在 Contents/MacOS，
# 这样 LaunchServices 认的是 筱义.app（显示名 + 图标 + Dock 身份），不是裸可执行文件。
ditto "$DESKTOP_TMP"/ "$APP/Contents/MacOS"/
find "$APP/Contents/MacOS" -name '*.pdb' -delete
mv "$APP/Contents/MacOS/Shoyee.Desktop" "$APP/Contents/MacOS/shoyee-desktop"

# 上面复制 daemon/desktop 会让 build-app 的签名失效；整包再签一次
IDENTITY="${SHOYEE_CODESIGN_IDENTITY:-}"
if [ -z "$IDENTITY" ]; then
    IDENTITY="$(security find-identity -v -p codesigning 2>/dev/null \
        | sed -n 's/.*"\(Apple Development:[^"]*\)".*/\1/p' \
        | head -n 1)"
fi
if [ -z "$IDENTITY" ]; then
    IDENTITY="$(security find-identity -v -p codesigning 2>/dev/null \
        | sed -n 's/.*"\(BorderTools Local Dev\)".*/\1/p' \
        | head -n 1)"
fi
if [ -n "$IDENTITY" ]; then
    codesign --force --deep --sign "$IDENTITY" "$APP"
else
    codesign --force --deep --sign - "$APP"
fi

# 旧版本先挪到废纸篓（旧 Secretary 的 筱义.app、旧的 筱.app 与「筱 桌面端.app」）
mkdir -p "$HOME/.Trash"
for stale in "$DEST" "$OLD" "$OLD_DESKTOP"; do
    if [ -d "$stale" ]; then
        mv "$stale" "$HOME/.Trash/$(basename "$stale")-$(date +%Y%m%d%H%M%S)" 2>/dev/null || true
    fi
done

ditto "$APP" "$DEST"

/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$DEST"
echo "已安装：${DEST}（bundle id com.shoyee.app，显示名 筱义）"
