using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace NTsWallpaperEngine.Signage.EditorTools
{
    /// <summary>
    /// Raspberry Pi 4B（box64経由）向け Linux x64 ビルド。
    /// - box64互換性優先で Mono バックエンド（IL2CPPは使わない: AGENTS.md 7章）
    /// - ビルド前に CreationsDB をサブモジュールから自動同期（サブモジュール駆動）
    /// 出力: Builds/LinuxSignage/（git管理外）→「Raspberry Pi OS開発」プロジェクトへ引き渡し
    /// </summary>
    public static class SignageBuild
    {
        public const string OutputPath = "Builds/LinuxSignage/NTsWallpaperEngine.x86_64";

        [MenuItem("Signage/Build Linux x64 (RPi Signage)")]
        public static void BuildLinux64Menu() => BuildLinux64();

        public static void BuildLinux64()
        {
            // 1. サブモジュール → StreamingAssets 同期（released分のみ）
            if (!SignageDbSync.SyncAll())
            {
                Debug.LogError("[Signage] CreationsDB同期に失敗したためビルド中止。");
                return;
            }

            // 2. シーンが無ければ構築
            if (!File.Exists(SignageSceneBuilder.ScenePath))
                SignageSceneBuilder.BuildScene();

            // 3. プレイヤー設定（サイネージ向け）
            var target = NamedBuildTarget.Standalone;
            PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.Mono2x);
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = false;

            // 4. ビルド
            var options = new BuildPlayerOptions
            {
                scenes = new[] { SignageSceneBuilder.ScenePath },
                target = BuildTarget.StandaloneLinux64,
                targetGroup = BuildTargetGroup.Standalone,
                locationPathName = OutputPath,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == BuildResult.Succeeded)
                Debug.Log($"[Signage] ビルド成功: {OutputPath} ({report.summary.totalSize / (1024 * 1024)} MB)\n" +
                          "docs/raspberrypi-handoff.md に従って「Raspberry Pi OS開発」へ引き渡して。");
            else
                Debug.LogError($"[Signage] ビルド失敗: {report.summary.result}");
        }
    }
}
