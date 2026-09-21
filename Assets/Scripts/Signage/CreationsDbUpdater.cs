using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace NTsWallpaperEngine.Signage
{
    /// <summary>
    /// 実行中に創作DBを取り直す（Esc / Start / ホイール押しの短押し）。
    /// - 実機（Linuxプレイヤー）: 実行ファイルの隣の <c>update-creationsdb.sh</c> をそのまま叩く。
    ///   OS の日次タイマーと同じスクリプトなので、sparse 設定やブランチの正本は1か所のままになる。
    ///   取得先は NTsWallpaper OS なら /opt/ntswallpaper/creationsdb（環境変数）、
    ///   UnityConsole など指定の無い機体では persistentDataPath/creationsdb（アプリが書ける場所）。
    /// - エディタ: サブモジュール <c>100BeautiesLab_CreationsDB</c> を git pull する（サブモジュール駆動のまま）。
    /// 時間がかかる（初回クローンは数分）ので呼び出し側はワーカースレッドで回すこと。
    /// </summary>
    public static class CreationsDbUpdater
    {
        public struct Result
        {
            public bool Ok;
            public bool Changed;
            public string DataRoot;   // 成功時: 更新後のデータルート（Works_NumberTales）
            public string Message;    // 画面に出す1行
        }

        /// <summary>update-creationsdb.sh と同じ変数名（取得先ディレクトリ）。</summary>
        public const string RepoDirEnvVar = "CREATIONSDB_REPO_DIR";
        const string ScriptName = "update-creationsdb.sh";
        const string SubmoduleDir = "100BeautiesLab_CreationsDB";
        const string Branch = "develop";
        const int TimeoutMs = 900000;   // 初回の sparse clone は実機だと数分かかる

        // Application.* はメインスレッドからしか読めないので、起動時に退避しておく（更新はワーカーで走る）
        static string _appDir;          // プレイヤー: 実行ファイルのある場所 / エディタ: プロジェクトルート
        static string _persistentDir;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void CacheAppPaths()
        {
            _appDir = Path.GetDirectoryName(Application.dataPath);
            _persistentDir = Application.persistentDataPath;
        }

        /// <summary>取得先リポジトリ。環境変数優先（NTsWallpaper OS）→ 無ければアプリの保存領域（UnityConsole 等）。</summary>
        public static string RepoDir()
        {
            string repo = Environment.GetEnvironmentVariable(RepoDirEnvVar);
            if (!string.IsNullOrEmpty(repo)) return repo;

            // run-signage.sh は <repo>/data/Works_NumberTales を NTSWE_CREATIONSDB に入れている
            string external = Environment.GetEnvironmentVariable(CreationsDbLoader.ExternalRootEnvVar);
            if (!string.IsNullOrEmpty(external))
                return Path.GetFullPath(Path.Combine(external, "..", ".."));

            return Path.Combine(_persistentDir ?? Application.persistentDataPath, "creationsdb");
        }

        public static string DataRootOf(string repoDir) => Path.Combine(repoDir, "data", "Works_NumberTales");

        public static Result Run()
        {
            try
            {
#if UNITY_EDITOR
                string repo = Path.Combine(_appDir, SubmoduleDir);
                if (!Directory.Exists(repo))
                    return Fail($"サブモジュールが無い: {SubmoduleDir}");
                string before = Head(repo);
                var (code, _, err) = Exec("git", $"pull --ff-only origin {Branch}", repo, null);
                if (code != 0) return Fail(LastLine(err));
#else
                string repo = RepoDir();
                string script = Path.Combine(_appDir, ScriptName);
                if (!File.Exists(script))
                    return Fail($"{ScriptName} が無い");
                string before = Head(repo);
                var (code, output) = RunScript(script, repo);
                if (code != 0) return Fail(LastLine(output));
#endif
                string after = Head(repo);
                string root = DataRootOf(repo);
                if (!Directory.Exists(Path.Combine(root, "DataBases")))
                    return Fail("取得したデータに DataBases が無い");

                bool changed = !string.IsNullOrEmpty(after) && after != before;
                Debug.Log($"[Signage] 創作DB更新: {before} → {after} ({root})");
                return new Result
                {
                    Ok = true,
                    Changed = changed,
                    DataRoot = root,
                    Message = changed ? $"創作DBを更新: {before} → {after}" : $"創作DBは最新 {after}",
                };
            }
            catch (Exception e)
            {
                return Fail(e.Message);
            }
        }

        static Result Fail(string reason)
        {
            reason = string.IsNullOrEmpty(reason) ? "原因不明" : reason.Trim();
            if (reason.Length > 90) reason = reason.Substring(0, 90) + "…";
            Debug.LogWarning($"[Signage] 創作DB更新に失敗: {reason}");
            return new Result { Ok = false, Message = "創作DB更新に失敗: " + reason };
        }

        /// <summary>
        /// HEAD の短縮ハッシュ。git を起動せず .git を直接読む
        /// （box64 上では子プロセスの回収が効かないので、プロセスは最小限にする）。
        /// サブモジュールの .git はファイル（gitdir: …）なのでそれも辿る。
        /// </summary>
        static string Head(string repo)
        {
            try
            {
                string gitDir = Path.Combine(repo, ".git");
                if (File.Exists(gitDir))
                {
                    string line = File.ReadAllText(gitDir).Trim();
                    if (!line.StartsWith("gitdir:", StringComparison.Ordinal)) return "";
                    string rel = line.Substring("gitdir:".Length).Trim();
                    gitDir = Path.IsPathRooted(rel) ? rel : Path.GetFullPath(Path.Combine(repo, rel));
                }
                if (!Directory.Exists(gitDir)) return "";

                string head = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();
                if (!head.StartsWith("ref:", StringComparison.Ordinal)) return Short(head);

                string refName = head.Substring(4).Trim();
                string refFile = Path.Combine(gitDir, refName.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(refFile)) return Short(File.ReadAllText(refFile).Trim());

                string packed = Path.Combine(gitDir, "packed-refs");
                if (File.Exists(packed))
                    foreach (string line in File.ReadAllLines(packed))
                        if (line.EndsWith(" " + refName, StringComparison.Ordinal))
                            return Short(line.Split(' ')[0]);
                return "";
            }
            catch
            {
                return "";
            }
        }

        static string Short(string sha) => sha.Length >= 7 ? sha.Substring(0, 7) : sha;

        /// <summary>
        /// 取得スクリプトを実行して終了コードを得る。**WaitForExit は使わない**:
        /// box64 上の Mono は子プロセスを回収できず（SIGCHLD が届かない）、子が &lt;defunct&gt; になっても
        /// WaitForExit が返ってこない（2026-09-21 実機で確認）。終了はマーカーファイルで見る。
        /// ゾンビが1つ残るが実害は無い（プロセステーブルの1エントリ）。
        /// </summary>
        static (int code, string output) RunScript(string script, string repo)
        {
            string stem = Path.Combine(Path.GetTempPath(), "ntswe-dbupdate");
            string log = stem + ".log", marker = stem + ".done";
            try { File.Delete(marker); } catch { /* 無ければそれでよい */ }

            Process.Start(new ProcessStartInfo("/bin/bash",
                $"-c \"{RepoDirEnvVar}='{repo}' '{script}' >'{log}' 2>&1; echo $? >'{marker}'\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            var until = DateTime.UtcNow.AddMilliseconds(TimeoutMs);
            while (DateTime.UtcNow < until)
            {
                Thread.Sleep(500);
                if (!File.Exists(marker)) continue;
                string text = ReadAll(marker).Trim();
                if (text.Length == 0) continue;        // echo の書き込み途中
                return (int.TryParse(text, out int code) ? code : -1, ReadAll(log));
            }
            return (-1, "時間切れ");
        }

        static string ReadAll(string path)
        {
            try { return File.ReadAllText(path); }
            catch { return ""; }
        }

        /// <summary>
        /// エディタ（Windows）専用のプロセス実行。stdout/stderr は必ず非同期で両方読む
        /// （順次 ReadToEnd は clone の進捗でデッドロックする）。実機側は RunScript を使うこと。
        /// </summary>
        static (int code, string stdout, string stderr) Exec(
            string file, string args, string workingDirectory, (string key, string value)? env)
        {
            var psi = new ProcessStartInfo(file, args)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            if (env.HasValue) psi.EnvironmentVariables[env.Value.key] = env.Value.value;

            using var process = Process.Start(psi);
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeoutMs))
            {
                try { process.Kill(); } catch { /* 既に終わっていれば無視 */ }
                return (-1, "", "時間切れ");
            }
            return (process.ExitCode, stdout.Result, stderr.Result);
        }

        static string LastLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            var lines = text.Replace("\r", "").Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
                if (!string.IsNullOrWhiteSpace(lines[i])) return lines[i];
            return "";
        }
    }
}
