# Raspberry Pi 4B 引き渡し資料 — NTsWallpaperEngine サイネージ

「Raspberry Pi OS開発」Coworkプロジェクトへの引き渡し内容。OSイメージへの組み込み（配置・自動起動・キオスク化）はあちら側の管轄で、本リポジトリはビルド成果物と起動要件の提供までを担当する。

## 1. 引き渡す成果物

Unityメニュー `Signage/Build Linux x64 (RPi Signage)` の実行で生成される:

| 内容 | パス |
| --- | --- |
| プレイヤー本体 | `Builds/LinuxSignage/NTsWallpaperEngine.x86_64` |
| データ一式（StreamingAssets内にCreationsDB同梱） | `Builds/LinuxSignage/NTsWallpaperEngine_Data/` |
| Unityランタイム | `Builds/LinuxSignage/UnityPlayer.so` ほか |
| 起動スクリプト | `scripts/rpi/run-signage.sh`（成果物と同じフォルダに配置する） |
| 創作DB取得スクリプト | `scripts/rpi/update-creationsdb.sh`（同上。日次タイマーと**アプリの短押し更新**が同じものを叩く。ビルド時に成果物の隣へ自動コピーされる） |

- ビルドは **Linux x64 / Mono バックエンド**（box64互換性のため IL2CPP 不使用）。
- 創作DBデータはビルド時にサブモジュールから自動同期され、`NTsWallpaperEngine_Data/StreamingAssets/CreationsDB/` に**表示対象レコード（Progress: released / released(beta) / stillTentative / unreleased）分のみ**含まれる。RPi側でサブモジュールやネットワークは不要。

## 2. OSイメージ側の要件

- **ベースOS**: Raspberry Pi OS (64-bit, Bookworm以降) — デスクトップ版（X11/Waylandセッションが必要。GUIなしのLite不可）
- **box64**: ARM64向けにRPI4ARM64プリセットでビルドしたもの（v0.3系以降推奨）

  ```bash
  git clone https://github.com/ptitSeb/box64 && cd box64
  mkdir build && cd build
  cmake .. -DRPI4ARM64=1 -DCMAKE_BUILD_TYPE=RelWithDebInfo
  make -j4 && sudo make install
  ```

- **GPU設定**: `raspi-config` でGPUメモリを256MB以上に。V3Dドライバ（既定のfkms/kms）有効。
- **配置想定**: `/opt/ntswallpaper/` に成果物一式 + `run-signage.sh`。

## 3. 起動

```bash
/opt/ntswallpaper/run-signage.sh
```

スクリプト内で次を設定している:

- `MESA_GL_VERSION_OVERRIDE=3.3` — V3DのGLバージョン報告がUnity要求より低いための引き上げ（本サイネージは2D UIのみなので実用上動作する）。
- `SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS=1` — ゲームパッド入力を窓のフォーカス無しで受け取る（6章）。
- **X の入力フォーカスを `PointerRoot` にする**（起動2/15/45秒後に設定）。WM が無い X では既定が `None` のままで、**キーボード入力がどの窓にも配送されない**（2026-09-21 実機で確認）。ポインタ下の窓＝全画面のサイネージへキーが流れるようにする。
- `NTSWE_QUIT_ACTION=poweroff` — 長押し（Esc / パッドStart / ホイール押し込みを2秒）の行き先。サイネージ機は電源オフ、この変数が無い機体（UnityConsole 等）はアプリ終了になる。

レンダリング解像度は **960x540**（UIの基準解像度と一致）。フルスクリーン表示時はディスプレイ側で拡大されるため、RPi 4Bの描画負荷はフルHD比で約1/4になる。

自動起動の例（OS開発側の参考。systemdユーザーユニット or デスクトップautostart）:

```ini
# ~/.config/systemd/user/ntswallpaper.service
[Unit]
Description=NTs Wallpaper Signage
After=graphical-session.target

[Service]
ExecStart=/opt/ntswallpaper/run-signage.sh
Restart=always
RestartSec=5

[Install]
WantedBy=graphical-session.target
```

## 3.5 創作DBの日次自動更新（稼働中のデータ更新）

稼働中もDB更新を毎日取り込む仕組みを用意している。**pullはOS側（ネイティブARM git）、アプリは読むだけ**の分担。

1. `scripts/rpi/update-creationsdb.sh` を成果物と一緒に配置（既定の取得先: `/opt/ntswallpaper/creationsdb`。初回はsparseクローン、以降はpull）。
2. systemdタイマー等で毎日実行する（例: 03:30）。

   ```ini
   # /etc/systemd/system/creationsdb-update.service
   [Unit]
   Description=Update NumberTales CreationsDB
   [Service]
   Type=oneshot
   ExecStart=/opt/ntswallpaper/update-creationsdb.sh
   ```

   ```ini
   # /etc/systemd/system/creationsdb-update.timer
   [Unit]
   Description=Daily CreationsDB update
   [Timer]
   OnCalendar=*-*-* 03:30:00
   Persistent=true
   [Install]
   WantedBy=timers.target
   ```

