# NTsWallpaperEngine: 100BeautiesLab_CreationsDB サブモジュールの初期化 + sparse-checkout 設定
# 使い方: リポジトリルートで `pwsh -File scripts/setup-submodule.ps1`
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

git submodule update --init --depth 1 100BeautiesLab_CreationsDB

Set-Location 100BeautiesLab_CreationsDB
git sparse-checkout set --no-cone `
  '/*.md' `
  '/LICENCE' `
  '/data/Dictionaries/**' `
  '/data/Works_NumberTales/DataBases/**' `
  '/data/Works_NumberTales/Dictionaries/**' `
  '/data/Works_NumberTales/RoleplayPrompts/**' `
  '/data/Works_NumberTales/Images/DB_Primary/corefolder/**' `
  '/data/Works_NumberTales/Images/DB_SemiPrimary/corefolder/**' `
  '/data/Works_NumberTales/Images/DB_SelfSecondary/corefolder/**'

Write-Host "OK: sparse-checkout configured (Works_NumberTales: DataBases + corefolder images + RoleplayPrompts)"

# PenchantManufacture フォント（正はサブモジュール。Assets 側 .otf は同期コピー）
Set-Location (Join-Path $PSScriptRoot "..")
git submodule update --init --depth 1 PenchantManufacture_ImageAssets
Set-Location PenchantManufacture_ImageAssets
git sparse-checkout set --no-cone '/*.md' '/LICENSE' '/assets/fonts/**'
Set-Location ..
Copy-Item PenchantManufacture_ImageAssets/assets/fonts/PenchantManufacture.otf Assets/Fonts/penchant-manufactuer/PenchantManufacture.otf -Force
Write-Host "OK: PenchantManufacture.otf synced from submodule"
