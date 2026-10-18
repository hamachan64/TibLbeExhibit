using System;
using System.IO;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    public enum ThMode { TargetAndScore = 0, TargetOnly = 1 }
    public enum ThGripStyle { Beam = 0, Direct = 1, Swing = 2 }

    /// <summary>ゲーム設定（N-8）。運営PCで保持し、ThState で全端末へ配る。* の付く項目は #42 以降で使う。</summary>
    [Serializable]
    public class ThGameSettings
    {
        public ThMode mode = ThMode.TargetAndScore;
        public bool statue;
        public bool large;
        public bool golem;
        public bool secret;
        public ThGripStyle gripStyle = ThGripStyle.Beam;

        public ThGameSettings Clone() => (ThGameSettings)MemberwiseClone();

        public bool IsValid() =>
            Enum.IsDefined(typeof(ThMode), mode) && Enum.IsDefined(typeof(ThGripStyle), gripStyle);

        /// <summary>JSON から読む。null・空 → 初期値（警告なし）。壊れている・値が不正 → 初期値と警告。</summary>
        public static ThGameSettings Parse(string json, out string warning)
        {
            warning = null;
            if (string.IsNullOrWhiteSpace(json)) return new ThGameSettings();
            try
            {
                var s = JsonUtility.FromJson<ThGameSettings>(json);
                if (s == null) throw new ArgumentException("null");
                if (!s.IsValid())
                {
                    warning = "ゲーム設定の値が不正なので初期値にしました";
                    return new ThGameSettings();
                }
                return s;
            }
            catch (Exception)
            {
                warning = "ゲーム設定の JSON が壊れているので初期値にしました";
                return new ThGameSettings();
            }
        }
    }

    /// <summary>運営PCの persistentDataPath にある設定ファイルの読み書き（パスは引数。テストでは一時フォルダ）。</summary>
    public static class ThSettingsFiles
    {
        public const string ParamsFileName = "treasure_haul_params.json";
        public const string SettingsFileName = "treasure_haul_settings.json";

        public static string ParamsPath(string directory) => Path.Combine(directory, ParamsFileName);
        public static string SettingsPath(string directory) => Path.Combine(directory, SettingsFileName);

        /// <summary>ファイルが無い → null。読めない → null と警告。</summary>
        public static string ReadTextOrNull(string path, out string warning)
        {
            warning = null;
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception e)
            {
                warning = $"{Path.GetFileName(path)} を読めません: {e.Message}";
                return null;
            }
        }

        public static ThGameSettings LoadSettings(string directory, out string warning)
        {
            var text = ReadTextOrNull(SettingsPath(directory), out warning);
            if (warning != null) return new ThGameSettings();
            return ThGameSettings.Parse(text, out warning);
        }

        public static void SaveSettings(string directory, ThGameSettings settings)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(SettingsPath(directory), JsonUtility.ToJson(settings, true));
        }
    }
}
