using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>読み込みの結果。</summary>
    public sealed class ThParamsLoadResult
    {
        public ThParams Params;
        /// <summary>版 = 重ねた後の値の JsonUtility.ToJson の SHA-256 の先頭8桁（16進小文字）</summary>
        public string Version;
        public List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// 初期値＋JSON（null 可）から「使う値・版・警告」を作る純粋クラス（N-7、設計書 7章）。
    /// 書いた項目だけ上書き／壊れた JSON は初期値／範囲外の項目は初期値／組み合わせの矛盾はグループごと初期値。
    /// </summary>
    public static class ThParamsLoader
    {
        public const int MaxPlayers = 4;

        sealed class Group
        {
            public string Field;
            public string Label;
            public Func<ThParams, ThEnvironmentInfo, List<string>> Validate;
        }

        static readonly Group[] Groups =
        {
            new Group { Field = nameof(ThParams.progress), Label = "P-1 進行", Validate = ValidateProgress },
            new Group { Field = nameof(ThParams.practice), Label = "P-2 練習", Validate = ValidatePractice },
            new Group { Field = nameof(ThParams.grip), Label = "P-3 持ち方", Validate = ValidateGrip },
            new Group { Field = nameof(ThParams.treasure), Label = "P-4 お宝・減額", Validate = ValidateTreasure },
            new Group { Field = nameof(ThParams.layout), Label = "P-5 配置・バランス", Validate = ValidateLayout },
            new Group { Field = nameof(ThParams.operation), Label = "P-6 運営", Validate = (p, e) => new List<string>() },
        };

        public static ThParamsLoadResult Load(ThParams defaults, string json, ThEnvironmentInfo env)
        {
            var result = new ThParamsLoadResult();
            var work = defaults.Clone();

            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    JsonUtility.FromJsonOverwrite(json, work);
                }
                catch (Exception e)
                {
                    result.Warnings.Add($"パラメータの JSON が壊れているので初期値を使います（{e.Message}）");
                    work = defaults.Clone();
                }
            }

            foreach (var g in Groups)
            {
                var field = typeof(ThParams).GetField(g.Field);
                // 個々の範囲
                var cur = field.GetValue(work);
                var def = field.GetValue(defaults);
                if (cur == null) { field.SetValue(work, DeepCopy(def)); result.Warnings.Add($"{g.Label}: 欠けていたので初期値にしました"); continue; }
                CheckRanges(cur, def, g.Label, result.Warnings);

                // 組み合わせ
                var errors = g.Validate(work, env);
                if (errors.Count > 0)
                {
                    foreach (var err in errors) result.Warnings.Add($"{g.Label}: {err}。このグループ全体を初期値に戻しました");
                    field.SetValue(work, DeepCopy(def));
                    // 初期値に戻しても矛盾していれば、環境側（配置候補点など）の問題
                    if (g.Validate(work, env).Count > 0)
                        result.Warnings.Add($"{g.Label}: 初期値でも矛盾しています（環境の設定を確認してください）");
                }
            }

            result.Params = work;
            result.Version = ComputeVersion(work);
            return result;
        }

        public static string ComputeVersion(ThParams p)
        {
            byte[] bytes;
            using (var sha = SHA256.Create()) bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(JsonUtility.ToJson(p)));
            var sb = new StringBuilder();
            for (int i = 0; i < 4; i++) sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        // ---- 範囲 ----

        static object DeepCopy(object o) => o == null ? null : JsonUtility.FromJson(JsonUtility.ToJson(o), o.GetType());

        static void CheckRanges(object cur, object def, string path, List<string> warnings)
        {
            foreach (var f in cur.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var curV = f.GetValue(cur);
                var defV = f.GetValue(def);
                var range = f.GetCustomAttribute<RangeAttribute>();
                if (range != null)
                {
                    double d = Convert.ToDouble(curV, CultureInfo.InvariantCulture);
                    if (!(d >= range.min && d <= range.max))
                    {
                        f.SetValue(cur, defV);
                        warnings.Add($"{path}.{f.Name}: {d} は範囲 {range.min}〜{range.max} の外なので初期値 {defV} にしました");
                    }
                }
                else if (f.FieldType.IsArray)
                {
                    var arr = (Array)curV;
                    var darr = (Array)defV;
                    if (arr == null || arr.Length != darr.Length)
                    {
                        var copy = (Array)Array.CreateInstance(f.FieldType.GetElementType(), darr.Length);
                        for (int i = 0; i < darr.Length; i++) copy.SetValue(DeepCopy(darr.GetValue(i)), i);
                        f.SetValue(cur, copy);
                        warnings.Add($"{path}.{f.Name}: 要素数が {darr.Length} でないので初期値にしました");
                        continue;
                    }
                    for (int i = 0; i < arr.Length; i++)
                    {
                        if (arr.GetValue(i) == null) { arr.SetValue(DeepCopy(darr.GetValue(i)), i); continue; }
                        CheckRanges(arr.GetValue(i), darr.GetValue(i), $"{path}.{f.Name}[{i}]", warnings);
                    }
                }
                else if (f.FieldType.IsClass && f.FieldType != typeof(string))
                {
                    if (curV == null) { f.SetValue(cur, DeepCopy(defV)); continue; }
                    CheckRanges(curV, defV, $"{path}.{f.Name}", warnings);
                }
            }
        }

        // ---- 組み合わせ ----

        static List<string> ValidateProgress(ThParams p, ThEnvironmentInfo env)
        {
            var errors = new List<string>();
            if (!(p.progress.heartbeatInterval < p.progress.hostLostSeconds))
                errors.Add("Heartbeat 間隔が運営PC切断の待ち以上");
            return errors;
        }

        static List<string> ValidatePractice(ThParams p, ThEnvironmentInfo env)
        {
            var errors = new List<string>();
            if (env?.TutorialSlotCount is int slots && slots < MaxPlayers)
                errors.Add($"TutorialSlot が {slots} 個で、最大参加人数 {MaxPlayers} より少ない");
            return errors;
        }

        static List<string> ValidateGrip(ThParams p, ThEnvironmentInfo env)
        {
            var errors = new List<string>();
            if (!(p.grip.gripThreshold > p.grip.releaseThreshold))
                errors.Add("「つかむ」しきい値が「離す」しきい値以下");
            return errors;
        }

        static List<string> ValidateTreasure(ThParams p, ThEnvironmentInfo env)
        {
            var t = p.treasure;
            var errors = new List<string>();
            if (t.damagePercent1 + t.damagePercent2 + t.damagePercent3 != 100)
                errors.Add("減額率 3 つの合計が 100% でない");
            CheckValueRange(errors, "S", t.sValueMin, t.sValueMax);
            CheckValueRange(errors, "M", t.mValueMin, t.mValueMax);
            CheckValueRange(errors, "L", t.lValueMin, t.lValueMax);
            if (t.secretValue % 100 != 0) errors.Add("秘宝の価値が 100G 刻みでない");
            return errors;
        }

        static void CheckValueRange(List<string> errors, string label, int min, int max)
        {
            if (min % 100 != 0 || max % 100 != 0) errors.Add($"{label} の価値の範囲が 100G 刻みでない");
            if (min > max) errors.Add($"{label} の価値の下限が上限より大きい");
        }

        static List<string> ValidateLayout(ThParams p, ThEnvironmentInfo env)
        {
            var errors = new List<string>();
            if (env?.PlacementPointCount is int points)
            {
                for (int i = 0; i < p.layout.rows.Length; i++)
                {
                    var r = p.layout.rows[i];
                    if (r.spawnS + r.spawnM > points)
                        errors.Add($"{i + 1} 人の出現数 {r.spawnS + r.spawnM} が配置候補点 {points} より多い");
                }
            }
            return errors;
        }
    }
}
