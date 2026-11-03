#!/usr/bin/env bash
# 安装唯一的常驻 LaunchAgent：daemon（com.shoyee.daemon）。
# 前置：src/Shoyee.Menu/install-app.sh 已把 筱义.app 装到 /Applications。
# 用法：tools/install-launchd.sh [工作区路径]
#   工作区默认从 ~/Library/Application Support/Shoyee/config.json 的 workspace 取。
#
# 只有一个登录项：菜单栏由 daemon 启动时拉起，桌面端由菜单栏按需启动。
# AssociatedBundleIdentifiers 让系统设置「后台 App 活动」把这条归到 筱义.app 名下（显示名 + 图标）。
set -euo pipefail

APP="/Applications/筱义.app"
DAEMON="$APP/Contents/Resources/daemon/shoyee-daemon"
HOME_DIR="$HOME/Library/Application Support/Shoyee"
LOG_DIR="$HOME_DIR/logs"
AGENTS="$HOME/Library/LaunchAgents"

if [[ ! -x "$DAEMON" ]]; then
  echo "缺少 $DAEMON；先跑 src/Shoyee.Menu/install-app.sh" >&2
  exit 1
fi

WORKSPACE="${1:-${SHOYEE_WORKSPACE:-}}"
if [[ -z "$WORKSPACE" ]]; then
  WORKSPACE="$(plutil -extract workspace raw "$HOME_DIR/config.json" 2>/dev/null || true)"
fi
if [[ -z "$WORKSPACE" || ! -d "$WORKSPACE" ]]; then
  echo "找不到工作区（config.json 的 workspace 为空或目录不存在）：$WORKSPACE" >&2
  exit 1
fi

mkdir -p "$LOG_DIR" "$AGENTS"

# 与 Secretary 的 plist 同一套 PATH 策略：登录 profile 之外的进程也要能找到 rg / node。
PATH_VALUE="$HOME/.nvm/current/bin:$HOME/.local/bin:/opt/homebrew/bin:/opt/homebrew/sbin:/usr/local/bin:/usr/local/sbin:/usr/local/share/dotnet:$HOME/.dotnet/tools:$HOME/.cargo/bin:/opt/homebrew/opt/python@3.14/libexec/bin:/usr/bin:/bin:/usr/sbin:/sbin"

cat > "$AGENTS/com.shoyee.daemon.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>com.shoyee.daemon</string>
  <key>ProgramArguments</key>
  <array><string>$DAEMON</string></array>
  <key>WorkingDirectory</key><string>$WORKSPACE</string>
  <!-- 归到 筱义.app 名下，后台 App 活动显示应用名和图标 -->
  <key>AssociatedBundleIdentifiers</key><array><string>com.shoyee.app</string></array>
  <key>RunAtLoad</key><true/>
  <!-- 正常退出（退出码 0，含菜单栏「停止 daemon」）不重启；崩溃/信号才拉起 -->
  <key>KeepAlive</key><dict><key>SuccessfulExit</key><false/></dict>
  <key>ThrottleInterval</key><integer>30</integer>
  <key>StandardOutPath</key><string>$LOG_DIR/daemon.log</string>
  <key>StandardErrorPath</key><string>$LOG_DIR/daemon.log</string>
  <key>EnvironmentVariables</key>
  <dict><key>PATH</key><string>$PATH_VALUE</string></dict>
</dict>
</plist>
PLIST
chmod 644 "$AGENTS/com.shoyee.daemon.plist"

# 清掉旧的双任务方案：菜单栏不再单独注册
launchctl bootout "gui/$(id -u)/com.shoyee.menu" >/dev/null 2>&1 || true
rm -f "$AGENTS/com.shoyee.menu.plist"

launchctl bootout "gui/$(id -u)/com.shoyee.daemon" >/dev/null 2>&1 || true
# bootout 是异步的；没等它真正卸载就 bootstrap 会报 EIO
for _ in $(seq 1 30); do
  launchctl print "gui/$(id -u)/com.shoyee.daemon" >/dev/null 2>&1 || break
  sleep 0.2
done
launchctl bootstrap "gui/$(id -u)" "$AGENTS/com.shoyee.daemon.plist"

echo "已安装 LaunchAgent："
echo "  daemon  com.shoyee.daemon → $DAEMON（工作区 $WORKSPACE）"
echo "  菜单栏由 daemon 启动时拉起；桌面端从菜单栏打开"
echo "日志：$LOG_DIR/daemon.log"
echo "查看：launchctl list | grep com.shoyee"
