using UnityEditor;
using UnityEngine;

namespace NTsWallpaperEngine.Signage.EditorTools
{
    /// <summary>
    /// 入力判定（短押し／長押し）と、画面ごとのカード選択（SignageController.PickNext）の自己チェック。実機で1往復8分かかる部分なので、
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

            // カード選択（2画面）: 番号順・方向入力は2枚ずつ送って端で回る。ランダムは直前とも隣の画面とも重ならない
            failed += Expect("番号順: 末尾から先頭へ回る", SignageController.PickNext(new[] { 3, 4 }, 5, 1, null), 0, 1);
            failed += Expect("方向入力（前）: 2枚戻る", SignageController.PickNext(new[] { 0, 1 }, 5, -1, null), 3, 4);
            failed += Expect("起動直後（表示なし）は先頭から", SignageController.PickNext(new[] { -1, -1 }, 5, 1, null), 0, 1);
            failed += Expect("1画面の番号順は従来どおり1枚ずつ", SignageController.PickNext(new[] { 4 }, 5, 1, null), 0);
            failed += Expect("ランダム: 1件しか無くても止まらない",
                SignageController.PickNext(new[] { 0, 0 }, 1, 0, new System.Random(0)), 0, 0);
            for (int seed = 0; seed < 200; seed++)
            {
                var three = SignageController.PickNext(new[] { 1, 2 }, 3, 0, new System.Random(seed));
                var two = SignageController.PickNext(new[] { 0, 1 }, 2, 0, new System.Random(seed));
                if (three[0] != 1 && three[1] != 2 && three[0] != three[1] && two[0] == 1 && two[1] == 0) continue;
                Debug.LogError($"[Signage] Self Check NG: ランダム seed={seed} 3件→{three[0]},{three[1]} 2件→{two[0]},{two[1]}");
                failed++;
                break;
            }

            string message = failed == 0 ? "[Signage] Self Check: OK（入力判定 4件・カード選択 6件）"
                                         : $"[Signage] Self Check: NG {failed}件";
            if (failed == 0) Debug.Log(message); else Debug.LogError(message);
        }

        static int Expect(string name, int[] actual, params int[] expect)
        {
            if (System.Linq.Enumerable.SequenceEqual(actual, expect)) return 0;
            Debug.LogError($"[Signage] Self Check NG: {name} / 期待={string.Join(",", expect)} 実際={string.Join(",", actual)}");
            return 1;
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
