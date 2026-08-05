#!/usr/bin/env bash
# NTsWallpaperEngine: 100BeautiesLab_CreationsDB サブモジュールの初期化 + sparse-checkout 設定
# 使い方: リポジトリルートで `bash scripts/setup-submodule.sh`
set -euo pipefail
cd "$(dirname "$0")/.."

git submodule update --init --depth 1 100BeautiesLab_CreationsDB

cd 100BeautiesLab_CreationsDB
git sparse-checkout set --no-cone \
  '/*.md' \
  '/LICENCE' \
  '/data/Works_NumberTales/DataBases/**' \
  '/data/Works_NumberTales/RoleplayPrompts/**' \
  '/data/Works_NumberTales/Images/DB_Primary/corefolder/**' \
  '/data/Works_NumberTales/Images/DB_SemiPrimary/corefolder/**' \
  '/data/Works_NumberTales/Images/DB_SelfSecondary/corefolder/**'

echo "OK: sparse-checkout configured (Works_NumberTales: DataBases + corefolder images + RoleplayPrompts)"
