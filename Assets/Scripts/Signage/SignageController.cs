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
        [Tooltip("画面ごとのカード。要素 i が Unity の Display i+1 に出る（SignageSceneBuilder が組む）")]
        [SerializeField] SignageCardView[] views = new SignageCardView[0];

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
        [Tooltip("切替間隔（秒）の初期値。秒針と同期し、30なら秒針00/30、20なら00/20/40ちょうどで切り替わる（60の約数推奨）。実行中は画面ごとに変えられる")]
        [SerializeField] int switchIntervalSeconds = 30;
        [Tooltip("再生モード（ランダム／番号順）の初期値。実行中の切替は SignageInput の Mode（M・右クリック・パッドY）で、画面ごとに持つ")]
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
        // 画面ごとの再生設定と切り替えの状態（要素 i = Display i+1）。画面の集合はビットで持つ（bit i = Display i+1）
        int[] _interval;
        PlaybackMode[] _mode;
        long[] _lastSlot;         // 壁時計同期用の秒スロット（-1 = 次の境界から数え直す）
        int _animating;           // 切り替え演出の途中の画面
        int _queuedMask, _queuedStep;   // アニメ中に来た手動操作を1つだけ覚える（捨てると「押しても無反応」に見える）
        int All => (1 << views.Length) - 1;
        DateTime _lastReloadDate; // 日次リロードの実施日
        Texture2D _dotTexture, _gradientTexture, _glowTexture;

        System.Random _random = new System.Random();

        SignageInput _input;      // バインドと操作方法の表（SignageInput）
        SignageHud[] _huds;       // 通知・操作方法・長押しゲージ・操作対象の札（画面ごと。実行時に組む）
        Task<CreationsDbUpdater.Result> _dbUpdate;
        bool _quitting;

        const string PrefInterval = "signage.intervalSeconds";
        const string PrefMode = "signage.playbackMode";
        /// <summary>Display 1 は従来のキーのまま（1画面機の保存値をそのまま使う）。2枚目以降は番号を付ける。</summary>
        static string PrefKey(string key, int screen) => screen == 0 ? key : $"{key}.{screen + 1}";

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

            // 2枚目以降の画面（Display）は Activate しないと描かれない。繋がっていない画面のカードは止めて、
            // 見えない演出に CPU を使わない。エディタは Display.displays が常に1枚なので全部残す。
#if !UNITY_EDITOR
            int screens = Mathf.Min(Display.displays.Length, views.Length);
            for (int i = 1; i < screens; i++) Display.displays[i].Activate();
            for (int i = screens; i < views.Length; i++) views[i].gameObject.SetActive(false);
            Array.Resize(ref views, screens);
            Debug.Log("[Signage] 画面: " + string.Join(", ", Display.displays.Select(d => $"{d.systemWidth}x{d.systemHeight}")) +
                      $"（カード {screens}枚）");
