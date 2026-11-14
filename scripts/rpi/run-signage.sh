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
# ポインタ下の窓＝全画面のサイネージへキーが流れるよう PointerRoot にする。
#
# 同じく WM が無いので、窓をモニタいっぱいへ広げる役もここで担う。Unity 6.7 の Linux プレイヤーは全画面化を
# WM 任せにし、WM が無いと窓が -screen-width/height の大きさで中央に残る（描画はモニタ解像度なので左下だけ映る）。
# 窓は作られた順に 1枚目・2枚目のモニタへ割り当てる（Unity は Display 2 の窓を Activate で後から作る）。
# RasPiOS_UnityConsole の ucon-run-app と同じ処理（2026-11-12 Pi 4B の UnityConsole で2画面を実測）。
# exec で box64 へ置き換わるので、サイネージの窓の _NET_WM_PID は このシェルの $$ と一致する。
# 窓の生成（box64 で数十秒）や Unity の取り直しに追従するため、起動後しばらく繰り返す。
fix_x_window() {
  FIT_PID=$$ python3 - <<'PY' 2>/dev/null || true
import ctypes, ctypes.util, os, re, subprocess
x = ctypes.CDLL(ctypes.util.find_library("X11"))
c_p, c_ul, c_i, c_u = ctypes.c_void_p, ctypes.c_ulong, ctypes.c_int, ctypes.c_uint
x.XOpenDisplay.restype = c_p
x.XSetInputFocus.argtypes = [c_p, c_ul, c_i, c_ul]
x.XSync.argtypes = [c_p, c_i]
x.XDefaultRootWindow.restype = c_ul; x.XDefaultRootWindow.argtypes = [c_p]
x.XQueryTree.argtypes = [c_p, c_ul, ctypes.POINTER(c_ul), ctypes.POINTER(c_ul), ctypes.POINTER(ctypes.POINTER(c_ul)), ctypes.POINTER(c_u)]
x.XGetGeometry.argtypes = [c_p, c_ul, ctypes.POINTER(c_ul)] + [ctypes.POINTER(c_i)] * 2 + [ctypes.POINTER(c_u)] * 4
x.XInternAtom.restype = c_ul; x.XInternAtom.argtypes = [c_p, ctypes.c_char_p, c_i]
x.XGetWindowProperty.argtypes = [c_p, c_ul, c_ul, ctypes.c_long, ctypes.c_long, c_i, c_ul, ctypes.POINTER(c_ul),
                                 ctypes.POINTER(c_i), ctypes.POINTER(c_ul), ctypes.POINTER(c_ul), ctypes.POINTER(c_p)]
x.XMoveResizeWindow.argtypes = [c_p, c_ul, c_i, c_i, c_u, c_u]
d = x.XOpenDisplay(None)
if d:
    x.XSetInputFocus(d, 1, 2, 0)   # PointerRoot, RevertToPointerRoot, CurrentTime
    pid = int(os.environ.get("FIT_PID", "0"))
    if pid:
        def geom(w):
            g = [c_ul(), c_i(), c_i(), c_u(), c_u(), c_u(), c_u()]
            x.XGetGeometry(d, w, *[ctypes.byref(v) for v in g])
            return g[1].value, g[2].value, g[3].value, g[4].value
        def wm_pid(w):
            t, f, n, b, p = c_ul(), c_i(), c_ul(), c_ul(), c_p()
            x.XGetWindowProperty(d, w, x.XInternAtom(d, b"_NET_WM_PID", 0), 0, 1, 0, 0, ctypes.byref(t),
                                 ctypes.byref(f), ctypes.byref(n), ctypes.byref(b), ctypes.byref(p))
            return ctypes.cast(p, ctypes.POINTER(c_ul))[0] if p.value and n.value else None
        root = x.XDefaultRootWindow(d)
        # モニタの矩形 (x, y, w, h)。xrandr の行は " 0: +*HDMI-1 1280/597x800/336+0+0  HDMI-1"。読めなければ root 全体
        # --current: 付けないと xrandr は毎回モニタを検出し直す（3秒おきに EDID を読みに行くことになる）
        mons = [(int(mx), int(my), int(mw), int(mh)) for mw, mh, mx, my in re.findall(
            r"(\d+)/\d+x(\d+)/\d+\+(\d+)\+(\d+)",
            subprocess.run(["xrandr", "--current", "--listactivemonitors"], capture_output=True, text=True).stdout)] or [geom(root)]
        r, pa, ch, n = c_ul(), c_ul(), ctypes.POINTER(c_ul)(), c_u()
        if x.XQueryTree(d, root, ctypes.byref(r), ctypes.byref(pa), ctypes.byref(ch), ctypes.byref(n)):
            # 窓 ID は作られた順に増える
            for w, mon in zip(sorted(w for w in (ch[i] for i in range(n.value)) if wm_pid(w) == pid), mons):
                if geom(w) != mon:
                    x.XMoveResizeWindow(d, w, *mon)
    x.XSync(d, 0)
PY
}
( for _ in $(seq 60); do sleep 3; fix_x_window; done ) &

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
