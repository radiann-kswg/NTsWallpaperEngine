using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NTsWallpaperEngine.Signage
{
    /// <summary>
    /// サイネージ本体。創作DB（サブモジュール駆動）からレコードを読み込み、
    /// 毎分（またはクリック時）にカードを切り替える。数字カウントアニメーションは必須要件（AGENTS.md 6章）。
    /// 旧 CharacterAssetsDB のアニメーション仕様（カウント演出・イージング・フェード）を踏襲。
    /// </summary>
    public class SignageController : MonoBehaviour
    {
        [SerializeField] SignageCardView view;

        [Header("Animation (旧CharacterAssetsDB準拠)")]
        [SerializeField] int numAnimDuration = 300;
        [SerializeField] float acceleration = 2.65f;
        [SerializeField] float animationTime = 1.4f;
        [Tooltip("文字スクランブルがフェードインより先に確定する倍率")]
        [SerializeField] float textAnimSpeed = 1.35f;

        public enum PlaybackMode
        {
            Random,      // ランダム再生（直前と同じ個体は連続しない）
            Sequential,  // 番号順再生（Num昇順で巡回）
        }

        [Header("Switching")]
        [Tooltip("切替間隔（秒）。秒針と同期し、30なら秒針00/30、20なら00/20/40ちょうどで切り替わる（60の約数推奨）")]
        [SerializeField] int switchIntervalSeconds = 30;
        [Tooltip("再生モード（ランダム／番号順）。実行中は modeToggleKey か右クリックで切替")]
        [SerializeField] PlaybackMode playbackMode = PlaybackMode.Random;
        [SerializeField] KeyCode modeToggleKey = KeyCode.M;

        [Header("Daily DB reload (RPi常時稼働向け)")]
        [Tooltip("毎日この時刻(時)にDBを再読込する。OS側の日次pull（scripts/rpi/update-creationsdb.sh）とセットで運用")]
        [SerializeField, Range(0, 23)] int dailyReloadHour = 4;

        [Header("Background design")]
        [Tooltip("ドット1周期のピクセル数（生成テクスチャ内）")]
        [SerializeField] int dotCellSize = 56;
        [SerializeField, Range(0f, 1f)] float dotRadius = 0.24f;
        [SerializeField, Range(0f, 1f)] float dotMaxAlpha = 0.55f;
        [Tooltip("画面中央でドットをどこまで弱めるか (0=消える)")]
        [SerializeField, Range(0f, 1f)] float dotCenterFade = 0.06f;
        [SerializeField, Range(0f, 1f)] float glowAlpha = 0.5f;

        List<NtCharacterRecord> _records = new List<NtCharacterRecord>();
        List<NtCharacterRecord> _sorted = new List<NtCharacterRecord>();  // 番号順再生用
        NtCharacterRecord _next;
        int _lastIndex = -1;      // Random: _records / Sequential: _sorted のインデックス
        float _alpha;
        bool _isAnimating;
        long _lastSlot = -1;  // 壁時計同期用の秒スロット
        DateTime _lastReloadDate; // 日次リロードの実施日
        Texture2D _dotTexture, _gradientTexture, _glowTexture;

        System.Random _random = new System.Random();

        void Start()
        {
            _records = CreationsDbLoader.LoadAll();
            RebuildSortedList();
            _lastReloadDate = DateTime.Now.Date;
            SetupBackground();
            if (view) view.SetClock(DateTime.Now);

            if (_records.Count == 0)
            {
                Debug.LogError("[Signage] 表示できるレコードが0件。サブモジュール取得と同期状態を確認して。");
                enabled = false;
                return;
            }
            StartCoroutine(AnimateCard(true));
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

            // クリック/タップでも切り替え（旧実装踏襲）
            if (Input.GetMouseButtonDown(0) && !_isAnimating)
                StartCoroutine(AnimateCard());

            // 再生モード切替（Mキー or 右クリック）
            if (Input.GetKeyDown(modeToggleKey) || Input.GetMouseButtonDown(1))
                TogglePlaybackMode();

            // 日次リロード: OS側がpullした最新DBを毎日 dailyReloadHour 時に取り込む（常時稼働サイネージ向け）
            if (now.Date != _lastReloadDate && now.Hour >= dailyReloadHour && !_isAnimating)
            {
                _lastReloadDate = now.Date;
                var reloaded = CreationsDbLoader.LoadAll();
                if (reloaded.Count > 0)
                {
                    _records = reloaded;
                    RebuildSortedList();
                    _lastIndex = -1;
                    Debug.Log($"[Signage] 日次リロード完了: {reloaded.Count}件");
                }
                else
                {
                    Debug.LogWarning("[Signage] 日次リロードで0件だったため、現行データを継続使用");
                }
            }
        }

        NtCharacterRecord PickNextRecord()
        {
            if (_records.Count == 0) return null; // 同期直後など一時的に空のケースを防御
            if (_records.Count == 1) return _records[0];

            if (playbackMode == PlaybackMode.Sequential)
            {
                _lastIndex = (_lastIndex + 1) % _sorted.Count;
                return _sorted[_lastIndex];
            }

            int index;
            do { index = _random.Next(_records.Count); } while (index == _lastIndex);
            _lastIndex = index;
            return _records[index];
        }

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
            playbackMode = playbackMode == PlaybackMode.Random ? PlaybackMode.Sequential : PlaybackMode.Random;

            // 番号順へ切り替えたら、現在表示中の個体から順番を継続する
            if (playbackMode == PlaybackMode.Sequential && view && view.Current != null)
                _lastIndex = _sorted.IndexOf(view.Current);
            else
                _lastIndex = -1;

            Debug.Log($"[Signage] 再生モード: {playbackMode}");
        }

        IEnumerator AnimateCard(bool onStarting = false)
        {
            if (_isAnimating && !onStarting) yield break;
            _isAnimating = true;

            // フェードアウト（数字はカウントアップしながら消える）
            if (!onStarting && view.Current != null)
            {
                int baseNum = view.Current.NumValue;
                while (_alpha > 0f)
                {
                    int shown = baseNum + Mathf.FloorToInt(numAnimDuration * Mathf.Pow(1f - _alpha, acceleration));
                    view.SetAnimatedNumber(shown);
                    view.SetAlpha(_alpha);
                    _alpha -= Time.deltaTime / animationTime;
                    yield return null;
                }
                _alpha = 0f;
                view.SetAlpha(0f);
            }

            // 次レコードの決定・画像読込（バリアントはランダム）
            _next = PickNextRecord();
            if (_next == null) { _isAnimating = false; yield break; }
            string imagePath = _next.ImagePaths[_random.Next(_next.ImagePaths.Count)];
            Texture2D texture = CreationsDbLoader.LoadTexture(imagePath);
            view.Apply(_next, texture);
            view.SetAlpha(0f);

            // フェードイン（数字はカウントダウンで収束、文字はスクランブル→確定）
            int target = _next.NumValue;
            while (_alpha < 1f)
            {
                int shown = target - Mathf.FloorToInt(numAnimDuration * Mathf.Pow(1f - _alpha, acceleration));
                view.SetAnimatedNumber(shown);
                view.SetTextProgress(_alpha * textAnimSpeed);
                view.SetAlpha(_alpha);
                _alpha += Time.deltaTime / animationTime;
                yield return null;
            }
            _alpha = 1f;
            view.SetAnimatedNumber(target);
            view.SetNumberFinal(); // バッジ表記（例 "222A"）へ着地
            view.SetTextProgress(1f);
            view.SetAlpha(1f);
            _isAnimating = false;
        }

        // ---- 背景デザイン（3層: 縦グラデーション / エッジ強調ドット / キャラ背後グロー）----

        void SetupBackground()
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
            if (_dotTexture) Destroy(_dotTexture);
            if (_gradientTexture) Destroy(_gradientTexture);
            if (_glowTexture) Destroy(_glowTexture);
        }
    }
}
