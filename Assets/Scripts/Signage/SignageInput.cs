using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NTsWallpaperEngine.Signage
{
    /// <summary>
    /// サイネージの入力定義（Input System）。マウス／キーボード／ゲームパッドを同じ操作へ束ねる。
    /// **バインドと操作方法UIの表をこのクラスだけで持つ**（片方を直して説明とズレるのを防ぐため）。
    /// 長押し／短押しの判定もここに置く（Esc・Start・ホイール押し込みが同じ扱い）。
    /// </summary>
    public sealed class SignageInput : IDisposable
    {
        /// <summary>長押し成立までの秒数（UnityConsole のホームボタンと同じ2秒）。</summary>
        public const float HoldSeconds = 2f;
        /// <summary>この秒数を超えて押され続けたら画面にゲージを出す（短押しでチラつかせない）。</summary>
        public const float HoldVisibleAfter = 0.25f;

        /// <summary>自動送り間隔の選択肢（秒）。キーボードの 1〜4 と対応する。</summary>
        public static readonly int[] IntervalChoices = { 20, 30, 60, 120 };

        public enum Press { None, Short, Long }

        public readonly InputAction Next, PagePrev, PageNext, Mode, IntervalCycle, Help, System;
        public readonly InputAction[] IntervalPick = new InputAction[IntervalChoices.Length];

        /// <summary>離上をこの秒数だけ様子見してから確定する（X のキーリピート対策。下の PollSystem 参照）。</summary>
        const float ReleaseGrace = 0.15f;

        readonly List<InputAction> _all = new List<InputAction>();
        float _pressedAt, _releasedAt;
        bool _down, _longFired;

        public SignageInput()
        {
            // 次のカード（再生モードに従う）
            Next = Add("Next", "<Pointer>/press", "<Gamepad>/buttonSouth", "<Keyboard>/space", "<Keyboard>/enter");
            // キャラ番号順のページ送り（マウスは非対応: User 指定）
            PagePrev = Add("PagePrev", "<Keyboard>/leftArrow", "<Keyboard>/upArrow",
                "<Gamepad>/dpad/left", "<Gamepad>/dpad/up", "<Gamepad>/leftStick/left", "<Gamepad>/leftStick/up");
            PageNext = Add("PageNext", "<Keyboard>/rightArrow", "<Keyboard>/downArrow",
                "<Gamepad>/dpad/right", "<Gamepad>/dpad/down", "<Gamepad>/leftStick/right", "<Gamepad>/leftStick/down");
            Mode = Add("Mode", "<Mouse>/rightButton", "<Keyboard>/m", "<Gamepad>/buttonNorth");
            IntervalCycle = Add("IntervalCycle", "<Gamepad>/buttonWest");
            for (int i = 0; i < IntervalPick.Length; i++)
                IntervalPick[i] = Add("Interval" + (i + 1), "<Keyboard>/" + (i + 1));
            Help = Add("Help", "<Keyboard>/h", "<Keyboard>/f1", "<Gamepad>/select");
            // 短押し=創作DB更新 / 長押し=終了（サイネージOSでは電源オフ）
            System = Add("System", "<Keyboard>/escape", "<Gamepad>/start", "<Mouse>/middleButton");
        }

        InputAction Add(string name, params string[] paths)
        {
            var action = new InputAction(name, InputActionType.Button, paths[0]);
            for (int i = 1; i < paths.Length; i++) action.AddBinding(paths[i]);
            _all.Add(action);
            return action;
        }

        public void Enable() { foreach (var a in _all) a.Enable(); }
        public void Disable() { foreach (var a in _all) a.Disable(); }
        public void Dispose() { foreach (var a in _all) a.Dispose(); _all.Clear(); }

        /// <summary>
        /// System（Esc / Start / ホイール押し込み）の短押し・長押しを毎フレーム1回だけ評価する。
        /// 長押しは押しっぱなしの途中で成立し、そのときの離上は None を返す。
        ///
        /// 判定は「押し始めのエッジ」＋「押されている状態」の併用で、離上は ReleaseGrace だけ
        /// 様子見してから確定する。X のキーボード自動リピートは「離上＋押下」の連打として届くため、
        /// 離上のエッジで即確定すると長押しが毎回リセットされて永遠に成立しない（2026-09-21 実機で発生）。
        /// 逆に押下の状態だけを見ると、1フレーム内で押して離した短押しを取りこぼす（同日実機で発生）。
        /// </summary>
        public Press PollSystem(out float holdProgress) =>
            Evaluate(System.WasPressedThisFrame(), System.IsPressed(), Time.unscaledTime, out holdProgress);

        /// <summary>
        /// 判定の本体。押下状態と時刻だけで決まるので、そのまま自己チェックから叩ける
        /// （`Signage/Self Check (入力判定)` メニュー）。
        /// </summary>
        public Press Evaluate(bool wasPressedThisFrame, bool isPressed, float now, out float holdProgress)
        {
            holdProgress = 0f;

            if (wasPressedThisFrame)
            {
                // 押し始め。1フレーム内で押して離した短押し（実機の20fps弱では普通に起きる）もここで拾う
                if (!_down) { _down = true; _pressedAt = now; _longFired = false; }
                _releasedAt = 0f;
            }
            else if (isPressed)
            {
                _releasedAt = 0f;   // 押されっぱなし
            }
            else if (_down)
            {
                if (_releasedAt <= 0f) _releasedAt = now;             // 離した「かもしれない」時刻
                else if (now - _releasedAt >= ReleaseGrace)           // 猶予を過ぎたら本当に離した
                {
                    _down = false;
                    bool wasShort = !_longFired;
                    _longFired = false;
                    return wasShort ? Press.Short : Press.None;
                }
            }

            if (_down && !_longFired)
            {
                float held = now - _pressedAt;
                if (held >= HoldSeconds)
                {
                    _longFired = true;
                    return Press.Long;
                }
                if (held >= HoldVisibleAfter) holdProgress = Mathf.Clamp01(held / HoldSeconds);
            }
            return Press.None;
        }

        // ---- 操作方法UI（表はバインドと同じこのクラスに置く）----

        static readonly (string action, string keyboard, string pad, string mouse)[] Rows =
        {
            ("次のカード",       "Space・Enter", "A",                "左クリック"),
            ("番号順で前／次",   "← ↑ ／ → ↓",  "十字・左スティック", "－"),
            ("再生モード切替",   "M",            "Y",                "右クリック"),
            ("自動送り間隔",     "1〜4",         "X",                "－"),
            ("この表示",         "H・F1",        "Select",           "－"),
            ("創作DB更新（短押し）", "Esc",      "Start",            "ホイール押し"),
        };

        /// <summary>操作方法パネルの本文。最後の行（長押し）だけ機体ごとに文言が変わる。</summary>
        public static string HelpText(string quitLabel, string status)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("<b>操作方法</b>\n\n");
            sb.Append(Row("操作", "キーボード", "ゲームパッド", "マウス")).Append('\n');
            foreach (var r in Rows) sb.Append(Row(r.action, r.keyboard, r.pad, r.mouse)).Append('\n');
            sb.Append(Row(quitLabel + "（長押し）", "Esc", "Start", "ホイール押し")).Append('\n');
            sb.Append('\n').Append("<size=85%>").Append(status).Append("</size>");
            return sb.ToString();
        }

        // 列位置は一番長い語（創作DB更新（短押し）／Space・Enter／十字・左スティック）が次の列に掛からない幅
        static string Row(string a, string k, string g, string m) =>
            $"{a}<pos=30%>{k}<pos=53%>{g}<pos=80%>{m}";
    }
}
