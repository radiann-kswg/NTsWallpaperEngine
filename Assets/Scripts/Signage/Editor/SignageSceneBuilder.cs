using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NTsWallpaperEngine.Signage.EditorTools
{
    /// <summary>
    /// サイネージシーン（SignageScene.unity）を自動構築する。
    /// UIは創作DBから自動形成されるため、シーンには「枠」だけを作り、値はランタイムが流し込む。
    /// レイアウト（英文表示のみ）:
    ///   左       = 大型番号（旧シーン踏襲のカウント演出・透かし風）
    ///   中央右   = corefolder画像（グロー付き）
    ///   右上     = 型番 / NUMBERTALES #番号 / 英名
    ///   右下     = プロフィール（Gender / Class / Height / Concept Age — 旧シーン準拠）
    ///   左下     = 英語機体名
    ///   左上     = 時計
    /// </summary>
    public static class SignageSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/SignageScene.unity";
        const float RefW = 960f, RefH = 540f;

        [MenuItem("Signage/Build Signage Scene")]
        public static void BuildSceneMenu()
        {
            BuildScene();
            EditorUtility.DisplayDialog("Signage", $"シーンを構築した: {ScenePath}", "OK");
        }

        public static void BuildScene()
        {
            var penchant = SignageFontAssets.LoadOrCreate(
                SignageFontAssets.PenchantAssetPath, SignageFontAssets.PenchantSourcePath, 90);
            var sourceHan = SignageFontAssets.LoadOrCreate(
                SignageFontAssets.SourceHanAssetPath, SignageFontAssets.SourceHanSourcePath, 72);

            // Class等にDB表記（和文）が混ざっても豆腐にならないよう、和文フォントをフォールバックに登録
            if (penchant && sourceHan)
            {
                penchant.fallbackFontAssetTable ??= new List<TMP_FontAsset>();
                if (!penchant.fallbackFontAssetTable.Contains(sourceHan))
                {
                    penchant.fallbackFontAssetTable.Add(sourceHan);
                    EditorUtility.SetDirty(penchant);
                }
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // --- Camera ---
            var camGo = new GameObject("Main Camera");
            var cam = camGo.AddComponent<Camera>();
            camGo.tag = "MainCamera";
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.orthographic = true;
            camGo.AddComponent<AudioListener>();

            // --- Canvas ---
            var canvasGo = new GameObject("SignageCanvas", typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.matchWidthOrHeight = 0.5f;

            // --- Background (3層) ---
            var bg = CreateStretched<Image>(canvasGo.transform, "Background");
            bg.color = new Color(0.86f, 0.94f, 0.96f);
            var gradient = CreateStretched<RawImage>(canvasGo.transform, "GradientOverlay");
            gradient.raycastTarget = false;
            var dots = CreateStretched<RawImage>(canvasGo.transform, "DotsOverlay");
            dots.color = Color.white;
            dots.raycastTarget = false;

            // --- Character glow + image (中央やや左・一回り小さめ) ---
            var glowGo = new GameObject("CharacterGlow", typeof(RectTransform), typeof(RawImage));
            var glowRect = (RectTransform)glowGo.transform;
            glowRect.SetParent(canvasGo.transform, false);
            glowRect.anchorMin = glowRect.anchorMax = new Vector2(0.46f, 0.50f);
            glowRect.anchoredPosition = Vector2.zero;
            glowRect.sizeDelta = new Vector2(520f, 520f);
            var glow = glowGo.GetComponent<RawImage>();
            glow.raycastTarget = false;

            // サイズはランタイムが「ネイティブ解像度 × characterScale」で設定する（キャラ間サイズ校正の保持）
            var charGo = new GameObject("CharacterImage", typeof(RectTransform), typeof(RawImage));
            var charRect = (RectTransform)charGo.transform;
            charRect.SetParent(canvasGo.transform, false);
            charRect.anchorMin = charRect.anchorMax = new Vector2(0.46f, 0.50f);
            charRect.anchoredPosition = Vector2.zero;
            charRect.sizeDelta = new Vector2(280f, 205f);
            var charImage = charGo.GetComponent<RawImage>();
            charImage.raycastTarget = false;

            // --- Header (右上・詰めた縦積み: 型番 → 正式名称(全文・最大2行) → 英名) ---
            var modelNumber = CreateText(canvasGo.transform, "ModelNumberText", penchant, 32f,
                TextAlignmentOptions.TopRight, new Vector2(1f, 1f), new Vector2(-24f, -18f), new Vector2(700f, 40f));
            var formalName = CreateText(canvasGo.transform, "FormalNameText", penchant, 29f,
                TextAlignmentOptions.TopRight, new Vector2(1f, 1f), new Vector2(-24f, -60f), new Vector2(475f, 76f));
            formalName.textWrappingMode = TextWrappingModes.Normal; // 長い正式名称は2行に折り返す
            var nameEn = CreateText(canvasGo.transform, "NameEnText", penchant, 59f,
                TextAlignmentOptions.TopRight, new Vector2(1f, 1f), new Vector2(-24f, -143f), new Vector2(800f, 145f));

            // --- Big number (画面下端に密着・1.5倍・透かし風カウント演出) ---
            var bigNumber = CreateText(canvasGo.transform, "BigNumberText", penchant, 225f,
                TextAlignmentOptions.BottomLeft, new Vector2(0f, 0f), new Vector2(16f, -22f), new Vector2(650f, 245f));

            // --- Profile (右下・下から: 機体名EN → Gender/Concept Age → 区切り線 → Class複数行) ---
            var classBlock = CreateText(canvasGo.transform, "ClassText", penchant, 23f,
                TextAlignmentOptions.BottomRight, new Vector2(1f, 0f), new Vector2(-24f, 144f), new Vector2(700f, 160f));
            classBlock.lineSpacing = 12f;

            var dividerGo = new GameObject("ClassDividerLine", typeof(RectTransform), typeof(Image));
            var dividerRect = (RectTransform)dividerGo.transform;
            dividerRect.SetParent(canvasGo.transform, false);
            dividerRect.anchorMin = dividerRect.anchorMax = new Vector2(1f, 0f);
            dividerRect.pivot = new Vector2(1f, 0f);
            dividerRect.anchoredPosition = new Vector2(-24f, 136f);
            dividerRect.sizeDelta = new Vector2(300f, 2f);
            var divider = dividerGo.GetComponent<Image>();
            divider.raycastTarget = false;

            var profile = CreateText(canvasGo.transform, "ProfileText", penchant, 23f,
                TextAlignmentOptions.BottomRight, new Vector2(1f, 0f), new Vector2(-24f, 64f), new Vector2(700f, 66f));
            profile.lineSpacing = 12f;
            var modelNameEn = CreateText(canvasGo.transform, "ModelNameEnText", penchant, 30f,
                TextAlignmentOptions.BottomRight, new Vector2(1f, 0f), new Vector2(-24f, 24f), new Vector2(800f, 40f));

            // --- Clock (左上: HH:MM + 小さい秒 / 下段に日付) ---
            var clock = CreateText(canvasGo.transform, "ClockText", penchant, 42f,
                TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), new Vector2(24f, -18f), new Vector2(340f, 90f));
            clock.color = new Color(50f / 255f, 50f / 255f, 50f / 255f, 0.85f);

            // --- Controller / View ---
            var signageGo = new GameObject("Signage", typeof(SignageCardView), typeof(SignageController));
            var view = signageGo.GetComponent<SignageCardView>();
            view.background = bg;
            view.gradientOverlay = gradient;
            view.dotsOverlay = dots;
            view.characterGlow = glow;
            view.characterImage = charImage;
            view.characterScale = 0.92f; // 960x540基準・中央寄せ調整版
            view.bigNumberText = bigNumber;
            view.modelNumberText = modelNumber;
            view.formalNameText = formalName;
            view.nameEnText = nameEn;
            view.classText = classBlock;
            view.classDividerLine = divider;
            view.profileText = profile;
            view.modelNameEnText = modelNameEn;
            view.clockText = clock;

            var controller = signageGo.GetComponent<SignageController>();
            var so = new SerializedObject(controller);
            so.FindProperty("view").objectReferenceValue = view;
            so.ApplyModifiedPropertiesWithoutUndo();

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);

            // EditorBuildSettings をサイネージシーンのみに
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log($"[Signage] シーン構築完了: {ScenePath}");
        }

        static T CreateStretched<T>(Transform parent, string name) where T : Component
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(T));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return go.GetComponent<T>();
        }

        static TMP_Text CreateText(Transform parent, string name, TMP_FontAsset font, float size,
            TextAlignmentOptions alignment, Vector2 anchor, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = sizeDelta;

            var text = go.GetComponent<TextMeshProUGUI>();
            if (font) text.font = font;
            text.fontSize = size;
            // 長い名前・型番でも枠内へ収まるよう自動縮小（下限は45%）
            text.enableAutoSizing = true;
            text.fontSizeMax = size;
            text.fontSizeMin = size * 0.45f;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.text = name;
            return text;
        }
    }
}
