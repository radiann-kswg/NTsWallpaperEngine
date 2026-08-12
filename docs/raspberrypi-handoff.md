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

スクリプト内で `MESA_GL_VERSION_OVERRIDE=3.3` を設定している（V3DのGLバージョン報告がUnity要求より低いための引き上げ。本サイネージは2D UIのみなので実用上動作する想定）。

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

## 4. 動作仕様（サイネージ）

- 毎分（時計の分が変わるタイミング）でキャラクターカードが自動切替。画面クリック/タップでも切替。
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
| 画面が出ずXエラー | デスクトップセッション内から起動しているか確認（SSHからは `DISPLAY=:0` を付与） |
| 文字化け・豆腐 | ビルドにフォントは同梱済みのため通常発生しない。発生時はビルド成果物の破損を疑う |
