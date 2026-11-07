using System;
using System.Collections.Generic;
using System.Globalization;

namespace STYLY.TIB_Template
{
    /// <summary>
    /// 端末ごとの外部設定（AppConfig.toml）の中身。
    /// 読めるのは TOML のうち「キー = 値」の行だけ（bool / 数値 / 文字列）。テーブルや配列は無視する。
    /// 壊れた行は例外にせず飛ばす。
    /// </summary>
    public sealed class AppConfig
    {
        public static readonly AppConfig Empty = new AppConfig(new Dictionary<string, string>());

        private readonly Dictionary<string, string> _values;

        public IEnumerable<string> Keys => _values.Keys;

        private AppConfig(Dictionary<string, string> values)
        {
            _values = values;
        }

        public bool GetBool(string key, bool fallback)
        {
            if (!_values.TryGetValue(key, out var raw)) return fallback;
            if (raw == "true") return true;
            if (raw == "false") return false;
            return fallback;
        }

        /// <summary>数値（整数・小数）を返す。引用符付き・数値でない・NaN / Infinity は fallback。</summary>
        public float GetFloat(string key, float fallback)
        {
            if (!_values.TryGetValue(key, out var raw)) return fallback;
            if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) return fallback;
            return float.IsNaN(f) || float.IsInfinity(f) ? fallback : f;
        }

        /// <summary>整数を返す。引用符付き・整数でない場合は fallback。</summary>
        public int GetInt(string key, int fallback)
        {
            if (!_values.TryGetValue(key, out var raw)) return fallback;
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : fallback;
        }

        /// <summary>引用符で囲まれた値だけを文字列として返す。</summary>
        public string GetString(string key, string fallback)
        {
            if (!_values.TryGetValue(key, out var raw)) return fallback;
            if (raw.Length >= 2 && (raw[0] == '"' || raw[0] == '\'') && raw[raw.Length - 1] == raw[0])
                return raw.Substring(1, raw.Length - 2);
            return fallback;
        }

        /// <param name="warnings">読み飛ばした行の説明を足す先。null なら捨てる</param>
        public static AppConfig Parse(string text, List<string> warnings = null)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(text)) return new AppConfig(values);

            // テーブル内のキーをトップレベルと取り違えないよう、[xxx] 以降は読まない
            bool inTable = false;
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = StripComment(lines[i]).Trim();
                if (line.Length == 0) continue;
                if (line[0] == '[')
                {
                    inTable = true;
                    continue;
                }
                if (inTable) continue;

                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    warnings?.Add($"line {i + 1}: '=' がないので無視: {line}");
                    continue;
                }

                string key = line.Substring(0, eq).Trim();
                // 引用符は残す。"true" を bool の true と取り違えないため
                values[key] = line.Substring(eq + 1).Trim();
            }
            return new AppConfig(values);
        }

        /// <summary># 以降を落とす。ただし文字列の中の # は残す。</summary>
        private static string StripComment(string line)
        {
            char quote = '\0';
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                }
                else if (c == '"' || c == '\'') quote = c;
                else if (c == '#') return line.Substring(0, i);
            }
            return line;
        }
    }
}
