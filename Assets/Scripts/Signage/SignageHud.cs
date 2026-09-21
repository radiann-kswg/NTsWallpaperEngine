using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NTsWallpaperEngine.Signage
{
    /// <summary>
    /// 操作用のオーバーレイ（操作方法パネル・通知トースト・長押しゲージ）。
    /// シーンには置かず実行時に Canvas 直下へ組む（カードのレイアウトを触らずに済み、
    /// 既定では何も出ないので README のプレビューにも影響しない）。
    /// </summary>
    public sealed class SignageHud : MonoBehaviour
    {
        const float ToastSeconds = 3.5f;
        const float HelpSeconds = 20f;   // 出しっぱなしで放置されないよう自動で引っ込める

        RectTransform _toastBox, _gaugeFill, _helpBox;
        TMP_Text _toastText, _helpText;
        float _toastUntil, _helpUntil;
        bool _holding;

        public bool HelpVisible => _helpBox && _helpBox.gameObject.activeSelf;

        public static SignageHud Create(Canvas canvas, TMP_FontAsset font)
        {
            var go = new GameObject("SignageHud", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(canvas.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.SetAsLastSibling();                       // カードより手前
            var hud = go.AddComponent<SignageHud>();
            hud.Build(font);
            return hud;
        }

        void Build(TMP_FontAsset font)
        {
            // --- トースト（下端中央）---
            _toastBox = Panel("Toast", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 26f),
                new Vector2(420f, 44f), 0.88f);
            _toastText = Label(_toastBox, font, 22f, TextAlignmentOptions.Center);
            _toastText.rectTransform.offsetMin = new Vector2(16f, 4f);
            _toastText.rectTransform.offsetMax = new Vector2(-16f, -4f);

            var fill = new GameObject("HoldGauge", typeof(RectTransform), typeof(Image));
            _gaugeFill = (RectTransform)fill.transform;
            _gaugeFill.SetParent(_toastBox, false);
            _gaugeFill.anchorMin = new Vector2(0f, 0f);
            _gaugeFill.anchorMax = new Vector2(0f, 0f);
            _gaugeFill.pivot = new Vector2(0f, 0f);
            _gaugeFill.anchoredPosition = Vector2.zero;
            _gaugeFill.sizeDelta = new Vector2(0f, 5f);
            fill.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.85f);

            // --- 操作方法パネル（中央）---
            _helpBox = Panel("Help", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(720f, 320f), 0.94f);
            _helpText = Label(_helpBox, font, 20f, TextAlignmentOptions.TopLeft);
            _helpText.rectTransform.offsetMin = new Vector2(24f, 20f);
            _helpText.rectTransform.offsetMax = new Vector2(-24f, -20f);
            _helpText.lineSpacing = 18f;

            _toastBox.gameObject.SetActive(false);
            _helpBox.gameObject.SetActive(false);
        }

        RectTransform Panel(string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, float alpha)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            var image = go.GetComponent<Image>();
            image.color = new Color(0.06f, 0.08f, 0.10f, alpha);
            image.raycastTarget = false;
            return rect;
        }

        static TMP_Text Label(RectTransform parent, TMP_FontAsset font, float size, TextAlignmentOptions alignment)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var text = go.GetComponent<TextMeshProUGUI>();
            if (font) text.font = font;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return text;
        }

        /// <summary>画面下に短く出す通知。</summary>
        public void Toast(string message, float seconds = ToastSeconds)
        {
            _holding = false;
            _gaugeFill.sizeDelta = new Vector2(0f, 5f);
            _toastText.text = message;
            _toastText.ForceMeshUpdate();
            float width = Mathf.Clamp(_toastText.preferredWidth + 48f, 260f, 880f);
            _toastBox.sizeDelta = new Vector2(width, 44f);
            _toastBox.gameObject.SetActive(true);
            _toastUntil = Time.unscaledTime + seconds;
        }

        /// <summary>長押しの進捗（0で消える）。トーストの下端をゲージにする。</summary>
        public void Hold(float progress, string label)
        {
            if (progress <= 0f)
            {
                if (_holding) { _holding = false; _toastUntil = 0f; }
                return;
            }
            if (!_holding) { Toast(label, 0.3f); _holding = true; }
            _toastUntil = Time.unscaledTime + 0.3f;   // 押している間は生かし続ける
            _gaugeFill.sizeDelta = new Vector2(_toastBox.sizeDelta.x * progress, 5f);
        }

        public void ToggleHelp(string text)
        {
            if (HelpVisible) { _helpBox.gameObject.SetActive(false); return; }
            _helpText.text = text;
            _helpBox.gameObject.SetActive(true);
            _helpUntil = Time.unscaledTime + HelpSeconds;
        }

        void Update()
        {
            if (_toastBox.gameObject.activeSelf && Time.unscaledTime > _toastUntil)
            {
                _toastBox.gameObject.SetActive(false);
                _holding = false;
                _gaugeFill.sizeDelta = new Vector2(0f, 5f);
            }
            if (HelpVisible && Time.unscaledTime > _helpUntil) _helpBox.gameObject.SetActive(false);
        }
    }
}
