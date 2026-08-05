using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NTsWallpaperEngine.Signage
{
    /// <summary>
    /// emstk-rv コアフォルダカード風のUI要素一式への参照と、レコード反映処理。
    /// レイアウト: 右上=型番/FormalName(数字アニメ)/英名、中央=corefolder画像、左下=和名/機体名、左上=時計。
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
        public AspectRatioFitter characterAspect;

        [Header("Header (top-right)")]
        public TMP_Text modelNumberText;   // 例 "APHR-NT IV+[R]IV"
        public TMP_Text formalNameText;    // 例 "NUMBERTALES #44"（#部分が数字アニメ対象）
        public TMP_Text nameEnText;        // 例 "FOLFOURN"

        [Header("Footer (bottom-left)")]
        public TMP_Text nameJpText;        // 例 "シトシ"
        public TMP_Text modelNameJpText;   // 例 "ナンバーテールズ 正規+改良型4号機(44番機)"

        [Header("Clock (top-left)")]
        public TMP_Text clockText;

        [Header("Colors")]
        public Color headerColor = new Color(1f, 1f, 1f, 0.92f);
        public Color footerColor = new Color(50f / 255f, 50f / 255f, 50f / 255f);
        [Range(0f, 1f)] public float backgroundPastel = 0.62f;

        NtCharacterRecord _current;
        Texture2D _texture;
        string _formalPrefix = "NUMBERTALES";
        int _numDigits = 2;

        public NtCharacterRecord Current => _current;

        /// <summary>レコードの静的テキスト・画像・背景色を反映する（数字は SetAnimatedNumber で駆動）。</summary>
        public void Apply(NtCharacterRecord record, Texture2D texture)
        {
            _current = record;

            if (modelNumberText) modelNumberText.text = record.ModelNumber ?? "";
            _formalPrefix = ExtractFormalPrefix(record.FormalNameEN);
            _numDigits = Mathf.Max(1, (record.NumBadge ?? record.NumValue.ToString()).Replace("-", "").Length);
            if (nameEnText) nameEnText.text = FormatNameEn(record.NameEN);
            if (nameJpText) nameJpText.text = FormatNameJp(record.NameJP);
            if (modelNameJpText) modelNameJpText.text = record.ModelNameJP ?? "";

            SetTexture(texture);
            SetAnimatedNumber(record.NumValue);

            ApplyTheme(record.HasThemeColor ? record.ThemeColor : new Color(0.72f, 0.85f, 0.90f));
        }

        /// <summary>テーマ色を背景3層（ベース・グラデーション・ドット）へ展開する。</summary>
        public void ApplyTheme(Color theme)
        {
            // ベース: 明るいパステル
            if (background)
                background.color = Color.Lerp(theme, Color.white, backgroundPastel);

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
                if (characterAspect && texture)
                    characterAspect.aspectRatio = (float)texture.width / texture.height;
            }
        }

        /// <summary>数字アニメーション中の表示値を反映（"NUMBERTALES #044" 形式）。</summary>
        public void SetAnimatedNumber(int shownValue)
        {
            if (!formalNameText) return;
            int modulo = (int)Mathf.Pow(10, _numDigits);
            int v = ((shownValue % modulo) + modulo) % modulo;
            formalNameText.text = $"{_formalPrefix} #{v.ToString("D" + _numDigits)}";
        }

        /// <summary>フェード用に全表示要素へアルファを適用。</summary>
        public void SetAlpha(float alpha)
        {
            if (characterImage)
                characterImage.color = new Color(1f, 1f, 1f, alpha);

            ApplyAlpha(modelNumberText, headerColor, alpha);
            ApplyAlpha(formalNameText, headerColor, alpha);
            ApplyAlpha(nameEnText, headerColor, alpha);
            ApplyAlpha(nameJpText, footerColor, alpha);
            ApplyAlpha(modelNameJpText, footerColor, alpha);
        }

        static void ApplyAlpha(TMP_Text text, Color baseColor, float alpha)
        {
            if (!text) return;
            text.color = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * alpha);
        }

        public void SetClock(System.DateTime now)
        {
            if (clockText) clockText.text = $"{now.Hour:D2} {now.Minute:D2}";
        }

        // ---- 表記整形 ----

        /// <summary>"NumberTales #44" → "NUMBERTALES" / "NumberTales [Dev.]" → "NUMBERTALES [DEV.]"</summary>
        static string ExtractFormalPrefix(string formalNameEn)
        {
            if (string.IsNullOrEmpty(formalNameEn)) return "NUMBERTALES";
            string s = Regex.Replace(formalNameEn, @"\s*#[\w\-\.]+\s*$", "");
            return s.Trim().ToUpperInvariant();
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

        /// <summary>"44(シトシ)" → "シトシ" ／ "バイナ(ツギ)" → "バイナ(ツギ)"（先頭が数字のときだけ中身を抽出）</summary>
        static string FormatNameJp(string nameJp)
        {
            if (string.IsNullOrEmpty(nameJp)) return "";
            var m = Regex.Match(nameJp, @"^\s*(?<head>[^(（]*?)\s*[（(]\s*(?<inner>.+?)\s*[)）]\s*$");
            if (m.Success && Regex.IsMatch(m.Groups["head"].Value.Trim(), @"^[\d\-]*$"))
                return m.Groups["inner"].Value.Trim();
            return nameJp.Trim();
        }
    }
}
