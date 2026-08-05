#!/usr/bin/env bash
# NTsWallpaperEngine サイネージ起動スクリプト（Raspberry Pi 4B / box64）
# 配置想定: /opt/ntswallpaper/ に Builds/LinuxSignage/ の中身と本スクリプトを置く。
# OSイメージへの組み込み・自動起動設定は「Raspberry Pi OS開発」プロジェクト側で行う。
set -u
cd "$(dirname "$0")"

# RPi の V3D (Mesa) は素の GL バージョン報告が低いため、Unity が要求する 3.3 相当へ引き上げる
export MESA_GL_VERSION_OVERRIDE=3.3
export MESA_GLSL_VERSION_OVERRIDE=330

# box64 チューニング（安定性優先。問題があれば BIGBLOCK を 1 に下げる）
export BOX64_DYNAREC_BIGBLOCK=2
export BOX64_DYNAREC_SAFEFLAGS=1
export BOX64_LOG=0

exec box64 ./NTsWallpaperEngine.x86_64 \
  -screen-fullscreen 1 \
  -screen-width 1920 \
  -screen-height 1080 \
  "$@"
