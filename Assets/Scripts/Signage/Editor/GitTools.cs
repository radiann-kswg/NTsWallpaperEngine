using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;

/// git を Windows 側（Unity エディタのプロセス）で実行する。
/// Cowork の Linux サンドボックスから git を書き込むと .git/index.lock / HEAD.lock が unlink できず残るため、
/// add / commit / checkout / submodule などの書き込みは必ずこちらを通す（AGENTS.md 2章）。
///   Unity_RunCommand → GitTools.RunGit("status --short") / GitTools.CommitAll()
/// RouletteSphereChaser / NTsLotteryEngine / NTsMedalGame の GitTools.cs と同一（統合ルート CLAUDE.md「git の書き込み」）。
public static class GitTools
{
    [MenuItem("Tools/Git Commit All")]
    public static void CommitAll()
    {
        string msg = EditorPrefs.GetString("GitTools.Message", "");
        if (string.IsNullOrEmpty(msg)) msg = "WIP: 定期コミット";
        EditorPrefs.DeleteKey("GitTools.Message");
        msg = msg.Replace('"', '\'').TrimEnd('\\'); // 引用符・末尾\で引数が壊れてコミット失敗するのを防ぐ

        UnityEngine.Debug.Log("[GitTools] add: " + RunGit("add -A"));
        UnityEngine.Debug.Log("[GitTools] commit: " + RunGit(
            $"-c user.name=Radian -c user.email=snine9801@gmail.com commit -m \"{msg}\""));
        UnityEngine.Debug.Log("[GitTools] log: " + RunGit("log --oneline -3"));
    }

    /// 任意の git コマンド（RunCommand のコンパイル文脈から Process が使えないため公開）
    public static string RunGit(string args)
    {
        string root = Directory.GetCurrentDirectory();
        SweepStaleLocks(root);
        return Run(root, args);
    }

    /// サンドボックスが残した .git/*.lock（と *.lock.stale）を消す。
    /// ponytail: 60 秒より古いものだけ消す（Windows 側で本当に走っている git を壊さない）。
    static void SweepStaleLocks(string root)
    {
        string git = Path.Combine(root, ".git");
        if (!Directory.Exists(git)) return;
        string stale = Path.Combine(git, "stale-locks"); // サンドボックスが rename で退避した残骸置き場（NTsLotteryEngine で実在）
        var files = Directory.GetFiles(git, "*.lock*", SearchOption.TopDirectoryOnly);
        if (Directory.Exists(stale)) files = files.Concat(Directory.GetFiles(stale)).ToArray();
        foreach (var f in files)
        {
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(f) < TimeSpan.FromSeconds(60)) continue;
            try { File.Delete(f); UnityEngine.Debug.Log("[GitTools] stale lock removed: " + f); }
            catch (Exception e) { UnityEngine.Debug.LogWarning("[GitTools] lock remove failed: " + f + " " + e.Message); }
        }
    }

    static string Run(string cwd, string args)
    {
        var psi = new ProcessStartInfo("git", args)
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        using (var p = Process.Start(psi))
        {
            // stdout/stderr は必ず非同期で両方読む。順次 ReadToEnd() は stderr がパイプバッファを超えた時点でデッドロックする
            var so = p.StandardOutput.ReadToEndAsync();
            var se = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(300000))
            {
                try { p.Kill(); } catch { }
                return "[timeout] git " + args;
            }
            return $"[exit {p.ExitCode}] {so.Result}{se.Result}";
        }
    }
}