3. `run-signage.sh` は取得先が存在すれば環境変数 `NTSWE_CREATIONSDB` を自動設定し、アプリはビルド同梱データより**そちらを優先**して読む。
4. アプリは毎日 **04:00**（`SignageController.dailyReloadHour` で変更可）にレコードを再読込するため、再起動不要で新キャラ・修正が反映される。ネットワーク断などでpullに失敗した日も、直前のデータで継続動作する。
5. **待てないときは短押しで即取り直す**: `Esc` / パッド `Start` / ホイール押し込みの短押しで、アプリが同じ `update-creationsdb.sh` を実行して読み直す（サービス再起動なし）。取得先は `NTSWE_CREATIONSDB` があればそのリポジトリ、無ければ `persistentDataPath/creationsdb`（UnityConsole 等。**実機に git が必要**）。結果は画面下に1行出る。

## 4. 動作仕様（サイネージ）

- 既定30秒（実行中に 20秒/30秒/1分/2分 へ変更可・設定は保存される）でキャラクターカードが自動切替。
- 操作は マウス / キーボード / ゲームパッド の3系統（Input System）。**一覧は実行中に `H`・`F1`・パッド `Select` で画面に出る**（正本は `Assets/Scripts/Signage/SignageInput.cs`、README にも表がある）。
  次のカード = 左クリック・Space/Enter・A(南) ／ 番号順の前後 = 矢印・十字・左スティック ／ 再生モード = 右クリック・M・Y(北) ／
  自動送り間隔 = 1〜4・X(西) ／ 創作DB取り直し = 短押し（Esc・Start・ホイール押し込み） ／ 終了・電源オフ = 同じ入力の長押し2秒。
- カードは創作DB（db_Primary / db_SemiPrimary / db_SelfSecondary）から自動形成。corefolder画像・型番・名前・機体名・テーマ色を表示。
- 数字部分はカウントアップ/ダウンのアニメーション付き。
- 時計（HH mm）を左上に常時表示。

## 5. 既知のリスクと代替案

- box64 + Mesa GLオーバーライドは**非公式構成**。フレームレートはRPi 4Bで15〜30fps程度を想定（2D UIのため実用範囲だが、実機検証必須）。
- 動作しない/重すぎる場合の代替: Unityを **Web (WebGL) ビルド**に切り替えて Chromium キオスクで表示する構成が確実（本リポジトリはUnity 6.3のためWebビルドに対応済み。必要になったら言って）。

## 6. トラブルシューティング

| 症状 | 対処 |
| --- | --- |
| 起動直後にクラッシュ | `BOX64_LOG=1` で再実行しログ確認。`BOX64_DYNAREC_BIGBLOCK=1` に下げる |
| 「OpenGL 3.2 not supported」系エラー | MESA_GL_VERSION_OVERRIDE が効いているか確認（`glxinfo | grep version`） |
| ゲームパッドが効かない（マウスは効く） | 起動環境に `SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS=1` があるか確認（`run-signage.sh` / UnityConsole の `ucon-run-app` が設定）。WM の無い X では窓がフォーカスを得ず、SDL が既定でパッド入力を捨てる。Player.log 冒頭の `[Signage] 入力デバイス:` にパッドが `Gamepad` として出ているかも見る |
| キーボードだけ効かない（マウス・パッドは効く） | X の入力フォーカスを見る（`DISPLAY=:0` で `XGetInputFocus`）。`None`(0x0) なら誰にも届いていない。`run-signage.sh` / `ucon-run-app` が `PointerRoot` を設定するので、古い起動スクリプトのままの機体は差し替える |
| 長押しが成立しない（短押しばかり起きる） | X のキーリピートが「離上＋押下」の連打として届いている。アプリ側は離上を0.15秒様子見して吸収済み（`SignageInput.PollSystem`）。それでも駄目なら `xset -r` で切り分ける |
| 創作DBの取り直しが終わらない | box64 上の Mono は子プロセスを回収できず `WaitForExit` が返らない。アプリは `/tmp/ntswe-dbupdate.done` のマーカーで完了を見る実装（`CreationsDbUpdater.RunScript`）。`/tmp/ntswe-dbupdate.log` に `update-creationsdb.sh` の出力が残る |
| 画面が出ずXエラー | デスクトップセッション内から起動しているか確認（SSHからは `DISPLAY=:0` を付与） |
| 文字化け・豆腐 | ビルドにフォントは同梱済みのため通常発生しない。発生時はビルド成果物の破損を疑う |
