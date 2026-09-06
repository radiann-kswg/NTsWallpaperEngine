using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace NTsWallpaperEngine.Signage.EditorTools
{
    /// <summary>
    /// README のプレビュー素材づくり（NTsLotteryEngine の `LotoRecord` / `LotoCapture` と同じ流儀）。
    ///
    /// - `Signage/Play + Record` … Unity Recorder で Game View を 960x540 の MP4 に録りながら Play する。
    ///   出力は `Recordings/&lt;yyyyMMdd-HHmmss&gt;.mp4`（git 管轄外）。Stop で自動的に閉じる。
    ///   **静止画も GIF もこの MP4 から ffmpeg で切り出す**（本サイネージの Canvas は ScreenSpaceOverlay のため
    ///   `Camera.Render()` にも `Unity_Camera_Capture` にも写らない。Game View 直撮りは Game View の解像度に左右される）。
    /// - `Signage/Update README Roster` … 創作DBの表示対象件数を README のマーカー間へ書き戻す（Play 不要）。
    /// </summary>
    [InitializeOnLoad]
    public static class SignageCapture
    {
        const string Key = "NTsWE.RecordOnPlay";
        const string Readme = "README.md";
        const string Begin = "<!-- roster:start -->", End = "<!-- roster:end -->";
        public static int Width = 960, Height = 540, Fps = 30;   // 960x540 = UI の基準解像度・RPi の実行解像度

        static RecorderController controller;
        static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        static SignageCapture() => EditorApplication.playModeStateChanged += OnPlayMode;

        // ---- 録画 ----

        [MenuItem("Signage/Play + Record")]
        public static void PlayAndRecord()
        {
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
            {
                SessionState.SetBool(Key, false);
                StartRecording();
            }
            else if (s == PlayModeStateChange.ExitingPlayMode && controller != null && controller.IsRecording())
            {
                controller.StopRecording();
                controller = null;
            }
        }

        static void StartRecording()
        {
            var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            settings.SetRecordModeToManual();
            settings.FrameRate = Fps;
            // 1/Fps 刻みで進める。**これを false にすると GIF がスローモーションになる**:
            // 実測で Game View は 100fps 超で回るので、1フレーム＝1/30秒として書き出すと映像が実時間の4倍近く伸びる。
            // true なら Time.deltaTime が 1/30 固定になり、切替アニメ（1.4秒）は映像上も 1.4 秒で映る。
            // 代わりにカード切替は壁時計（DateTime.Now）駆動のままなので、**切替の間隔だけは映像の秒数と一致しない**。
            settings.CapFrameRate = true;

            var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movie.name = "SignageMovie";
            movie.Enabled = true;
            movie.EncoderSettings = new CoreEncoderSettings
            {
                Codec = CoreEncoderSettings.OutputCodec.MP4,
                EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.Medium,
            };
            movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = Width, OutputHeight = Height };
            movie.OutputFile = Path.Combine(Repo, "Recordings", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            settings.AddRecorderSettings(movie);

            controller = new RecorderController(settings);
            controller.PrepareRecording();
            if (controller.StartRecording())
                Debug.Log($"[SignageCapture] recording → {movie.OutputFile}.mp4 ({Width}x{Height}@{Fps})");
            else
                Debug.LogError("[SignageCapture] StartRecording failed");
        }

        // ---- README の収録状況表 ----

        static readonly string[] DbKeys = { "DB_Primary", "DB_SemiPrimary", "DB_SelfSecondary" };

        /// <summary>
        /// 表示対象レコードを数え直して README のマーカー間を書き換える。
        /// 表に出すのは**件数だけ**（個体名は未公開個体を含むため README には出さない。AGENTS.md 8章）。
        /// </summary>
        [MenuItem("Signage/Update README Roster")]
        public static void UpdateRoster()
        {
            var records = CreationsDbLoader.LoadAll();
            if (records.Count == 0)
            {
                Debug.LogError("[SignageCapture] 表示対象が0件。サブモジュール取得・同期を確認して（表は書き換えていない）");
                return;
            }

            var rows = new StringBuilder();
            foreach (string key in DbKeys)
            {
                var hit = records.Where(r => r.DbKey == key).ToList();
                rows.AppendLine($"| `db_{key.Substring(3)}.json` | {hit.Count} | {hit.Sum(r => r.ImagePaths.Count)} |");
            }

            var body = new StringBuilder();
            body.AppendLine(Begin);
            body.AppendLine();
            body.AppendLine($"**表示対象 {records.Count} 体 / 画像 {records.Sum(r => r.ImagePaths.Count)} 枚**"
                          + $"（{DateTime.Now:yyyy-MM-dd} 時点。`Signage/Update README Roster` が自動更新）");
            body.AppendLine();
            body.AppendLine("| DB | 表示対象 | corefolder 画像 |");
            body.AppendLine("| --- | --- | --- |");
            body.Append(rows);
            body.AppendLine();
            body.Append(End);

            var path = Path.Combine(Repo, Readme);
            var text = File.ReadAllText(path);
            int a = text.IndexOf(Begin, StringComparison.Ordinal), b = text.IndexOf(End, StringComparison.Ordinal);
            if (a < 0 || b < a)
            {
                Debug.LogWarning($"[SignageCapture] README に {Begin} / {End} が無いので表は書いていない");
                return;
            }
            File.WriteAllText(path, text.Substring(0, a) + body + text.Substring(b + End.Length));
            Debug.Log($"[SignageCapture] README の収録状況表を更新（{records.Count} 体）");
        }
    }
}
