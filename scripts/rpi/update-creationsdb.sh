#!/usr/bin/env bash
# 創作DB（100BeautiesLab_CreationsDB）の日次更新スクリプト（Raspberry Pi側・ネイティブARM gitで実行）
# systemdタイマー等で毎日実行する想定（docs/raspberrypi-handoff.md 参照）。
# アプリ側は環境変数 NTSWE_CREATIONSDB がこのリポジトリ内の Works_NumberTales を指していれば、
# 毎日 dailyReloadHour（既定04時）に自動で再読込する。
set -euo pipefail

REPO_URL="https://github.com/radiann-kswg/100BeautiesLab_CreationsDB.git"
BRANCH="develop"
DEST="${CREATIONSDB_REPO_DIR:-/opt/ntswallpaper/creationsdb}"

if [ ! -d "$DEST/.git" ]; then
  echo "[update-creationsdb] initial sparse clone -> $DEST"
  git clone --filter=blob:none --sparse --depth 1 -b "$BRANCH" "$REPO_URL" "$DEST"
fi
cd "$DEST"

# sparse設定は毎回適用する（既存クローンにも新パターンを反映させるため冪等に実行）
git sparse-checkout set --no-cone \
  '/*.md' \
  '/LICENCE' \
  '/data/Dictionaries/**' \
  '/data/Works_NumberTales/DataBases/**' \
  '/data/Works_NumberTales/Dictionaries/**' \
  '/data/Works_NumberTales/Images/DB_Primary/corefolder/**' \
  '/data/Works_NumberTales/Images/DB_SemiPrimary/corefolder/**' \
  '/data/Works_NumberTales/Images/DB_SelfSecondary/corefolder/**'

echo "[update-creationsdb] pull $BRANCH"
git fetch origin "$BRANCH"
git pull origin "$BRANCH"

echo "[update-creationsdb] done: $(git rev-parse --short HEAD)"
