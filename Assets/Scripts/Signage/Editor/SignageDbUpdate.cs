using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace NTsWallpaperEngine.Signage.EditorTools
{
    /// <summary>
    /// 創作DBサブモジュール（100BeautiesLab_CreationsDB）の更新をUnity側から取得する。
    /// git fetch/pull（develop追従・sparse-checkout設定は保持）→ StreamingAssets への再同期まで一括実行。
    /// 実行にはPATH上の git が必要（Git for Windows等）。
    /// </summary>
    public static class SignageDbUpdate
    {
        const string SubmoduleDir = "100BeautiesLab_CreationsDB";
        const string Branch = "develop";

        [MenuItem("Signage/Update CreationsDB (git pull + sync)")]
        public static void UpdateMenu()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string submodulePath = Path.Combine(projectRoot, SubmoduleDir);
            if (!Directory.Exists(submodulePath))
            {
                EditorUtility.DisplayDialog("Signage",
                    $"サブモジュールが見つからない: {submodulePath}\nscripts/setup-submodule を実行して。", "OK");
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("Signage", "CreationsDB を取得中 (git fetch)...", 0.2f);

                var (fetchCode, _, fetchErr) = RunGit($"fetch origin {Branch}", submodulePath);
                if (fetchCode != 0)
                {
                    Fail($"git fetch に失敗:\n{fetchErr}");
                    return;
                }

                string before = RunGit("rev-parse --short HEAD", submodulePath).stdout.Trim();

                EditorUtility.DisplayProgressBar("Signage", "CreationsDB を更新中 (git pull)...", 0.5f);
                var (pullCode, pullOut, pullErr) = RunGit($"pull origin {Branch}", submodulePath);
                if (pullCode != 0)
                {
                    Fail($"git pull に失敗:\n{pullErr}\n（サブモジュール内のローカル変更が原因の場合は手動で解消して）");
                    return;
                }

                string after = RunGit("rev-parse --short HEAD", submodulePath).stdout.Trim();
                bool updated = before != after;
                Debug.Log($"[Signage] CreationsDB {(updated ? $"更新: {before} → {after}" : $"最新のまま ({before})")}\n{pullOut.Trim()}");

                EditorUtility.DisplayProgressBar("Signage", "StreamingAssets へ同期中...", 0.8f);
                bool synced = SignageDbSync.SyncAll();

                EditorUtility.ClearProgressBar();
                string message = updated
                    ? $"CreationsDB を更新した: {before} → {after}\n"
                    : $"CreationsDB は最新だった ({before})。\n";
                message += synced ? "StreamingAssets への同期も完了。" : "※同期に失敗（Consoleを確認）";
                if (updated)
                    message += "\n\n注意: 本リポジトリのサブモジュール参照が進んだので、" +
                               "区切りの良いところで親リポジトリ側の変更（gitlink）をコミットして。";
                EditorUtility.DisplayDialog("Signage", message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        static void Fail(string message)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError($"[Signage] {message}");
            EditorUtility.DisplayDialog("Signage", message, "OK");
        }

        internal static (int code, string stdout, string stderr) RunGit(string arguments, string workingDirectory)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            using var process = Process.Start(psi);
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(120000);
            return (process.ExitCode, stdout, stderr);
        }
    }
}
