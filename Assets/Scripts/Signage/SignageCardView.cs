using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NTsWallpaperEngine.Signage
{
    /// <summary>
    /// emstk-rv コアフォルダカード風のUI要素一式への参照と、レコード反映処理（英文表示のみ）。
    /// レイアウト: 左=大型番号（カウント演出）、中央右=corefolder画像、右上=型番/FormalName/英名、
    /// 左下=英語機体名、左上=時計。
    /// テキストは「スクランブル→確定」の文字アニメーションで表示する。
    /// </summary>
    public class SignageCardView : MonoBehaviour
    {
        [Header("Background")]
        public Image background;          // ベース色（テーマ色のパステル）
        public RawImage gradientOverlay;  // 縦グラデーション（テーマ色を濃くした層）
        public RawImage dotsOverlay;      // ドット柄（端ほど強い）
        public RawImage characterGlow;    // キャラ背後のソフトグロー

        [Header("Character")]
        public RawImage characterImage;
        [Tooltip("コアフォルダ画像の一律縮小率（元画像はキャラ間でサイズ校正済みのため、全員同じ倍率で縮小して相対サイズを保持する）")]
        [Range(0.1f, 2f)] public float characterScale = 0.92f;

        [Header("Big number (left)")]
        public TMP_Text bigNumberText;    // 例 "044"（旧シーン踏襲の大型番号・カウント演出対象）

        [Header("Header (top-right)")]
        public TMP_Text modelNumberText;   // 例 "APHR-NT IV+[R]IV"
        public TMP_Text formalNameText;    // 例 "NUMBERTALES #044"（#部分が数字アニメ対象）
        public TMP_Text nameEnText;        // 例 "FOLFOURN"

        [Header("Profile (bottom-right)")]
        public TMP_Text profileText;       // Gender / Class / Concept Age
        public TMP_Text modelNameEnText;   // 機体名EN（プロフィール最下行・一回り大きいフォント）

        [Header("Clock (top-left)")]
        public TMP_Text clockText;

        [Header("Colors")]
        [Tooltip("背景が明るいときの文字色")]
        public Color darkTextColor = new Color(45f / 255f, 45f / 255f, 45f / 255f);
        [Tooltip("背景が暗いときの文字色")]
        public Color lightTextColor = Color.white;
        [Tooltip("この知覚輝度以上なら背景を「明るい」と判定")]
        [Range(0f, 1f)] public float luminanceThreshold = 0.55f;
        Color headerColor = new Color(1f, 1f, 1f, 0.92f);
        Color footerColor = new Color(50f / 255f, 50f / 255f, 50f / 255f);
        [Tooltip("大型番号の不透明度（透かし風）")]
        [Range(0f, 1f)] public float bigNumberAlpha = 0.42f;
        [Range(0f, 1f)] public float backgroundPastel = 0.62f;

        const string ScramblePool = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ#-+.[]";

        static readonly Regex NumTokenRegex = new Regex(@"#[\w\.\-]+");

        NtCharacterRecord _current;
        Texture2D _texture;
        string _formalTemplate = "NUMBERTALES";  // FormalName_EN全文（#番号トークンにアニメ数字を埋め込む）
        int _numDigits = 2;
        bool _numericNum = true;     // Numが数字を持つか（%や∞などの特殊個体はfalse）
        string _rawNumDisplay = "";  // 非数値個体の表示用
        string _numBadgeDisplay = ""; // アニメ確定後の最終表記（例 "222A"）

        // 文字アニメーションの確定文字列（スクランブル対象）
        string _targetModelNumber = "", _targetNameEn = "", _targetModelNameEn = "", _targetProfile = "";
        float _textProgress = 1f;

        public NtCharacterRecord Current => _current;

        /// <summary>レコードの静的テキスト・画像・背景色を反映する（数字は SetAnimatedNumber、文字は SetTextProgress で駆動）。</summary>
        public void Apply(NtCharacterRecord record, Texture2D texture)
        {
            _current = record;

            // 複数行の型番は1行に連結（表記は原文のまま・大文字化しない）
            _targetModelNumber = (record.ModelNumber ?? "").Replace("\r", "").Replace("\n", " / ");
            _targetNameEn = FormatNameEnMultiline(record.NameEN);
            _targetModelNameEn = (record.ModelNameEN ?? "").Replace("\r", "").Replace("\n", " / ");
            _targetProfile = BuildProfile(record);
            // 正式名称は原文表記（小文字あり）のまま使用
            _formalTemplate = string.IsNullOrEmpty(record.FormalNameEN)
                ? "NumberTales"
                : record.FormalNameEN.Replace("\r", "").Replace("\n", " ").Trim();
            // Numの先頭数字のみを表示（"2-alt"→2, "10-alt"→10, "222-mp"→222 のようにsuffixは除去）
            var numMatch = Regex.Match(record.NumRaw ?? "", @"^\d+");
            _numericNum = numMatch.Success;
            _rawNumDisplay = record.NumRaw ?? "";
            _numDigits = _numericNum ? numMatch.Value.Length : 1;
            // 最終表記はできるだけ数字のみ:
            //   "222A"→"222" / "2-alt"→"2" / "777.Jackpot"→"777"（suffixに数字が無ければ除去）
            //   "3x11"→"3x11"（残部に数字を含む数式表記はそのまま） / "000"→"000"（ゼロ埋め保持） / "%"や"∞"はそのまま
            _numBadgeDisplay = ExtractDisplayNum(_rawNumDisplay);

            SetTexture(texture);
            SetAnimatedNumber(record.NumValue);
            SetTextProgress(0f);

            ApplyTheme(record.HasThemeColor ? record.ThemeColor : new Color(0.72f, 0.85f, 0.90f));
        }

        /// <summary>テーマ色を背景3層（ベース・グラデーション・ドット）と文字色へ展開する。</summary>
        public void ApplyTheme(Color theme)
        {
            // ベース: 明るいパステル
            var baseColor = Color.Lerp(theme, Color.white, backgroundPastel);
            if (background)
                background.color = baseColor;

            // 背景の知覚輝度で文字色を全体統一（明るい背景=黒系 / 暗い背景=白系）
            float luminance = 0.299f * baseColor.r + 0.587f * baseColor.g + 0.114f * baseColor.b;
            Color textColor = luminance >= luminanceThreshold ? darkTextColor : lightTextColor;
            headerColor = new Color(textColor.r, textColor.g, textColor.b, 0.92f);
            footerColor = textColor;
            if (clockText)
                clockText.color = new Color(textColor.r, textColor.g, textColor.b, 0.85f);

            // グラデーション: テーマ色をやや濃く・彩度高めにした層（テクスチャ側は白+縦αランプ）
            if (gradientOverlay)
            {
                Color.RGBToHSV(theme, out float h, out float s, out float v);
                var deep = Color.HSVToRGB(h, Mathf.Clamp01(s * 1.15f + 0.05f), Mathf.Clamp01(v * 0.92f));
                gradientOverlay.color = new Color(deep.r, deep.g, deep.b, 0.55f);
            }

            // ドット: テーマ色を白へ寄せた色で、背景より一段明るく
            if (dotsOverlay)
            {
                var dot = Color.Lerp(theme, Color.white, 0.85f);
                dotsOverlay.color = new Color(dot.r, dot.g, dot.b, 1f);
            }
        }

        void SetTexture(Texture2D texture)
        {
            if (_texture && _texture != texture) Destroy(_texture);
            _texture = texture;
            if (characterImage)
            {
                characterImage.texture = texture;
                // ネイティブ解像度 × 一律スケール（キャラ間のサイズ校正を保持）
                if (texture)
                    characterImage.rectTransform.sizeDelta =
                        new Vector2(texture.width, texture.height) * characterScale;
            }
        }

        /// <summary>数字アニメーション中の表示値を反映（大型番号 と "NUMBERTALES #044" の両方）。</summary>
        public void SetAnimatedNumber(int shownValue)
        {
            // 数字を持たない特殊個体（% や ∞ など）はカウント演出なしでそのまま表示
            if (!_numericNum)
            {
                if (bigNumberText) bigNumberText.text = _rawNumDisplay;
                if (formalNameText) formalNameText.text = _formalTemplate;
                return;
            }

            int modulo = (int)Mathf.Pow(10, _numDigits);
            int v = ((shownValue % modulo) + modulo) % modulo;
            string digits = v.ToString("D" + _numDigits);

            if (bigNumberText) bigNumberText.text = digits;
            if (formalNameText)
            {
                // FormalName_EN 全文の「#番号」トークン（最初の1つ）にアニメ中の数字を埋め込む
                formalNameText.text = NumTokenRegex.IsMatch(_formalTemplate)
                    ? NumTokenRegex.Replace(_formalTemplate, "#" + digits, 1)
                    : $"{_formalTemplate} #{digits}";
            }
        }

        /// <summary>カウント演出の確定後に呼ぶ。大型番号を確定表記（数字のみ優先）で着地させる。</summary>
        public void SetNumberFinal()
        {
            if (bigNumberText && !string.IsNullOrEmpty(_numBadgeDisplay))
                bigNumberText.text = _numBadgeDisplay;
        }

        /// <summary>
        /// Num の確定表示形式。先頭数字の後ろに数字を含まないsuffixが付く場合のみ数字部分へ丸める。
        /// 例: "222A"→"222", "2-alt"→"2", "777.Jackpot"→"777", "3x11"→"3x11", "000"→"000", "%"→"%"
        /// </summary>
        static string ExtractDisplayNum(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var m = Regex.Match(raw, @"^\d+");
            if (!m.Success) return raw;                     // 数字を持たない特殊個体はそのまま
            string rest = raw.Substring(m.Value.Length);
            return Regex.IsMatch(rest, @"\d") ? raw : m.Value; // 残部に数字→数式表記なのでそのまま
        }

        /// <summary>
        /// 文字アニメーション進行度 (0..1)。未確定位置はスクランブル文字で表示し、進行に応じて左から確定する。
        /// </summary>
        public void SetTextProgress(float progress)
        {
            _textProgress = Mathf.Clamp01(progress);
            ApplyScramble(modelNumberText, _targetModelNumber, _textProgress);
            ApplyScramble(nameEnText, _targetNameEn, _textProgress);
            ApplyScramble(modelNameEnText, _targetModelNameEn, _textProgress);
            ApplyScrambleValues(profileText, _targetProfile, _textProgress);
        }

        /// <summary>
        /// プロフィール用: 各行の「ラベル: 」は常に確定表示し、値の部分（DBの値）だけをスクランブル→確定させる。
        /// </summary>
        static void ApplyScrambleValues(TMP_Text text, string target, float progress)
        {
            if (!text) return;
            if (string.IsNullOrEmpty(target)) { text.text = ""; return; }
            if (progress >= 1f) { text.text = target; return; }

            var lines = target.Split('\n');
            var sb = new StringBuilder(target.Length);
            for (int li = 0; li < lines.Length; li++)
            {
                string line = lines[li];
                int sep = line.IndexOf(": ", System.StringComparison.Ordinal);
                if (sep < 0)
                {
                    AppendScrambled(sb, line, progress);
                }
                else
                {
                    sb.Append(line, 0, sep + 2);                      // ラベルは固定
                    AppendScrambled(sb, line.Substring(sep + 2), progress); // 値のみアニメーション
                }
                if (li < lines.Length - 1) sb.Append('\n');
            }
            text.text = sb.ToString();
        }

        /// <summary>
        /// リッチテキストタグ対応のスクランブル追記。&lt;size&gt;等のタグはそのまま透過し、
        /// 可視文字だけを進行度に応じて左から確定させる。
        /// </summary>
        static void AppendScrambled(StringBuilder sb, string target, float progress)
        {
            // タグを除いた可視文字数を数える
            int visibleCount = 0;
            for (int i = 0; i < target.Length; i++)
            {
                if (target[i] == '<')
                {
                    int close = target.IndexOf('>', i);
                    if (close >= 0) { i = close; continue; }
                }
                visibleCount++;
            }

            int revealed = Mathf.FloorToInt(visibleCount * progress);
            int visibleIndex = 0;
            for (int i = 0; i < target.Length; i++)
            {
                char c = target[i];
                if (c == '<')
                {
                    int close = target.IndexOf('>', i);
                    if (close >= 0)
                    {
                        sb.Append(target, i, close - i + 1); // タグはそのまま
                        i = close;
                        continue;
                    }
                }

                if (visibleIndex < revealed || char.IsWhiteSpace(c)) sb.Append(c);
                else sb.Append(ScramblePool[Random.Range(0, ScramblePool.Length)]);
                visibleIndex++;
            }
        }

        /// <summary>プロフィール文字列を組み立てる（Heightは表示しない。機体名ENは別要素 modelNameEnText 側）。</summary>
        static string BuildProfile(NtCharacterRecord record)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(record.GenderType))
                sb.Append("Gender: ").Append(record.GenderType).Append('\n');
            if (record.ClassNames.Count > 0)
                sb.Append("Class: ").Append(string.Join(", ", record.ClassNames)).Append('\n');
            if (!string.IsNullOrEmpty(record.ConceptAge))
            {
                sb.Append("Concept Age: ").Append(record.ConceptAge);
                // {value, about_EN} 形式の注記は半角括弧＋小さめ表示で追記
                if (!string.IsNullOrEmpty(record.ConceptAgeAboutEN))
                    sb.Append(" <size=70%>(").Append(record.ConceptAgeAboutEN).Append(")</size>");
            }
            return sb.ToString().TrimEnd('\n');
        }

        static void ApplyScramble(TMP_Text text, string target, float progress)
        {
            if (!text) return;
            if (string.IsNullOrEmpty(target)) { text.text = ""; return; }
            if (progress >= 1f) { text.text = target; return; }

            int revealed = Mathf.FloorToInt(target.Length * progress);
            var sb = new StringBuilder(target.Length);
            sb.Append(target, 0, revealed);
            for (int i = revealed; i < target.Length; i++)
            {
                char c = target[i];
                // 空白・記号の位置はそのまま残すと単語の輪郭が見えて読みやすい
                if (char.IsWhiteSpace(c)) sb.Append(c);
                else sb.Append(ScramblePool[Random.Range(0, ScramblePool.Length)]);
            }
            text.text = sb.ToString();
        }

        /// <summary>フェード用に全表示要素へアルファを適用。</summary>
        public void SetAlpha(float alpha)
        {
            if (characterImage)
                characterImage.color = new Color(1f, 1f, 1f, alpha);

            ApplyAlpha(bigNumberText, footerColor, alpha * bigNumberAlpha);
            ApplyAlpha(modelNumberText, headerColor, alpha);
            ApplyAlpha(formalNameText, headerColor, alpha);
            ApplyAlpha(nameEnText, headerColor, alpha);
            ApplyAlpha(modelNameEnText, footerColor, alpha);
            ApplyAlpha(profileText, footerColor, alpha);
        }

        static void ApplyAlpha(TMP_Text text, Color baseColor, float alpha)
        {
            if (!text) return;
            text.color = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * alpha);
        }

        /// <summary>
        /// 時計表示。「:」は0.5秒周期で点滅（非表示時も透明表示で幅を保持し、桁ズレを防ぐ）。
        /// 時分の後ろに小さく秒、下段に小さく日付（英語曜日）を表示する。
        /// </summary>
        public void SetClock(System.DateTime now)
        {
            if (!clockText) return;
            bool colonVisible = now.Millisecond < 500;
            string colon = colonVisible ? ":" : "<alpha=#00>:<alpha=#FF>";
            string date = now.ToString("yyyy/MM/dd ddd", System.Globalization.CultureInfo.InvariantCulture);
            clockText.text =
                $"{now.Hour:D2}{colon}{now.Minute:D2}<size=50%> {now.Second:D2}</size>\n" +
                $"<size=45%>{date}</size>";
        }

        // ---- 表記整形 ----

        /// <summary>複数行の Name_EN を行ごとに整形して連結（例 "222(Doppels)\n222(Doppelgans)" → "DOPPELS\nDOPPELGANS"）。</summary>
        static string FormatNameEnMultiline(string nameEn)
        {
            if (string.IsNullOrEmpty(nameEn)) return "";
            var lines = nameEn.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++) lines[i] = FormatNameEn(lines[i]);
            return string.Join("\n", lines);
        }

        /// <summary>"44(Folfourn)" → "FOLFOURN" ／ "Binor (Twicy)" → "BINOR(TWICY)"</summary>
        static string FormatNameEn(string nameEn)
        {
            if (string.IsNullOrEmpty(nameEn)) return "";
            var m = Regex.Match(nameEn, @"^\s*(?<head>[^(（]*?)\s*[（(]\s*(?<inner>.+?)\s*[)）]\s*$");
            if (m.Success)
            {
                string head = m.Groups["head"].Value.Trim();
                string inner = m.Groups["inner"].Value.Trim();
                if (Regex.IsMatch(head, @"^[\d\-]*$"))
                    return inner.ToUpperInvariant();
                return $"{head.ToUpperInvariant()}({inner.ToUpperInvariant()})";
            }
            return nameEn.Trim().ToUpperInvariant();
        }
    }
}
