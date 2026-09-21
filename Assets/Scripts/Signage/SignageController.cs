using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace NTsWallpaperEngine.Signage
{
    /// <summary>
    /// サイネージ本体。創作DB（サブモジュール駆動）からレコードを読み込み、
    /// 一定間隔（または入力時）にカードを切り替える。数字カウントアニメーションは必須要件（AGENTS.md 6章）。
    /// 入力の定義は SignageInput、画面の操作UIは SignageHud（実行時に組む）。
    /// 旧 CharacterAssetsDB のアニメーション仕様（カウント演出・イージング・フェード）を踏襲。
    /// </summary>
    public class SignageController : MonoBehaviour
    {
        [SerializeField] SignageCardView view;

        [Header("Animation (旧CharacterAssetsDB準拠)")]
        [SerializeField] int numAnimDuration = 300;
        [SerializeField] float acceleration = 2.65f;
        [SerializeField] float animationTime = 1.4f;
        [Tooltip("文字スクランブルの確定速度倍率。1未満にするとフェード完了後もスクランブルが継続し、全文の演出が見える")]
        [SerializeField] float textAnimSpeed = 0.6f;

        public enum PlaybackMode
        {
            Random,      // ランダム再生（直前と同じ個体は連続しない）
            Sequential,  // 番号順再生（Num昇順で巡回）
        }

        [Header("Switching")]
        [Tooltip("切替間隔（秒）。秒針と同期し、30なら秒針00/30、20なら00/20/40ちょうどで切り替わる（60の約数推奨）")]
        [SerializeField] int switchIntervalSeconds = 30;
        [Tooltip("再生モード（ランダム／番号順）。実行中の切替は SignageInput の Mode（M・右クリック・パッドY）")]
        [SerializeField] PlaybackMode playbackMode = PlaybackMode.Random;

        [Header("Daily DB reload (RPi常時稼働向け)")]
        [Tooltip("毎日この時刻(時)にDBを再読込する。OS側の日次pull（scripts/rpi/update-creationsdb.sh）とセットで運用")]
        [SerializeField, Range(0, 23)] int dailyReloadHour = 4;

        [Header("Controls overlay")]
        [Tooltip("操作UI（SignageHud）の和文フォント。カード用とは別のフリーフォント（BIZ UDPGothic / OFL 1.1）")]
        [SerializeField] TMP_FontAsset hudFont;

        [Header("Background design")]
        [Tooltip("ドット1周期のピクセル数（生成テクスチャ内）")]
        [SerializeField] int dotCellSize = 56;
        [SerializeField, Range(0f, 1f)] float dotRadius = 0.24f;
        [SerializeField, Range(0f, 1f)] float dotMaxAlpha = 0.55f;
        [Tooltip("画面中央でドットをどこまで弱めるか (0=消える)")]
        [SerializeField, Range(0f, 1f)] float dotCenterFade = 0.06f;
        [SerializeField, Range(0f, 1f)] float glowAlpha = 0.5f;

        List<NtCharacterRecord> _records = new List<NtCharacterRecord>();
        List<NtCharacterRecord> _sorted = new List<NtCharacterRecord>();  // 番号順再生・方向入力のページ送り用
        NtCharacterRecord _next;
        float _alpha;
        bool _isAnimating;
        int _queuedStep;          // アニメ中に来た手動操作を1つだけ覚える（捨てると「押しても無反応」に見える）
        bool _hasQueuedStep;
        long _lastSlot = -1;  // 壁時計同期用の秒スロット
        DateTime _lastReloadDate; // 日次リロードの実施日
        Texture2D _dotTexture, _gradientTexture, _glowTexture;

        System.Random _random = new System.Random();

        SignageInput _input;      // バインドと操作方法の表（SignageInput）
        SignageHud _hud;          // 通知・操作方法・長押しゲージ（実行時に組む）
        Task<CreationsDbUpdater.Result> _dbUpdate;
        bool _quitting;

        const string PrefInterval = "signage.intervalSeconds";
        const string PrefMode = "signage.playbackMode";

        /// <summary>
        /// 長押しの行き先（systemctl に渡す動詞）。poweroff / reboot 以外・未設定はアプリ終了（UnityConsole ではランチャーへ）。
        /// サイネージOSは /boot/firmware/ntswallpaper.conf の LONG_PRESS_ACTION を X セッションが渡してくる（既定 poweroff）。
        /// </summary>
        static string SystemVerb
        {
            get
            {
                string v = Environment.GetEnvironmentVariable("NTSWE_QUIT_ACTION")?.Trim().ToLowerInvariant();
                return v == "poweroff" || v == "reboot" ? v : null;
            }
        }
        static string QuitLabel => SystemVerb switch { "poweroff" => "電源オフ", "reboot" => "再起動", _ => "終了" };

        void Awake()
        {
            // RPi の X セッション（NTsWallpaper / UnityConsole とも）にはウィンドウマネージャが無く、窓がフォーカスを得ない。
            // 既定の ResetAndDisableNonBackgroundDevices だと全デバイスが「非フォーカス」で無効化されるので、
            // フォーカスを無視させ、起動時点で無効化済みのデバイスも明示的に戻す（NTsSphereChaser 826ea11 で実測）。
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            foreach (var d in InputSystem.devices) InputSystem.EnableDevice(d);

            _input = new SignageInput();

            // 現地で変えた設定は電源を切っても残す（サイネージは長押しで電源オフできる）
            switchIntervalSeconds = PlayerPrefs.GetInt(PrefInterval, switchIntervalSeconds);
            playbackMode = (PlaybackMode)PlayerPrefs.GetInt(PrefMode, (int)playbackMode);
        }

        void OnEnable() => _input.Enable();
        void OnDisable() => _input.Disable();

        void Start()
        {
            Debug.Log("[Signage] 入力デバイス: " + string.Join(", ",
                InputSystem.devices.Select(d => $"{d.displayName}({d.layout}{(d.enabled ? "" : ",disabled")})")));
            _records = CreationsDbLoader.LoadAll();
            RebuildSortedList();
            _lastReloadDate = DateTime.Now.Date;
            SetupBackground();
            if (view) view.SetClock(DateTime.Now);
            if (view && view.clockText)
            {
                _hud = SignageHud.Create(view.clockText.canvas, hudFont ? hudFont : view.clockText.font);
                _hud.Toast("H・F1 ／ パッド Select で操作方法", 6f);
            }

            if (_records.Count == 0)
            {
                // 止めない: この状態こそ短押し（創作DBの取り直し）で復帰させたいので入力は生かしておく
                Debug.LogError("[Signage] 表示できるレコードが0件。サブモジュール取得と同期状態を確認して。");
                _hud?.Toast("表示できるレコードが0件。Esc／Start／ホイール短押しで創作DBを取得", 20f);
                return;
            }
            StartCoroutine(AnimateCard(0, true));
        }

        void Update()
        {
            var now = DateTime.Now;

            // 時計は毎フレーム更新（「:」の点滅・秒表示のため）
            if (view) view.SetClock(now);

            // 秒針同期の切替: 実時刻を間隔で割ったスロット境界（00/20/40 や 00/30）で発火
            int interval = Mathf.Max(5, switchIntervalSeconds);
            long slot = (long)now.TimeOfDay.TotalSeconds / interval;
            if (_lastSlot < 0)
            {
                _lastSlot = slot; // 起動直後の即切替を防止（次の境界から同期開始）
            }
            else if (slot != _lastSlot)
            {
                _lastSlot = slot; // アニメ中に境界を跨いだ場合はその回をスキップ（次の境界で再同期）
                if (!_isAnimating) StartCoroutine(AnimateCard());
            }

            HandleInput();
            PollDbUpdate();

            // 日次リロード: OS側がpullした最新DBを毎日 dailyReloadHour 時に取り込む（常時稼働サイネージ向け）
            if (now.Date != _lastReloadDate && now.Hour >= dailyReloadHour && !_isAnimating)
            {
                _lastReloadDate = now.Date;
                ReloadRecords();
            }
        }

        // ---- 入力 ----

        void HandleInput()
        {
            if (_quitting) return;

            if (_input.Next.WasPressedThisFrame()) RequestCard(0);
            if (_input.PagePrev.WasPressedThisFrame()) RequestCard(-1);   // 方向入力: キャラ番号順に前
            if (_input.PageNext.WasPressedThisFrame()) RequestCard(1);    // 　　　　　　　　　　　　後
            if (_input.Mode.WasPressedThisFrame()) TogglePlaybackMode();

            if (_input.IntervalCycle.WasPressedThisFrame())
            {
                int at = Array.IndexOf(SignageInput.IntervalChoices, switchIntervalSeconds);
                SetInterval(SignageInput.IntervalChoices[(at + 1) % SignageInput.IntervalChoices.Length]);
            }
            for (int i = 0; i < _input.IntervalPick.Length; i++)
                if (_input.IntervalPick[i].WasPressedThisFrame()) SetInterval(SignageInput.IntervalChoices[i]);

            if (_input.Help.WasPressedThisFrame())
                _hud?.ToggleHelp(SignageInput.HelpText(QuitLabel, StatusLine()));

            switch (_input.PollSystem(out float hold))
            {
                case SignageInput.Press.Short: StartDbUpdate(); break;
                case SignageInput.Press.Long: StartCoroutine(QuitOrShutdown()); break;
            }
            _hud?.Hold(hold, $"長押しで{QuitLabel}…");
        }

        /// <summary>カード切替の要求。step=0 は再生モードに従う「次」、±1 はキャラ番号順の前後。</summary>
        void RequestCard(int step)
        {
            if (_isAnimating) { _queuedStep = step; _hasQueuedStep = true; return; }
            StartCoroutine(AnimateCard(step));
        }

        void SetInterval(int seconds)
        {
            switchIntervalSeconds = seconds;
            _lastSlot = -1;                     // 変更直後に即切り替わらないよう次の境界から数え直す
            PlayerPrefs.SetInt(PrefInterval, seconds);
            PlayerPrefs.Save();
            _hud?.Toast($"自動送り: {IntervalLabel(seconds)}");
            Debug.Log($"[Signage] 自動送り間隔: {seconds}秒");
        }

        static string IntervalLabel(int seconds) => seconds >= 60 && seconds % 60 == 0
            ? $"{seconds / 60}分" : $"{seconds}秒";

        string StatusLine() =>
            $"現在: 自動送り {IntervalLabel(switchIntervalSeconds)} ／ 再生モード " +
            $"{(playbackMode == PlaybackMode.Random ? "ランダム" : "番号順")} ／ 表示対象 {_records.Count}件";

        // ---- 創作DBの取り直し（短押し）----

        void StartDbUpdate()
        {
            if (_dbUpdate != null && !_dbUpdate.IsCompleted) { _hud?.Toast("創作DBを更新中…"); return; }
            _hud?.Toast("創作DBを更新中…", 120f);
            Debug.Log("[Signage] 創作DB更新を開始");
            _dbUpdate = Task.Run(() => CreationsDbUpdater.Run());   // 表示は止めない
        }

        void PollDbUpdate()
        {
            if (_dbUpdate == null || !_dbUpdate.IsCompleted) return;
            var result = _dbUpdate.IsFaulted
                ? new CreationsDbUpdater.Result
                { Message = "創作DB更新に失敗: " + _dbUpdate.Exception?.GetBaseException().Message }
                : _dbUpdate.Result;
            _dbUpdate = null;

            if (!result.Ok) { _hud?.Toast(result.Message, 6f); return; }

            // 取得先が同梱データと違う場合もあるので、成功したら必ずそちらを読み直す
            CreationsDbLoader.ExternalRootOverride = result.DataRoot;
            int count = ReloadRecords();
            _hud?.Toast(count > 0 ? $"{result.Message}（{count}件）" : result.Message, 5f);
        }

        int ReloadRecords()
        {
            var reloaded = CreationsDbLoader.LoadAll();
            if (reloaded.Count == 0)
            {
                Debug.LogWarning("[Signage] 再読込で0件だったため、現行データを継続使用");
                return 0;
            }
            _records = reloaded;
            RebuildSortedList();
            Debug.Log($"[Signage] 再読込完了: {reloaded.Count}件");
            return reloaded.Count;
        }

        // ---- 終了・電源オフ・再起動（長押し）----

        IEnumerator QuitOrShutdown()
        {
            if (_quitting) yield break;
            _quitting = true;
            _hud?.Toast($"{QuitLabel}します…", 15f);
            yield return new WaitForSecondsRealtime(0.7f);      // 文字を読ませてから落とす

            if (SystemVerb != null)
            {
                string error = Systemctl(SystemVerb);
                if (error == null) yield break;                 // あとは systemd が止める
                _quitting = false;
                _hud?.Toast($"{QuitLabel}に失敗: {error}", 6f);
                yield break;
            }
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// tty1 のセッション内から呼ぶので polkit が許可する（sudo も sudoers 追加も不要。2026-09-21 実機で確認）。
        /// 失敗したときだけ理由を返す。
        /// </summary>
        static string Systemctl(string verb)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("systemctl", verb)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                };
                using var process = System.Diagnostics.Process.Start(psi);
                // box64 上の Mono は子プロセスを回収できず WaitForExit / ExitCode が当てにならない（CreationsDbUpdater と同じ）。
                // stderr が閉じた＝終わったとみなし、何か書かれていれば失敗として返す（成功時の systemctl は無言）。
                var stderr = process.StandardError.ReadToEndAsync();
                if (!stderr.Wait(15000)) return "応答なし";
                string message = stderr.Result.Trim();
                return message.Length > 0 ? message : null;
            }
            catch (Exception e)
            {
                return e.Message;
            }
        }

        /// <summary>次に出すレコード。step=0 は再生モードに従い、±1 はキャラ番号順の前後（方向入力）。</summary>
        NtCharacterRecord PickNextRecord(int step)
        {
            if (_records.Count == 0) return null; // 同期直後など一時的に空のケースを防御
            if (_records.Count == 1) return _records[0];

            if (step != 0)
            {
                int at = IndexOf(_sorted, view.Current);
                int n = _sorted.Count;
                return _sorted[(((at < 0 ? 0 : at + step) % n) + n) % n];
            }

            if (playbackMode == PlaybackMode.Sequential)
                return _sorted[(IndexOf(_sorted, view.Current) + 1) % _sorted.Count];

            int current = IndexOf(_records, view.Current);
            int index;
            do { index = _random.Next(_records.Count); } while (index == current);
            return _records[index];
        }

        /// <summary>
        /// 表示中のカードの位置。再読込するとレコードは別実体になるので、DB名＋Num表記で引き直す
        /// （インデックスを覚えておくと、DB更新後に番号順ページ送りが飛ぶ）。
        /// </summary>
        static int IndexOf(List<NtCharacterRecord> list, NtCharacterRecord record) =>
            record == null ? -1 : list.FindIndex(r => r.DbKey == record.DbKey && r.NumRaw == record.NumRaw);

        void RebuildSortedList()
        {
            _sorted = new List<NtCharacterRecord>(_records);
            _sorted.Sort((a, b) =>
            {
                // 16進表記（"0xA" 等）は通常番号の後ろに別グループとして並べる。
                int byGroup = (a.NumIsHex ? 1 : 0) - (b.NumIsHex ? 1 : 0);
                if (byGroup != 0) return byGroup;
                // 算術形式（"3x11"→33 等）は計算結果と同値、16進はデコード値で比較（NumSortValue）。
                // 同値の場合はNum表記の序数比較で安定化（例: "33" が "3x11" より前）。
                int byNum = a.NumSortValue.CompareTo(b.NumSortValue);
                return byNum != 0 ? byNum : string.CompareOrdinal(a.NumRaw, b.NumRaw);
            });
        }

        void TogglePlaybackMode()
        {
            // 番号順は表示中の個体の次から続く（PickNextRecord が毎回現在位置を引き直すため、覚える値は無い）
            playbackMode = playbackMode == PlaybackMode.Random ? PlaybackMode.Sequential : PlaybackMode.Random;
            PlayerPrefs.SetInt(PrefMode, (int)playbackMode);
            PlayerPrefs.Save();
            _hud?.Toast($"再生モード: {(playbackMode == PlaybackMode.Random ? "ランダム" : "番号順")}");
            Debug.Log($"[Signage] 再生モード: {playbackMode}");
        }

        IEnumerator AnimateCard(int step = 0, bool onStarting = false)
        {
            if (_isAnimating && !onStarting) yield break;
            _isAnimating = true;

            // フェードアウト（数字はカウントアップしながら消える。スクランブルは大型番号のみ）
            if (!onStarting && view.Current != null)
            {
                int baseNum = view.Current.NumValue;
                while (_alpha > 0f)
                {
                    int shown = baseNum + Mathf.FloorToInt(numAnimDuration * Mathf.Pow(1f - _alpha, acceleration));
                    view.SetAnimatedNumber(shown);
                    view.SetBigNumberScramble(_alpha);
                    view.SetAlpha(_alpha);
                    _alpha -= Time.deltaTime / animationTime;
                    yield return null;
                }
                _alpha = 0f;
                view.SetAlpha(0f);
            }

            // 次レコードの決定・画像読込（バリアントはランダム）
            _next = PickNextRecord(step);
            if (_next == null) { _isAnimating = false; yield break; }
            Debug.Log($"[Signage] カード: {_next.NumRaw}" + (step != 0 ? $"（方向入力 {step:+#;-#}）" : ""));
            string imagePath = _next.ImagePaths[_random.Next(_next.ImagePaths.Count)];
            Texture2D texture = CreationsDbLoader.LoadTexture(imagePath);
            view.Apply(_next, texture);
            view.SetAlpha(0f);

            // フェードイン（数字はカウントダウンで収束、文字はスクランブル→確定）
            // 文字スクランブルは経過時間ベースで進行し、textAnimSpeed < 1 の場合は
            // フェード完了後も継続して全文の演出を見せる。
            int target = _next.NumValue;
            float textDuration = animationTime / Mathf.Max(0.05f, textAnimSpeed);
            float elapsed = 0f;
            while (_alpha < 1f)
            {
                int shown = target - Mathf.FloorToInt(numAnimDuration * Mathf.Pow(1f - _alpha, acceleration));
                view.SetAnimatedNumber(shown);
                view.SetTextProgress(elapsed / textDuration);
                view.SetAlpha(_alpha);
                elapsed += Time.deltaTime;
                _alpha += Time.deltaTime / animationTime;
                yield return null;
            }
            _alpha = 1f;
            view.SetAlpha(1f);
            view.SetAnimatedNumber(target);

            // 残りの文字スクランブルを完走させる
            while (elapsed < textDuration)
            {
                view.SetTextProgress(elapsed / textDuration);
                elapsed += Time.deltaTime;
                yield return null;
            }
            view.SetTextProgress(1f);
            view.SetNumberFinal(); // 大型番号を原文表記（例 "222A"）へ着地
            _isAnimating = false;

            if (_hasQueuedStep)   // アニメ中に押された分を1回だけ消化する
            {
                _hasQueuedStep = false;
                StartCoroutine(AnimateCard(_queuedStep));
            }
        }

        // ---- 背景デザイン（3層: 縦グラデーション / エッジ強調ドット / キャラ背後グロー）----

        public void SetupBackground() // SignageCapture（エディタでのカード撮影）からも呼ぶ
        {
            if (!view) return;

            if (view.gradientOverlay)
            {
                _gradientTexture = CreateVerticalGradientTexture();
                view.gradientOverlay.texture = _gradientTexture;
                view.gradientOverlay.uvRect = new Rect(0f, 0f, 1f, 1f);
            }

            if (view.dotsOverlay)
            {
                _dotTexture = CreateEdgeDotsTexture(960, 540);
                view.dotsOverlay.texture = _dotTexture;
                view.dotsOverlay.uvRect = new Rect(0f, 0f, 1f, 1f);
            }

            if (view.characterGlow)
            {
                _glowTexture = CreateRadialGlowTexture(512, glowAlpha);
                view.characterGlow.texture = _glowTexture;
                view.characterGlow.color = Color.white;
            }
        }

        /// <summary>白+縦αランプ（下ほど濃い）。色はビューがテーマ色でティントする。</summary>
        static Texture2D CreateVerticalGradientTexture()
        {
            const int h = 256;
            var tex = new Texture2D(1, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "SignageGradient",
            };
            var pixels = new Color32[h];
            for (int y = 0; y < h; y++)
            {
                float t = (float)y / (h - 1);          // 0=下, 1=上
                float a = Mathf.Pow(1f - t, 1.6f);     // 下端で最大、上へ滑らかに減衰
                pixels[y] = new Color32(255, 255, 255, (byte)(255f * a));
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>画面全体のドット柄。中央は薄く、四辺の縁に向かって強くなる（emstk-rvカードの雰囲気）。</summary>
        Texture2D CreateEdgeDotsTexture(int width, int height)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "SignageDots",
            };
            var pixels = new Color32[width * height];
            float cell = Mathf.Max(8, dotCellSize);
            float r = dotRadius * cell;

            for (int y = 0; y < height; y++)
            {
                float ny = Mathf.Abs(y / (float)(height - 1) - 0.5f) * 2f; // 0=中央,1=上下端
                for (int x = 0; x < width; x++)
                {
                    float nx = Mathf.Abs(x / (float)(width - 1) - 0.5f) * 2f;
                    // 端係数: 中央→縁で滑らかに増加（横方向を優先し、カード左右の柱状ドット感を出す）
                    float edge = Mathf.Clamp01(Mathf.Max(Mathf.Pow(nx, 2.2f), Mathf.Pow(ny, 3.2f)));
                    float strength = Mathf.Lerp(dotCenterFade, 1f, edge);

                    // 市松配置ドット距離
                    float gx = x / cell, gy = y / cell;
                    float ox = (Mathf.FloorToInt(gy) % 2 == 0) ? 0f : 0.5f;
                    float dx = (gx + ox) - Mathf.Floor(gx + ox) - 0.5f;
                    float dy = gy - Mathf.Floor(gy) - 0.5f;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy) * cell;

                    float aa = Mathf.Clamp01(r - dist + 1f);
                    byte a = (byte)(255f * dotMaxAlpha * strength * aa);
                    pixels[y * width + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>キャラ背後に敷くソフトな放射状グロー。</summary>
        static Texture2D CreateRadialGlowTexture(int size, float maxAlpha)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "SignageGlow",
            };
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(half, half)) / half;
                    float a = Mathf.Pow(Mathf.Clamp01(1f - d), 2.2f) * maxAlpha;
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255f * a));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        void OnDestroy()
        {
            _input?.Dispose();
            if (_dotTexture) Destroy(_dotTexture);
            if (_gradientTexture) Destroy(_gradientTexture);
            if (_glowTexture) Destroy(_glowTexture);
        }
    }
}
