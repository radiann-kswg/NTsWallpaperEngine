using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NTsWallpaperEngine.Signage
{
    /// <summary>
    /// 操作用のオーバーレイ（操作方法パネル・通知トースト・長押しゲージ・操作対象の札）。画面（Canvas）ごとに1つ。
    /// シーンには置かず実行時に Canvas 直下へ組む（カードのレイアウトを触らずに済み、
    /// 既定では何も出ないので README のプレビューにも影響しない）。
    /// </summary>
    public sealed class SignageHud : MonoBehaviour
    {
        public const float ToastSeconds = 3.5f;
        const float HelpSeconds = 20f;   // 出しっぱなしで放置されないよう自動で引っ込める
        const float ToastY = 26f;
        const float ToastRise = 8f;      // 通知が出るときに浮き上がる距離

        RectTransform _toastBox, _gaugeFill, _helpBox;
        CanvasGroup _toastGroup, _helpGroup;
        GameObject _chip;
        TMP_Text _toastText, _helpText;
        float _toastUntil, _helpUntil;
        float _toastShown, _helpShown;   // 出入りの進み具合（0=消えている, 1=出ている）
        bool _holding;

        public bool HelpVisible => Time.unscaledTime < _helpUntil;

        /// <param name="chipLabel">操作対象の札に出す画面名（例 "Display 1"）。</param>
        public static SignageHud Create(Canvas canvas, TMP_FontAsset font, string chipLabel)
        {
            var go = new GameObject("SignageHud", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(canvas.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.SetAsLastSibling();                       // カードより手前
            var hud = go.AddComponent<SignageHud>();
            hud.Build(font, chipLabel);
            return hud;
        }

        void Build(TMP_FontAsset font, string chipLabel)
        {
            // --- トースト（下端中央）---
            _toastBox = Panel("Toast", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, ToastY),
                new Vector2(420f, 44f), 0.88f);
            _toastGroup = _toastBox.gameObject.AddComponent<CanvasGroup>();
            _toastGroup.alpha = 0f;
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

            // --- 操作対象の札（上端中央＝時計と型番の間。キャラ画像に被らない。同時押し中だけ出す）---
            var chip = Panel("TargetChip", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f),
                new Vector2(150f, 36f), 0.88f);
            Label(chip, font, 22f, TextAlignmentOptions.Center).text = chipLabel;
            _chip = chip.gameObject;

            // --- 操作方法パネル（中央）---
            _helpBox = Panel("Help", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(720f, 320f), 0.94f);
            _helpGroup = _helpBox.gameObject.AddComponent<CanvasGroup>();
            _helpGroup.alpha = 0f;
            _helpText = Label(_helpBox, font, 20f, TextAlignmentOptions.TopLeft);
            _helpText.rectTransform.offsetMin = new Vector2(24f, 20f);
            _helpText.rectTransform.offsetMax = new Vector2(-24f, -20f);
            _helpText.lineSpacing = 18f;

            _toastBox.gameObject.SetActive(false);
            _chip.SetActive(false);
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

        /// <summary>
        /// 画面下に短く出す通知。表示中に呼ぶと文字だけ入れ替わる（出る動きはやり直さない。
        /// 間隔の連打などでガタつかせないため）。
        /// </summary>
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

        /// <summary>操作対象の札。押した瞬間に対象が分かることが役目なので、動かさずに即出して即消す。</summary>
        public void ShowChip(bool on)
        {
            if (_chip.activeSelf != on) _chip.SetActive(on);
        }

        /// <summary>操作方法パネルを出す（text が null なら引っ込める）。</summary>
        public void ShowHelp(string text)
        {
            if (text == null) { _helpUntil = 0f; return; }
            _helpText.text = text;
            _helpBox.gameObject.SetActive(true);
            _helpText.ForceMeshUpdate();
            _helpBox.sizeDelta = new Vector2(720f, _helpText.preferredHeight + 40f);   // 行数は画面の枚数で変わる
            _helpUntil = Time.unscaledTime + HelpSeconds;
        }

        void Update()
        {
            // 出入りの動きは透明度と小さな移動・拡縮だけ（Pi 4B は 15〜30fps で、0.2 秒でも数コマしか無い）。
            // 値が変わったフレームにだけ書く（毎フレーム書くと Canvas の作り直しが走り続ける）
            bool toastOn = Time.unscaledTime < _toastUntil;
            float toast = Approach(_toastShown, toastOn);
            if (toast != _toastShown)
            {
                _toastShown = _toastGroup.alpha = toast;
                _toastBox.anchoredPosition = new Vector2(0f, ToastY - ToastRise * (1f - toast));
            }
            if (!toastOn && toast <= 0f && _toastBox.gameObject.activeSelf)
            {
                _toastBox.gameObject.SetActive(false);
                _holding = false;
                _gaugeFill.sizeDelta = new Vector2(0f, 5f);
            }

            float help = Approach(_helpShown, HelpVisible);
            if (help != _helpShown)
            {
                _helpShown = _helpGroup.alpha = help;
                _helpBox.localScale = Vector3.one * (0.97f + 0.03f * help);   // 中央基準で少しだけ広がる
            }
            if (!HelpVisible && help <= 0f && _helpBox.gameObject.activeSelf) _helpBox.gameObject.SetActive(false);
        }

        /// <summary>
        /// 出入りの進み具合を目標（出す=1／消す=0）へ寄せる。最初が速く、途中で向きが変わっても値が飛ばない。
        /// 出るときは約 0.2 秒、消えるときは約 0.15 秒で落ち着く（消えるほうを速く）。
        /// </summary>
        static float Approach(float value, bool visible)
        {
            float target = visible ? 1f : 0f;
            value = Mathf.Lerp(target, value, Mathf.Exp(-Time.unscaledDeltaTime * (visible ? 15f : 20f)));
            return Mathf.Abs(target - value) < 0.01f ? target : value;
        }
    }
}