#endif

            _input = new SignageInput();

            // 現地で変えた設定は電源を切っても残す（サイネージは長押しで電源オフできる）。
            // 2枚目以降でまだ保存が無い画面は Display 1 の値を引き継ぐ
            _interval = new int[views.Length];
            _mode = new PlaybackMode[views.Length];
            _lastSlot = new long[views.Length];
            _huds = new SignageHud[views.Length];
            for (int i = 0; i < views.Length; i++)
            {
                _interval[i] = PlayerPrefs.GetInt(PrefKey(PrefInterval, i), i > 0 ? _interval[0] : switchIntervalSeconds);
                _mode[i] = (PlaybackMode)PlayerPrefs.GetInt(PrefKey(PrefMode, i), (int)(i > 0 ? _mode[0] : playbackMode));
                _lastSlot[i] = -1;
            }
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
            foreach (var v in views) v.SetClock(DateTime.Now);
            for (int i = 0; i < views.Length; i++)   // 操作UI（SignageHud）は画面ごと。2画面なら起動時にどちらが Display 何番かも出す
            {
                var clock = views[i].clockText;
                if (!clock) continue;
                _huds[i] = SignageHud.Create(clock.canvas, hudFont ? hudFont : clock.font, $"Display {i + 1}");
                _huds[i].Toast((views.Length > 1 ? $"Display {i + 1}｜" : "") + "H・F1 ／ パッド Select で操作方法", 6f);
            }

            if (_records.Count == 0)
            {
                // 止めない: この状態こそ短押し（創作DBの取り直し）で復帰させたいので入力は生かしておく
                Debug.LogError("[Signage] 表示できるレコードが0件。サブモジュール取得と同期状態を確認して。");
                Toast(All, "表示できるレコードが0件。Esc／Start／ホイール短押しで創作DBを取得", 20f);
                return;
            }
            StartCoroutine(AnimateCard(All));
        }

        void Update()
        {
            var now = DateTime.Now;

            // 時計は毎フレーム更新（「:」の点滅・秒表示のため）
            foreach (var v in views) v.SetClock(now);

            // 秒針同期の切替: 実時刻を間隔で割ったスロット境界（00/20/40 や 00/30）で発火。
            // 間隔は画面ごとで、同じ瞬間に境界を迎えた画面はひと組で切り替える
            int due = 0;
            for (int i = 0; i < views.Length; i++)
            {
                long slot = (long)now.TimeOfDay.TotalSeconds / Mathf.Max(5, _interval[i]);
                if (_lastSlot[i] >= 0 && slot != _lastSlot[i]) due |= 1 << i;   // -1（起動直後・間隔の変更直後）は次の境界から
                _lastSlot[i] = slot;
            }
            due &= ~_animating;   // アニメ中に境界を跨いだ画面はその回をスキップ（次の境界で再同期）
            if (due != 0) StartCoroutine(AnimateCard(due));

            HandleInput();
            PollDbUpdate();

            // 日次リロード: OS側がpullした最新DBを毎日 dailyReloadHour 時に取り込む（常時稼働サイネージ向け）
            if (now.Date != _lastReloadDate && now.Hour >= dailyReloadHour && _animating == 0)
            {
                _lastReloadDate = now.Date;
                ReloadRecords();
            }
        }

        // ---- 入力 ----

        void HandleInput()
        {
            if (_quitting) return;

            // 同時押しで操作する画面を絞る（Shift／LT = Display 1、Ctrl／RT = Display 2、無し・両方 = 全画面）。
            // 押している間は、対象の画面に札を出す（どのモニタが Display 何番かをここで知る）
            int target = _input.TargetMask(views.Length);
            bool chip = views.Length > 1 && _input.ModifierHeld;
            for (int i = 0; i < _huds.Length; i++) _huds[i]?.ShowChip(chip && (target & 1 << i) != 0);

            if (_input.Next.WasPressedThisFrame()) RequestCard(target, 0);
            if (_input.PagePrev.WasPressedThisFrame()) RequestCard(target, -1);   // 方向入力: キャラ番号順に前
            if (_input.PageNext.WasPressedThisFrame()) RequestCard(target, 1);    // 　　　　　　　　　　　　後
            if (_input.Mode.WasPressedThisFrame()) TogglePlaybackMode(target);

            if (_input.IntervalCycle.WasPressedThisFrame())
            {
                int at = Array.IndexOf(SignageInput.IntervalChoices, _interval[Screens(target).First()]);
                SetInterval(target, SignageInput.IntervalChoices[(at + 1) % SignageInput.IntervalChoices.Length]);
            }
            for (int i = 0; i < _input.IntervalPick.Length; i++)
                if (_input.IntervalPick[i].WasPressedThisFrame()) SetInterval(target, SignageInput.IntervalChoices[i]);

            if (_input.Help.WasPressedThisFrame())
            {
                bool show = !Screens(target).Any(i => _huds[i] && _huds[i].HelpVisible);
                foreach (int i in Screens(target))
                    _huds[i]?.ShowHelp(show ? SignageInput.HelpText(QuitLabel, StatusLine(), i, views.Length) : null);
            }

            switch (_input.PollSystem(out float hold))
            {
                case SignageInput.Press.Short: StartDbUpdate(); break;
                case SignageInput.Press.Long: StartCoroutine(QuitOrShutdown()); break;
            }
            foreach (var hud in _huds) hud?.Hold(hold, $"長押しで{QuitLabel}…");
        }

        /// <summary>ビット集合に入っている画面の番号（0始まり）。</summary>
        IEnumerable<int> Screens(int mask) => Enumerable.Range(0, views.Length).Where(i => (mask & 1 << i) != 0);

        /// <summary>ログ用の画面名（例 [D1] / [D1+D2]）。実機試験がどの画面に効いたかをこれで見る。</summary>
        string Tag(int mask) => "[D" + string.Join("+D", Screens(mask).Select(i => i + 1)) + "]";

        /// <summary>通知は操作が効いた画面にだけ出す。</summary>
        void Toast(int mask, string message, float seconds = SignageHud.ToastSeconds)
        {
            foreach (int i in Screens(mask)) _huds[i]?.Toast(message, seconds);
        }

        /// <summary>カード切替の要求。step=0 は再生モードに従う「次」、±1 はキャラ番号順の前後。</summary>
        void RequestCard(int mask, int step)
        {
            if ((_animating & mask) != 0) { _queuedMask = mask; _queuedStep = step; return; }
            StartCoroutine(AnimateCard(mask, step));
        }

        void SetInterval(int mask, int seconds)
        {
            foreach (int i in Screens(mask))
            {
                _interval[i] = seconds;
                _lastSlot[i] = -1;              // 変更直後に即切り替わらないよう次の境界から数え直す
                PlayerPrefs.SetInt(PrefKey(PrefInterval, i), seconds);
            }
            PlayerPrefs.Save();
            Toast(mask, $"自動送り: {IntervalLabel(seconds)}");
            Debug.Log($"[Signage] 自動送り間隔: {seconds}秒 {Tag(mask)}");
        }

        static string IntervalLabel(int seconds) => seconds >= 60 && seconds % 60 == 0
            ? $"{seconds / 60}分" : $"{seconds}秒";

        static string ModeLabel(PlaybackMode mode) => mode == PlaybackMode.Random ? "ランダム" : "番号順";

        string StatusLine()
        {
            string Setting(int i) => $"自動送り {IntervalLabel(_interval[i])} ／ 再生モード {ModeLabel(_mode[i])}";
            return views.Length == 1
                ? $"現在: {Setting(0)} ／ 表示対象 {_records.Count}件"
                : string.Join("\n", Screens(All).Select(i => $"Display {i + 1}: {Setting(i)}")) + $"\n表示対象 {_records.Count}件";
        }

        // ---- 創作DBの取り直し（短押し）----

        void StartDbUpdate()
        {
            if (_dbUpdate != null && !_dbUpdate.IsCompleted) { Toast(All, "創作DBを更新中…"); return; }
            Toast(All, "創作DBを更新中…", 120f);
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

            if (!result.Ok) { Toast(All, result.Message, 6f); return; }

            // 取得先が同梱データと違う場合もあるので、成功したら必ずそちらを読み直す
            CreationsDbLoader.ExternalRootOverride = result.DataRoot;
            int count = ReloadRecords();
            Toast(All, count > 0 ? $"{result.Message}（{count}件）" : result.Message, 5f);
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
            Toast(All, $"{QuitLabel}します…", 15f);
            yield return new WaitForSecondsRealtime(0.7f);      // 文字を読ませてから落とす

            if (SystemVerb != null)
            {
                string error = Systemctl(SystemVerb);
                if (error == null) yield break;                 // あとは systemd が止める
                _quitting = false;
                Toast(All, $"{QuitLabel}に失敗: {error}", 6f);
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

        /// <summary>
        /// 各画面に次に出すレコード（mask に無い画面は今のまま）。step=0 は画面ごとの再生モードに従い、
        /// ±1 はキャラ番号順の前後（方向入力）。
        /// </summary>
        NtCharacterRecord[] PickNextRecords(int mask, int step)
        {
            if (_records.Count == 0) return new NtCharacterRecord[0]; // 同期直後など一時的に空のケースを防御
            var current = views.Select(v => IndexOf(_sorted, v.Current)).ToArray();
            var dirs = _mode.Select(m => step != 0 ? step : m == PlaybackMode.Sequential ? 1 : 0).ToArray();
            return PickNext(current, _sorted.Count, dirs, mask, _random).Select(i => i < 0 ? null : _sorted[i]).ToArray();
        }

        /// <summary>
        /// 各画面に次に出すレコードの位置。current[i] は画面 i に今出ている位置（無ければ -1）、count は候補の件数（1以上）、
        /// dirs[i] は画面 i の進み方（0=ランダム、±1=番号順・方向入力）、mask は今回切り替える画面（それ以外は current のまま返す）。
        /// 番号順の画面はひと組として扱い、組の先頭の画面の位置から組の枚数ぶん送って順に並べる
        /// （2画面同時なら Display 1 の続きへ揃い、片方だけなら自分の位置から ±1）。
        /// ランダムの画面は、自分の直前の個体と、他の画面に出る（出ている）個体を避ける（件数が足りなければ諦める）。
        /// SignageSelfCheck から検査する。
        /// </summary>
        public static int[] PickNext(int[] current, int count, int[] dirs, int mask, System.Random random)
        {
            var next = (int[])current.Clone();
            var switching = Enumerable.Range(0, next.Length).Where(i => (mask & 1 << i) != 0).ToArray();
            int group = switching.Count(i => dirs[i] != 0), first = 0, placed = 0, decided = ~mask;
            foreach (int i in switching.OrderBy(s => dirs[s] == 0))   // 番号順を先に決める（ランダム側がそれを避けられるように）
            {
                if (dirs[i] != 0)
                {
                    if (placed == 0) first = current[i] < 0 ? 0 : current[i] + dirs[i] * group;
                    next[i] = (((first + placed++) % count) + count) % count;
                }
                else
                {
                    // 他の画面の個体: 切り替えない画面は今出ているもの、この回に先に決めた画面は決めたもの
                    var taken = Enumerable.Range(0, next.Length)
                        .Where(j => j != i && (decided & 1 << j) != 0 && next[j] >= 0).Select(j => next[j]).ToArray();
                    int pick;
                    do { pick = random.Next(count); }
                    while ((count > taken.Length && Array.IndexOf(taken, pick) >= 0) ||
                           (count > taken.Length + 1 && pick == current[i]));
                    next[i] = pick;
                }
                decided |= 1 << i;
            }
            return next;
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

        void TogglePlaybackMode(int mask)
        {
            // 番号順は表示中の個体の次から続く（PickNextRecord が毎回現在位置を引き直すため、覚える値は無い）。
            // 複数の画面をまとめて切り替えるときは先頭の画面の逆へ揃える（画面ごとに反転すると入れ替わるだけになる）
            var mode = _mode[Screens(mask).First()] == PlaybackMode.Random ? PlaybackMode.Sequential : PlaybackMode.Random;
            foreach (int i in Screens(mask))
            {
                _mode[i] = mode;
                PlayerPrefs.SetInt(PrefKey(PrefMode, i), (int)mode);
            }
            PlayerPrefs.Save();
            Toast(mask, $"再生モード: {ModeLabel(mode)}");
            Debug.Log($"[Signage] 再生モード: {mode} {Tag(mask)}");
        }

        /// <summary>
        /// mask の画面をひと組で切り替える（同じ瞬間に切り替わる画面は同じ組）。組が違えば並行して走るので、
        /// 呼ぶ側は対象の画面が演出の途中でない（_animating に入っていない）ことを確かめてから呼ぶ。
        /// </summary>
        IEnumerator AnimateCard(int mask, int step = 0)
        {
            _animating |= mask;
            var group = Screens(mask).Select(i => views[i]).ToArray();
            float alpha = 1f;

            // フェードアウト（数字はカウントアップしながら消える。スクランブルは大型番号のみ）。まだ何も出していなければ飛ばす
            if (group.All(v => v.Current != null))
            {
                while (alpha > 0f)
                {
                    int counted = Mathf.FloorToInt(numAnimDuration * Mathf.Pow(1f - alpha, acceleration));
                    foreach (var v in group)
                    {
                        v.SetAnimatedNumber(v.Current.NumValue + counted);
                        v.SetBigNumberScramble(alpha);
                        v.SetAlpha(alpha);
                    }
                    alpha -= Time.deltaTime / animationTime;
                    yield return null;
                }
                foreach (var v in group) v.SetAlpha(0f);
            }
            alpha = 0f;

            // 次レコードの決定・画像読込（バリアントはランダム）
            var next = PickNextRecords(mask, step);
            if (next.Length == 0) { _animating &= ~mask; yield break; }
            Debug.Log($"[Signage] カード: {string.Join(" / ", Screens(mask).Select(i => next[i].NumRaw))}" +
                      (step != 0 ? $"（方向入力 {step:+#;-#}）" : "") + " " + Tag(mask));
            foreach (int i in Screens(mask))
            {
                string imagePath = next[i].ImagePaths[_random.Next(next[i].ImagePaths.Count)];
                views[i].Apply(next[i], CreationsDbLoader.LoadTexture(imagePath));
                views[i].SetAlpha(0f);
            }

            // フェードイン（数字はカウントダウンで収束、文字はスクランブル→確定）
            // 文字スクランブルは経過時間ベースで進行し、textAnimSpeed < 1 の場合は
            // フェード完了後も継続して全文の演出を見せる。
            float textDuration = animationTime / Mathf.Max(0.05f, textAnimSpeed);
            float elapsed = 0f;
            while (alpha < 1f)
            {
                int counted = Mathf.FloorToInt(numAnimDuration * Mathf.Pow(1f - alpha, acceleration));
                foreach (var v in group)
                {
                    v.SetAnimatedNumber(v.Current.NumValue - counted);
                    v.SetTextProgress(elapsed / textDuration);
                    v.SetAlpha(alpha);
                }
                elapsed += Time.deltaTime;
                alpha += Time.deltaTime / animationTime;
                yield return null;
            }
            foreach (var v in group)
            {
                v.SetAlpha(1f);
                v.SetAnimatedNumber(v.Current.NumValue);
            }

            // 残りの文字スクランブルを完走させる
            while (elapsed < textDuration)
            {
                foreach (var v in group) v.SetTextProgress(elapsed / textDuration);
                elapsed += Time.deltaTime;
                yield return null;
            }
            foreach (var v in group)
            {
                v.SetTextProgress(1f);
                v.SetNumberFinal(); // 大型番号を原文表記（例 "222A"）へ着地
            }
            _animating &= ~mask;

            // アニメ中に押された分を1回だけ消化する（対象の画面がどれも空いてから）
            if (_queuedMask != 0 && (_queuedMask & _animating) == 0)
            {
                int queued = _queuedMask;
                _queuedMask = 0;
                StartCoroutine(AnimateCard(queued, _queuedStep));
            }
        }

        // ---- 背景デザイン（3層: 縦グラデーション / エッジ強調ドット / キャラ背後グロー）----

        public void SetupBackground() // SignageCapture（エディタでのカード撮影）からも呼ぶ
        {
            foreach (var v in views) SetupBackground(v);
        }

        void SetupBackground(SignageCardView view)   // テクスチャは全画面で共有する
        {
            if (!view) return;

            if (view.gradientOverlay)
            {
                if (!_gradientTexture) _gradientTexture = CreateVerticalGradientTexture();
                view.gradientOverlay.texture = _gradientTexture;
                view.gradientOverlay.uvRect = new Rect(0f, 0f, 1f, 1f);
            }

            if (view.dotsOverlay)
            {
                if (!_dotTexture) _dotTexture = CreateEdgeDotsTexture(960, 540);
                view.dotsOverlay.texture = _dotTexture;
                view.dotsOverlay.uvRect = new Rect(0f, 0f, 1f, 1f);
            }

            if (view.characterGlow)
            {
                if (!_glowTexture) _glowTexture = CreateRadialGlowTexture(512, glowAlpha);
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
