#!/usr/bin/env bash
# NTsWallpaperEngine サイネージ起動スクリプト（Raspberry Pi 4B / box64）
# 配置想定: /opt/ntswallpaper/ に Builds/LinuxSignage/ の中身と本スクリプトを置く。
# OSイメージへの組み込み・自動起動設定は「Raspberry Pi OS開発」プロジェクト側で行う。
set -u
cd "$(dirname "$0")" || exit 1

# OS側の日次pull先（update-creationsdb.sh）があれば、ビルド同梱データより優先して参照する
CREATIONSDB_DIR="${CREATIONSDB_REPO_DIR:-/opt/ntswallpaper/creationsdb}/data/Works_NumberTales"
if [ -d "$CREATIONSDB_DIR/DataBases" ]; then
  export NTSWE_CREATIONSDB="$CREATIONSDB_DIR"
fi

# RPi の V3D (Mesa) は素の GL バージョン報告が低いため、Unity が要求する 3.3 相当へ引き上げる
export MESA_GL_VERSION_OVERRIDE=3.3
export MESA_GLSL_VERSION_OVERRIDE=330

# X にウィンドウマネージャが無く、サイネージの窓は入力フォーカスを得ない。SDL（Unity の Linux プレイヤーが
# ゲームパッド読み取りに使う）は既定で「フォーカスの無いアプリ」のパッド入力を捨てるため、背景でも受け取らせる。
# 無いと Input System 上は Gamepad として認識されるのにボタンが一切届かない（2026-09-20 実機・仮想パッドで確認）
export SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS=1

# 長押し（Esc / パッドStart / ホイール押し込み）の行き先。poweroff（既定）か reboot。
# NTsWallpaper OS では X セッションが /boot/firmware/ntswallpaper.conf の LONG_PRESS_ACTION を渡してくる。
export NTSWE_QUIT_ACTION="${NTSWE_QUIT_ACTION:-poweroff}"

# X にウィンドウマネージャが無いため入力フォーカスが None のままで、キー入力はどの窓にも配送されない
# （2026-09-21 実機実測: 仮想キーボードの M が無反応 → PointerRoot にした瞬間に届いた）。
# ポインタ下の窓＝全画面のサイネージへキーが流れるよう PointerRoot にする。窓が出る前後で
# 取り直されることがあるので数回入れる（コストはほぼゼロ）。
focus_pointer_root() {
  python3 - <<'PY' 2>/dev/null || true
import ctypes, ctypes.util
x = ctypes.CDLL(ctypes.util.find_library("X11"))
x.XOpenDisplay.restype = ctypes.c_void_p
x.XSetInputFocus.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.c_int, ctypes.c_ulong]
x.XSync.argtypes = [ctypes.c_void_p, ctypes.c_int]
d = x.XOpenDisplay(None)
if d:
    x.XSetInputFocus(d, 1, 2, 0)   # PointerRoot, RevertToPointerRoot, CurrentTime
    x.XSync(d, 0)
PY
}
( for wait in 2 15 45; do sleep "$wait"; focus_pointer_root; done ) &

# box64 チューニング（安定性優先。問題があれば BIGBLOCK を 1 に下げる）
export BOX64_DYNAREC_BIGBLOCK=2
export BOX64_DYNAREC_SAFEFLAGS=1
export BOX64_LOG=0

# 960x540レンダリング（RPi 4Bの負荷対策。フルスクリーン表示時はディスプレイ側で拡大される）
exec box64 ./NTsWallpaperEngine.x86_64 \
  -screen-fullscreen 1 \
  -screen-width 960 \
  -screen-height 540 \
  "$@"
