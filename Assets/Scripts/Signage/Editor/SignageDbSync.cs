using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace NTsWallpaperEngine.Signage.EditorTools
{
    /// <summary>
    /// サブモジュール（正）→ StreamingAssets/CreationsDB（git管理外の生成物）への同期。
    /// 表示対象（CreationsDbLoader.ShownProgress）のレコードが参照する corefolder 画像のみをコピーし、
    /// それ以外の未公開画像をビルドへ含めない。
    /// </summary>
    public static class SignageDbSync
    {
        static readonly (string dbKey, string jsonName)[] Sources =
        {
            ("DB_Primary", "db_Primary.json"),
            ("DB_SemiPrimary", "db_SemiPrimary.json"),
            ("DB_SelfSecondary", "db_SelfSecondary.json"),
        };

        [MenuItem("Signage/Sync CreationsDB (submodule → StreamingAssets)")]
        public static void SyncMenu()
        {
            if (SyncAll())
                EditorUtility.DisplayDialog("Signage", "CreationsDB の同期が完了。", "OK");
        }

        public static bool SyncAll()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string src = Path.Combine(projectRoot, CreationsDbLoader.SubmoduleRelativeRoot.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(Path.Combine(src, "DataBases")))
            {
                Debug.LogError($"[Signage] サブモジュールが見つからない: {src}\nscripts/setup-submodule を実行して。");
                return false;
            }

            string dst = Path.Combine(Application.dataPath, "StreamingAssets", "CreationsDB");
            if (Directory.Exists(dst)) Directory.Delete(dst, true);
            Directory.CreateDirectory(Path.Combine(dst, "DataBases"));

            // Class英文表記辞書（dict_Triples.json は3桁番個体のクラスを収録。一覧はローダ側が正）
            foreach (string dictName in CreationsDbLoader.ClassDictionaryFiles)
            {
                string dictSrc = Path.Combine(src, "Dictionaries", dictName);
                if (!File.Exists(dictSrc)) continue;
                Directory.CreateDirectory(Path.Combine(dst, "Dictionaries"));
                File.Copy(dictSrc, Path.Combine(dst, "Dictionaries", dictName), true);
            }

            // グローバルクラス辞書（data/Dictionaries/。レゾンデイトルカンパニー / シンフォニー.XVI 所属個体用）
            foreach (string dictName in CreationsDbLoader.GlobalClassDictionaryFiles)
            {
                string dictSrc = Path.GetFullPath(Path.Combine(src, "..", "Dictionaries", dictName));
                if (!File.Exists(dictSrc))
                {
                    Debug.LogWarning($"[Signage] グローバル辞書が無い: {dictSrc}（scripts/setup-submodule を再実行して sparse に /data/Dictionaries を追加）");
                    continue;
                }
                Directory.CreateDirectory(Path.Combine(dst, "GlobalDictionaries"));
                File.Copy(dictSrc, Path.Combine(dst, "GlobalDictionaries", dictName), true);
            }

            int copiedImages = 0, releasedCount = 0;
            foreach (var (dbKey, jsonName) in Sources)
            {
                string jsonPath = Path.Combine(src, "DataBases", jsonName);
                if (!File.Exists(jsonPath))
                {
                    Debug.LogWarning($"[Signage] DBファイルが無い: {jsonPath}");
                    continue;
                }
                File.Copy(jsonPath, Path.Combine(dst, "DataBases", jsonName), true);

                // 表示対象レコードが参照する画像だけを抽出コピー
                var array = JArray.Parse(File.ReadAllText(jsonPath));
                foreach (var token in array)
                {
                    if (token is not JObject obj) continue;
                    if (!CreationsDbLoader.ShownProgress.Contains((string)obj["Progress"] ?? ""))
                        continue;
                    if (obj["Images"] is not JObject images) continue;
                    if (images["corefolder_PNGPath"] is not JArray corefolder || corefolder.Count == 0) continue;

                    releasedCount++;
                    foreach (var rel in corefolder)
                    {
                        string relPath = (string)rel;
                        if (string.IsNullOrEmpty(relPath)) continue;
                        string relFile = Path.Combine("Images", dbKey, "corefolder",
                            relPath.Replace('/', Path.DirectorySeparatorChar) + ".png");
                        string from = Path.Combine(src, relFile);
                        if (!File.Exists(from)) continue;
                        string to = Path.Combine(dst, relFile);
                        Directory.CreateDirectory(Path.GetDirectoryName(to));
                        File.Copy(from, to, true);
                        copiedImages++;
                    }
                }
            }

            AssetDatabase.Refresh();
            Debug.Log($"[Signage] Sync完了: 表示対象レコード {releasedCount}件 / corefolder画像 {copiedImages}枚 → {dst}");
            return true;
        }
    }
}
