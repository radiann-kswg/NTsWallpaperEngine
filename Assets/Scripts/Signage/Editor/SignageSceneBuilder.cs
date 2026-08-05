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
    /// レイアウトは emstk-rv コアフォルダカードを 1920x1080 サイネージ向けに再構成したもの。
    /// </summary>
    public static class SignageSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/SignageScene.unity";
        const float RefW = 1920f, RefH = 1080f;

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

            // --- Background (3層 + グロー) ---
            var bg = CreateStretched<Image>(canvasGo.transform, "Background");
            bg.color = new Color(0.86f, 0.94f, 0.96f);
            var gradient = CreateStretched<RawImage>(canvasGo.transform, "GradientOverlay");
            gradient.raycastTarget = false;
            var dots = CreateStretched<RawImage>(canvasGo.transform, "DotsOverlay");
            dots.color = Color.white;
            dots.raycastTarget = false;

            var glowGo = new GameObject("CharacterGlow", typeof(RectTransform), typeof(RawImage));
            var glowRect = (RectTransform)glowGo.transform;
            glowRect.SetParent(canvasGo.transform, false);
            glowRect.anchorMin = glowRect.anchorMax = new Vector2(0.5f, 0.5f);
            glowRect.anchoredPosition = new Vector2(0f, -30f);
            glowRect.sizeDelta = new Vector2(1500f, 1500f);
            var glow = glowGo.GetComponent<RawImage>();
            glow.raycastTarget = false;

            // --- Character image (中央やや下) ---
            var charGo = new GameObject("CharacterImage", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
            var charRect = (RectTransform)charGo.transform;
            charRect.SetParent(canvasGo.transform, false);
            charRect.anchorMin = charRect.anchorMax = new Vector2(0.5f, 0.5f);
            charRect.anchoredPosition = new Vector2(0f, -30f);
            charRect.sizeDelta = new Vector2(1150f, 820f);
            var charImage = charGo.GetComponent<RawImage>();
            charImage.raycastTarget = false;
            var fitter = charGo.GetComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 945f / 676f;

            // --- Header (右上) ---
            var modelNumber = CreateText(canvasGo.transform, "ModelNumberText", penchant, 64f,
                TextAlignmentOptions.TopRight, new Vector2(1f, 1f), new Vector2(-48f, -36f), new Vector2(1400f, 80f));
            var formalName = CreateText(canvasGo.transform, "FormalNameText", penchant, 58f,
                TextAlignmentOptions.TopRight, new Vector2(1f, 1f), new Vector2(-48f, -112f), new Vector2(1400f, 76f));
            var nameEn = CreateText(canvasGo.transform, "NameEnText", penchant, 118f,
                TextAlignmentOptions.TopRight, new Vector2(1f, 1f), new Vector2(-48f, -196f), new Vector2(1600f, 140f));

            // --- Footer (左下) ---
            var nameJp = CreateText(canvasGo.transform, "NameJpText", sourceHan, 104f,
                TextAlignmentOptions.BottomLeft, new Vector2(0f, 0f), new Vector2(48f, 150f), new Vector2(1500f, 130f));
            var modelNameJp = CreateText(canvasGo.transform, "ModelNameJpText", sourceHan, 54f,
                TextAlignmentOptions.BottomLeft, new Vector2(0f, 0f), new Vector2(48f, 48f), new Vector2(1824f, 80f));

            // --- Clock (左上) ---
            var clock = CreateText(canvasGo.transform, "ClockText", penchant, 84f,
                TextAlignmentOptions.TopLeft, new Vector2(0f, 1f), new Vector2(48f, -36f), new Vector2(500f, 100f));
            clock.color = new Color(50f / 255f, 50f / 255f, 50f / 255f, 0.85f);

            // --- Controller / View ---
            var signageGo = new GameObject("Signage", typeof(SignageCardView), typeof(SignageController));
            var view = signageGo.GetComponent<SignageCardView>();
            view.background = bg;
            view.gradientOverlay = gradient;
            view.dotsOverlay = dots;
            view.characterGlow = glow;
            view.characterImage = charImage;
            view.characterAspect = fitter;
            view.modelNumberText = modelNumber;
            view.formalNameText = formalName;
            view.nameEnText = nameEn;
            view.nameJpText = nameJp;
            view.modelNameJpText = modelNameJp;
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
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.text = name;
            return text;
        }
    }
}
