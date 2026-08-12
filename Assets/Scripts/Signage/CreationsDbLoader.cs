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
    /// 表示対象は Progress が released / released(beta) / stillTentative / unreleased のいずれかで、
    /// かつ corefolder 画像を持つレコードのみ（AGENTS.md 5章）。
    /// </summary>
    public sealed class NtCharacterRecord
    {
        public string DbKey;            // "DB_Primary" | "DB_SemiPrimary" | "DB_SelfSecondary"
        public string NumRaw;           // "44", "101-mp" など
        public int NumValue;            // 数字アニメーション用の数値（数字部分の抽出）
        public int NumSortValue;        // 並び順用の数値。算術形式（"3x11"→33, "9x9"→81）は計算結果と同値、16進表記（"0xA"→10）はデコード値
        public bool NumIsHex;           // 16進表記（"0xA" 等）。並び順では通常番号の後ろに別グループで並べる
        public string NumBadge;         // バッジ表記
        public string NameJP;           // 例 "44(シトシ)"
        public string NameEN;           // 例 "44(Folfourn)"
        public string FormalNameJP;
        public string FormalNameEN;     // 例 "NumberTales #44"
        public string ModelNumber;      // 例 "APHR-NT IV+[R]IV"
        public string ModelNameJP;      // 例 "ナンバーテールズ 正規+改良型4号機(44番機)"
        public string ModelNameEN;
        public string GenderType;       // 例 "Neutral" / "Female"（EN表記）
        public List<string> ClassNames = new List<string>();  // 所属クラス（DB表記のまま）
        public int HeightCm;
        public string ConceptAge;
        public string ConceptAgeAboutEN; // {value, about_EN} 形式の注記（無ければ空）
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

        /// <summary>
        /// サイネージ表示対象とする Progress 値（AGENTS.md 5章）。
        /// ローダ本体と SignageDbSync（画像抽出コピー）の両方がこの集合を参照する。
        /// </summary>
        public static readonly HashSet<string> ShownProgress =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "released", "released(beta)", "stillTentative", "unreleased",
            };

        /// <summary>外部データパス指定用の環境変数（RPi運用時にOS側の日次pull先を指す）。</summary>
        public const string ExternalRootEnvVar = "NTSWE_CREATIONSDB";

        /// <summary>
        /// データルート（DataBases/ と Images/ を含むフォルダ）を解決する。優先順:
        ///   1. 環境変数 NTSWE_CREATIONSDB（RPi等でOS側が日次pullするDBリポジトリ内の Works_NumberTales を指定）
        ///   2. StreamingAssets/CreationsDB（ビルド時同期分）
        ///   3. サブモジュール直読み（エディタ/開発時フォールバック）
        /// </summary>
        public static string ResolveDataRoot()
        {
            string external = Environment.GetEnvironmentVariable(ExternalRootEnvVar);
            if (!string.IsNullOrEmpty(external) && Directory.Exists(Path.Combine(external, "DataBases")))
                return external;

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

            var classDict = LoadClassDictionary(root);

            // パス1: 全DBを読み込み、(DB名, Num文字列) → レコード の索引を作る
            var loaded = new List<(string dbKey, JArray array)>();
            var index = new Dictionary<(string db, string num), JObject>();
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

                loaded.Add((dbKey, array));
                string dbShort = dbKey.Replace("DB_", "");
                foreach (var token in array)
                    if (token is JObject obj)
                        index[(dbShort, TokenToString(obj["Num"]))] = obj;
            }

            // パス2: SameModels_DBLink（$enrich: true）によるnullフィールドの継承
            var enrichState = new Dictionary<JObject, int>(); // 0/未着手は不在, 1=処理中, 2=完了
            foreach (var (_, array) in loaded)
                foreach (var token in array)
                    if (token is JObject obj)
                        EnrichRecord(obj, index, enrichState);

            // パス3: 表示対象の抽出
            foreach (var (dbKey, array) in loaded)
            {
                foreach (var token in array)
                {
                    if (token is not JObject obj) continue;
                    var record = ParseRecord(obj, dbKey, root, classDict);
                    if (record != null) results.Add(record);
                }
            }

            Debug.Log($"[Signage] 表示対象レコード: {results.Count}件 (root: {root})");
            return results;
        }

        // enrich対象外のフィールド（個体のアイデンティティおよびリンク系は継承しない）
        static readonly HashSet<string> EnrichExcludedFields = new HashSet<string>
        {
            "Num", "Num_Badge", "Progress", "Images",
        };

        /// <summary>
        /// SameModels_DBLink（db_type.json で $enrich: true 指定）のリンク先から、
        /// null/未定義のフィールドを継承する。循環リンク（67⇔67-old等）は訪問状態で防御。
        /// </summary>
        static void EnrichRecord(JObject obj, Dictionary<(string db, string num), JObject> index,
            Dictionary<JObject, int> state)
        {
            if (state.TryGetValue(obj, out int s) && s > 0) return; // 処理中 or 完了
            state[obj] = 1;

            if (obj["SameModels_DBLink"] is JArray links)
            {
                foreach (var linkToken in links)
                {
                    if (linkToken is not JObject link) continue;
                    // 別作品（_Work指定ありかつNumberTales以外）へのリンクは対象外
                    string work = (string)link["_Work"];
                    if (!string.IsNullOrEmpty(work) && work != "NumberTales") continue;

                    string db = (string)link["_DB"];
                    string num = TokenToString(link["Num"]);
                    if (string.IsNullOrEmpty(db) || !index.TryGetValue((db, num), out var target)) continue;

                    // リンク先を先にenrichしてから継承（処理中なら現状値のまま利用）
                    if (!state.TryGetValue(target, out int ts) || ts == 0)
                        EnrichRecord(target, index, state);

                    foreach (var prop in target.Properties())
                    {
                        if (EnrichExcludedFields.Contains(prop.Name)) continue;
                        if (prop.Name.EndsWith("_DBLink")) continue;
                        var local = obj[prop.Name];
                        if (local == null || local.Type == JTokenType.Null)
                            obj[prop.Name] = prop.Value.DeepClone();
                    }
                }
            }

            state[obj] = 2;
        }

        /// <summary>
        /// クラス辞書（Class → Class_EN）の実体ファイル。同一スキーマの複数辞書を併合して使う。
        /// dict_Triples.json は3桁番個体（223, 753 等）のクラスを収録する。
        /// SignageDbSync（サブモジュール → StreamingAssets 同期）もこの一覧を参照する。
        /// </summary>
        public static readonly string[] ClassDictionaryFiles =
        {
            "dict_Class.json",
            "dict_Triples.json",
        };

        /// <summary>クラス辞書群（Class → Class_EN）を読み込んで併合する。無ければ空辞書。</summary>
        static Dictionary<string, string> LoadClassDictionary(string root)
        {
            var dict = new Dictionary<string, string>();
            foreach (string fileName in ClassDictionaryFiles)
            {
                string path = Path.Combine(root, "Dictionaries", fileName);
                if (!File.Exists(path)) continue;
                try
                {
                    foreach (var token in JArray.Parse(File.ReadAllText(path)))
                    {
                        if (token is not JObject o) continue;
                        string jp = (string)o["Class"];
                        string en = (string)o["Class_EN"];
                        // 先勝ち（万一キーが重複した場合は先に読んだ辞書を正とする）
                        if (!string.IsNullOrEmpty(jp) && !string.IsNullOrEmpty(en) && !dict.ContainsKey(jp))
                            dict[jp] = en;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Signage] {fileName} のパースに失敗: {e.Message}");
                }
            }
            return dict;
        }

        static NtCharacterRecord ParseRecord(JObject obj, string dbKey, string root, Dictionary<string, string> classDict)
        {
            // ShownProgress のみ（それ以外の未公開情報をサイネージへ出さない）
            if (!ShownProgress.Contains((string)obj["Progress"] ?? ""))
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
                GenderType = TokenToString(obj["GenderType"]),
            };
            // ConceptAge は素の値 と {value, about_JP, about_EN} オブジェクトの両形式に対応
            if (obj["ConceptAge"] is JObject ageObj)
            {
                record.ConceptAge = TokenToString(ageObj["value"]);
                record.ConceptAgeAboutEN = TokenToString(ageObj["about_EN"]);
            }
            else
            {
                record.ConceptAge = TokenToString(obj["ConceptAge"]);
                record.ConceptAgeAboutEN = "";
            }
            record.NumValue = ExtractLeadingNumber(record.NumRaw);
            record.NumSortValue = ComputeSortNumber(record.NumRaw, out bool numIsHex);
            record.NumIsHex = numIsHex;
            record.HeightCm = obj["Height_cm"]?.Type == JTokenType.Integer ? (int)obj["Height_cm"] : 0;
            if (obj["Class"] is JArray classes)
                foreach (var c in classes)
                {
                    string name = (string)c;
                    if (string.IsNullOrEmpty(name)) continue;
                    // クラス辞書群（ClassDictionaryFiles）の英文表記を優先（無い場合のみDB表記のまま）
                    record.ClassNames.Add(classDict.TryGetValue(name, out var en) ? en : name);
                }

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

        // 16進表記 "0xA" "0xFF" 等の検出（プレフィクス 0x/0X、桁は大文字小文字とも許容）。
        // ※ "0x11" のような全桁数字の16進も算術形式（0×11）ではなく16進として解釈するため、
        //    判定は必ず ArithmeticNumPattern より先に行うこと。
        static readonly Regex HexNumPattern =
            new Regex(@"^0[xX]([0-9A-Fa-f]+)$", RegexOptions.Compiled);

        // 算術形式 "<整数><演算子><整数>" の検出。
        // 対応演算子: 乗算 x/×/*、加算 +、除算 /÷。
        // 減算 "-" は "101-mp" "121-sq" 等のサフィックス規約と衝突するため意図的に非対応。
        static readonly Regex ArithmeticNumPattern =
            new Regex(@"^(\d+)\s*([x×*+/÷])\s*(\d+)$", RegexOptions.Compiled);

        /// <summary>
        /// Num表記から並び順用の数値を求める。
        /// - 16進表記（"0xA"→10, "0xFF"→255）はデコード値。isHex=true を返し、並び順では最後尾の別グループになる。
        /// - 算術形式（"3x11"→33, "9x9"→81）は計算結果と同値で扱う。
        /// - それ以外は先頭の数字列を抽出する（"101-mp" → 101）。
        /// </summary>
        static int ComputeSortNumber(string raw, out bool isHex)
        {
            isHex = false;
            if (string.IsNullOrEmpty(raw)) return 0;
            string trimmed = raw.Trim();

            var hex = HexNumPattern.Match(trimmed);
            if (hex.Success && int.TryParse(hex.Groups[1].Value,
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out int hv))
            {
                isHex = true;
                return hv;
            }

            var m = ArithmeticNumPattern.Match(trimmed);
            if (m.Success
                && int.TryParse(m.Groups[1].Value, out int lhs)
                && int.TryParse(m.Groups[3].Value, out int rhs))
            {
                switch (m.Groups[2].Value)
                {
                    case "x": case "×": case "*": return lhs * rhs;
                    case "+": return lhs + rhs;
                    case "/": case "÷": return rhs != 0 ? lhs / rhs : lhs;
                }
            }

            return ExtractLeadingNumber(raw);
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
