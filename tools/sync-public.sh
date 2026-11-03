#!/usr/bin/env bash
# 把当前仓库同步到公开快照（~/RiderProjects/Shoyee-Public）并推送。
#
# 公开仓库只放代码：开发文档、笔记与 ComfyUI 词表/工作流是私有的，不随快照走。
# 用法：tools/sync-public.sh ["提交说明"]
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PUBLIC="${SHOYEE_PUBLIC_DIR:-$ROOT/../Shoyee-Public}"
MESSAGE="${1:-sync: 同步公开快照}"

if [ ! -d "$PUBLIC/.git" ]; then
    echo "找不到公开快照仓库：$PUBLIC（先克隆 TreeBorder/Shoyee 到那里）" >&2
    exit 1
fi

rsync -a --delete \
    --exclude='.git' \
    --exclude='bin/' \
    --exclude='obj/' \
    --exclude='docs/' \
    --exclude='notes/' \
    --exclude='AGENTS.md' \
    --exclude='src/Shoyee.Photo/Templates/' \
    --exclude='tests/Shoyee.Photo.Tests/PhotoWorkspaceTemplatesTests.cs' \
    --exclude='tools/export-sample-data.py' \
    "$ROOT/" "$PUBLIC/"

cd "$PUBLIC"
git add -A
if git diff --cached --quiet; then
    echo "没有变化。"
    exit 0
fi

git commit -m "$MESSAGE"
# 本机到 GitHub 的 443 走 Clash；直连会卡在 HTTP2 framing。
HTTPS_PROXY="${HTTPS_PROXY:-http://127.0.0.1:7897}" \
    git -c http.proxy="${HTTPS_PROXY:-http://127.0.0.1:7897}" push origin main
