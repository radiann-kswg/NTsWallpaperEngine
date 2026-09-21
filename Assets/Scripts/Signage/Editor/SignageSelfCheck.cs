using UnityEditor;
using UnityEngine;

namespace NTsWallpaperEngine.Signage.EditorTools
{
    /// <summary>
    /// 入力判定（短押し／長押し）の自己チェック。実機で1往復8分かかる部分なので、
    /// 変更したらまずこれを通すこと。メニュー `Signage/Self Check (入力判定)`。
    /// </summary>
    public static class SignageSelfCheck
    {
        [MenuItem("Signage/Self Check (入力判定)")]
        public static void Run()
        {
            int failed = 0;
            failed += Case("1フレーム内で押して離した短押し", new[]
            {
                // (押し始め, 押されている, 時刻, 期待)
                (true, false, 0.00f, SignageInput.Press.None),
                (false, false, 0.05f, SignageInput.Press.None),   // 離上は様子見
                (false, false, 0.30f, SignageInput.Press.Short),
            });
            failed += Case("ふつうの短押し", new[]
            {
                (true, true, 0.00f, SignageInput.Press.None),
                (false, true, 0.10f, SignageInput.Press.None),
                (false, false, 0.15f, SignageInput.Press.None),
                (false, false, 0.35f, SignageInput.Press.Short),
            });
            failed += Case("長押し（離上は Short にしない）", new[]
            {
                (true, true, 0.00f, SignageInput.Press.None),
                (false, true, 1.00f, SignageInput.Press.None),
                (false, true, 2.01f, SignageInput.Press.Long),
                (false, false, 2.10f, SignageInput.Press.None),
                (false, false, 2.40f, SignageInput.Press.None),
            });
            failed += Case("キーリピート（離上＋押下の連打）でも長押しが成立する", new[]
            {
                (true, true, 0.00f, SignageInput.Press.None),
                (false, false, 0.66f, SignageInput.Press.None),   // X の1回目のリピートで一瞬離れる
                (true, true, 0.70f, SignageInput.Press.None),
                (false, false, 1.30f, SignageInput.Press.None),
                (true, true, 1.34f, SignageInput.Press.None),
                (false, true, 2.05f, SignageInput.Press.Long),    // 押し始めからの通算で成立
            });

            string message = failed == 0 ? "[Signage] Self Check: OK（入力判定 4件）"
                                         : $"[Signage] Self Check: NG {failed}件";
            if (failed == 0) Debug.Log(message); else Debug.LogError(message);
        }

        static int Case(string name, (bool pressedEdge, bool isPressed, float now, SignageInput.Press expect)[] steps)
        {
            var input = new SignageInput();
            try
            {
                for (int i = 0; i < steps.Length; i++)
                {
                    var actual = input.Evaluate(steps[i].pressedEdge, steps[i].isPressed, steps[i].now, out float hold);
                    if (actual != steps[i].expect)
                    {
                        Debug.LogError($"[Signage] Self Check NG: {name} / {i}番目 t={steps[i].now}s " +
                                       $"期待={steps[i].expect} 実際={actual}");
                        return 1;
                    }
                }
                return 0;
            }
            finally
            {
                input.Dispose();
            }
        }
    }
}
