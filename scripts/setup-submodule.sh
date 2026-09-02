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
  '/data/Dictionaries/**' \
  '/data/Works_NumberTales/DataBases/**' \
  '/data/Works_NumberTales/Dictionaries/**' \
  '/data/Works_NumberTales/RoleplayPrompts/**' \
  '/data/Works_NumberTales/Images/DB_Primary/corefolder/**' \
  '/data/Works_NumberTales/Images/DB_SemiPrimary/corefolder/**' \
  '/data/Works_NumberTales/Images/DB_SelfSecondary/corefolder/**'

echo "OK: sparse-checkout configured (Works_NumberTales: DataBases + corefolder images + RoleplayPrompts)"

# PenchantManufacture フォント（正はサブモジュール。Assets 側 .otf は同期コピー）
cd ..
git submodule update --init --depth 1 PenchantManufacture_ImageAssets
cd PenchantManufacture_ImageAssets
git sparse-checkout set --no-cone '/*.md' '/LICENSE' '/assets/fonts/**'
cd ..
cp -f PenchantManufacture_ImageAssets/assets/fonts/PenchantManufacture.otf Assets/Fonts/penchant-manufactuer/PenchantManufacture.otf
echo "OK: PenchantManufacture.otf synced from submodule"
