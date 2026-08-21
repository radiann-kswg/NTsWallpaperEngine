using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace NTsWallpaperEngine.Signage.EditorTools
{
    /// <summary>
    /// サイネージ用 TMP FontAsset の生成。
    /// - PenchantManufacture（英数字・型番・時計）: 添付OTF由来の正フォント
    /// - SourceHanSans-Medium（和文）: Dynamicモードで必要グリフを実行時生成
    /// </summary>
    public static class SignageFontAssets
    {
        public const string OutputDir = "Assets/Fonts/TMP";
        public const string PenchantSourcePath = "Assets/Fonts/penchant-manufactuer/PenchantManufacture.otf";
        public const string SourceHanSourcePath = "Assets/Fonts/source-han-sans-release/OTF/Japanese/SourceHanSans-Medium.otf";
        public const string PenchantAssetPath = OutputDir + "/PenchantManufacture SDF.asset";
        public const string SourceHanAssetPath = OutputDir + "/SourceHanSans-Medium SDF.asset";

        /// <summary>
        /// フォント差し替え時の定型: PenchantManufacture SDF の動的アトラスキャッシュを全消去する。
        /// （旧グリフのメトリクス残留防止。バッチモードの -executeMethod からも呼べる）
        /// </summary>
        [MenuItem("Signage/Clear Penchant Dynamic Atlas")]
        public static void ClearPenchantAtlas()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(PenchantAssetPath);
            if (!asset)
            {
                Debug.LogError("[Signage] SDF assetが見つからない: " + PenchantAssetPath);
                return;
            }
            asset.ClearFontAssetData(true);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log("[Signage] 動的アトラスをクリア: " + PenchantAssetPath);
        }

        [MenuItem("Signage/Generate TMP Font Assets")]
        public static void GenerateMenu()
        {
            GenerateAll();
            EditorUtility.DisplayDialog("Signage", "TMP FontAsset の生成が完了。", "OK");
        }

        public static void GenerateAll()
        {
            Directory.CreateDirectory(OutputDir);
            CreateDynamicFontAsset(PenchantSourcePath, PenchantAssetPath, 90);
            CreateDynamicFontAsset(SourceHanSourcePath, SourceHanAssetPath, 72);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public static TMP_FontAsset LoadOrCreate(string assetPath, string sourcePath, int samplingPointSize)
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing) return existing;
            return CreateDynamicFontAsset(sourcePath, assetPath, samplingPointSize);
        }

        static TMP_FontAsset CreateDynamicFontAsset(string sourcePath, string assetPath, int samplingPointSize)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(sourcePath);
            if (!font)
            {
                Debug.LogError($"[Signage] フォントが見つからない: {sourcePath}");
                return null;
            }

            var old = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (old) AssetDatabase.DeleteAsset(assetPath);

            var fontAsset = TMP_FontAsset.CreateFontAsset(
                font, samplingPointSize, 9, GlyphRenderMode.SDFAA, 1024, 1024,
                AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: true);
            if (fontAsset == null)
            {
                Debug.LogError($"[Signage] FontAsset生成に失敗: {sourcePath}");
                return null;
            }

            fontAsset.name = Path.GetFileNameWithoutExtension(assetPath);
            AssetDatabase.CreateAsset(fontAsset, assetPath);

            if (fontAsset.material)
            {
                fontAsset.material.name = fontAsset.name + " Material";
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }
            if (fontAsset.atlasTextures != null && fontAsset.atlasTextures.Length > 0 && fontAsset.atlasTextures[0])
            {
                fontAsset.atlasTextures[0].name = fontAsset.name + " Atlas";
                AssetDatabase.AddObjectToAsset(fontAsset.atlasTextures[0], fontAsset);
            }

            EditorUtility.SetDirty(fontAsset);
            Debug.Log($"[Signage] FontAsset生成: {assetPath}");
            return fontAsset;
        }
    }
}
