#!/usr/bin/env bash
# macOS 只发布一个 App：筱义.app（Swift 菜单栏 + daemon + ctl + 桌面端）。
# 以前这里发布独立的「筱 桌面端.app」，现在桌面端是同一个 bundle 的主可执行文件（Contents/MacOS/shoyee-desktop）。
# 用法：tools/publish-macos.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exec bash "$ROOT/src/Shoyee.Menu/install-app.sh"
