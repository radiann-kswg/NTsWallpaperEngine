using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace NTsWallpaperEngine.Signage
{
    /// <summary>
    /// 創作DB（100BeautiesLab_CreationsDB サブモジュール）のナンバーテールズ3DBを読み込むローダ。
    /// データはサブモジュール駆動：
    ///   1. ビルド/同期後: StreamingAssets/CreationsDB/
    ///   2. エディタでの未同期時フォールバック: リポジトリ直下のサブモジュール
    /// 表示対象は Progress == "released" かつ corefolder 画像を持つレコードのみ（AGENTS.md 5章）。
    /// </summary>
    public sealed class NtCharacterRecord
    {
        public string DbKey;            // "DB_Primary" | "DB_SemiPrimary" | "DB_SelfSecondary"
        public string NumRaw;           // "44", "101-mp" など
        public int NumValue;            // 数字アニメーション用の数値（数字部分の抽出）
        public string NumBadge;         // バッジ表記
        public string NameJP;           // 例 "44(シトシ)"
        public string NameEN;           // 例 "44(Folfourn)"
        public string FormalNameJP;
        public string FormalNameEN;     // 例 "NumberTales #44"
        public string ModelNumber;      // 例 "APHR-NT IV+[R]IV"
        public string ModelNameJP;      // 例 "ナンバーテールズ 正規+改良型4号機(44番機)"
        public string ModelNameEN;
        public List<string> ImagePaths = new List<string>();  // corefolder画像の絶対パス
        public Color ThemeColor = new Color(0.72f, 0.85f, 0.90f);
        public bool HasThemeColor;
    }

    public static class CreationsDbLoader
    {
        static readonly (string dbKey, string jsonName)[] Sources =
        {
            ("DB_Primary", "db_Primary.json"),
            ("DB_SemiPrimary", "db_SemiPrimary.json"),
            ("DB_SelfSecondary", "db_SelfSecondary.json"),
        };

        public const string SubmoduleRelativeRoot = "100BeautiesLab_CreationsDB/data/Works_NumberTales";

        /// <summary>データルート（DataBases/ と Images/ を含むフォルダ）を解決する。</summary>
        public static string ResolveDataRoot()
        {
            string streaming = Path.Combine(Application.streamingAssetsPath, "CreationsDB");
            if (Directory.Exists(Path.Combine(streaming, "DataBases")))
                return streaming;

            // エディタ/開発時フォールバック: サブモジュール直読み（サブモジュール駆動）
            string submodule = Path.GetFullPath(Path.Combine(Application.dataPath, "..", SubmoduleRelativeRoot));
            if (Directory.Exists(Path.Combine(submodule, "DataBases")))
                return submodule;

            return null;
        }

        public static List<NtCharacterRecord> LoadAll()
        {
            var results = new List<NtCharacterRecord>();
            string root = ResolveDataRoot();
            if (root == null)
            {
                Debug.LogError("[Signage] CreationsDB が見つからない。scripts/setup-submodule を実行するか、メニュー Signage/Sync CreationsDB で同期して。");
                return results;
            }

            foreach (var (dbKey, jsonName) in Sources)
            {
                string jsonPath = Path.Combine(root, "DataBases", jsonName);
                if (!File.Exists(jsonPath))
                {
                    Debug.LogWarning($"[Signage] DBファイルが無い: {jsonPath}");
                    continue;
                }

                JArray array;
                try
                {
                    array = JArray.Parse(File.ReadAllText(jsonPath));
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Signage] {jsonName} のパースに失敗: {e.Message}");
                    continue;
                }

                foreach (var token in array)
                {
                    if (token is not JObject obj) continue;
                    var record = ParseRecord(obj, dbKey, root);
                    if (record != null) results.Add(record);
                }
            }

            Debug.Log($"[Signage] 表示対象レコード: {results.Count}件 (root: {root})");
            return results;
        }

        static NtCharacterRecord ParseRecord(JObject obj, string dbKey, string root)
        {
            // released のみ（未公開情報をサイネージへ出さない）
            if (!string.Equals((string)obj["Progress"], "released", StringComparison.OrdinalIgnoreCase))
                return null;

            var images = obj["Images"] as JObject;
            var corefolder = images?["corefolder_PNGPath"] as JArray;
            if (corefolder == null || corefolder.Count == 0) return null;

            var record = new NtCharacterRecord
            {
                DbKey = dbKey,
                NumRaw = TokenToString(obj["Num"]),
                NumBadge = TokenToString(obj["Num_Badge"]),
                NameJP = (string)obj["Name_JP"],
                NameEN = (string)obj["Name_EN"],
                FormalNameJP = (string)obj["FormalName_JP"],
                FormalNameEN = (string)obj["FormalName_EN"],
                ModelNumber = (string)obj["ModelNumber"],
                ModelNameJP = (string)obj["ModelName_JP"],
                ModelNameEN = (string)obj["ModelName_EN"],
            };
            record.NumValue = ExtractLeadingNumber(record.NumRaw);

            foreach (var rel in corefolder)
            {
                string relPath = (string)rel;
                if (string.IsNullOrEmpty(relPath)) continue;
                string abs = Path.Combine(root, "Images", dbKey, "corefolder",
                    relPath.Replace('/', Path.DirectorySeparatorChar) + ".png");
                if (File.Exists(abs)) record.ImagePaths.Add(abs);
            }
            if (record.ImagePaths.Count == 0) return null;

            ApplyThemeColor(record, obj["ColorPalette"] as JArray);
            return record;
        }

        static void ApplyThemeColor(NtCharacterRecord record, JArray palette)
        {
            if (palette == null) return;
            string hex = null;
            // Primary → Sub → 先頭 の優先順でテーマ色を拾う
            foreach (var role in new[] { "#ColorRole_Primary", "#ColorRole_Sub" })
            {
                foreach (var t in palette)
                {
                    if (t is JObject o && (string)o["Role"] == role && !string.IsNullOrEmpty((string)o["Hex"]))
                    {
                        hex = (string)o["Hex"];
                        break;
                    }
                }
                if (hex != null) break;
            }
            if (hex == null && palette.Count > 0 && palette[0] is JObject first)
                hex = (string)first["Hex"];

            if (hex != null && ColorUtility.TryParseHtmlString(hex, out var c))
            {
                record.ThemeColor = c;
                record.HasThemeColor = true;
            }
        }

        static string TokenToString(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return "";
            return token.ToString();
        }

        static int ExtractLeadingNumber(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return 0;
            var m = Regex.Match(raw, @"\d+");
            return m.Success && int.TryParse(m.Value, out int v) ? v : 0;
        }

        /// <summary>corefolder画像をTexture2Dとして読み込む（Linux/Windowsスタンドアロン想定）。</summary>
        public static Texture2D LoadTexture(string absolutePath)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(absolutePath);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(bytes, markNonReadable: true))
                {
                    tex.name = Path.GetFileNameWithoutExtension(absolutePath);
                    return tex;
                }
                UnityEngine.Object.Destroy(tex);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Signage] 画像読込に失敗: {absolutePath} ({e.Message})");
            }
            return null;
        }
    }
}
