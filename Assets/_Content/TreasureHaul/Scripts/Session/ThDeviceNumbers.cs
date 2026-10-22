using System;
using System.Collections.Generic;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    [Serializable]
    public class ThDeviceEntry
    {
        public string deviceId = "";
        /// <summary>端末番号（本体のラベルと一致）。0 は未登録扱い</summary>
        public int number;
    }

    /// <summary>treasure_haul_devices.json の中身: <c>{ "devices": [ { "deviceId": "...", "number": 1 } ] }</c></summary>
    [Serializable]
    public class ThDeviceFile
    {
        public List<ThDeviceEntry> devices = new List<ThDeviceEntry>();
    }

    /// <summary>
    /// DeviceId → 端末番号の対応表（設計書 7章。運営PCだけの別ファイル）と、その並び順（純粋クラス）。
    /// 番号は 1 以上。表に無い端末・番号 0 の端末は「未登録」で、並びでは登録済みの後ろに DeviceId の文字列順で置く。
    /// 壊れた JSON は空の表と警告、同じ番号の重複は警告（どちらの番号も残し、並びは DeviceId の文字列順で決める）。
    /// </summary>
    public sealed class ThDeviceNumbers
    {
        readonly Dictionary<string, int> _numbers = new Dictionary<string, int>();
        readonly List<string> _warnings = new List<string>();
        readonly List<ThDeviceEntry> _entries = new List<ThDeviceEntry>();

        public static ThDeviceNumbers Empty => new ThDeviceNumbers();

        /// <summary>読み込み時の警告（壊れた JSON・重複した番号・重複した DeviceId・不正な行）</summary>
        public IReadOnlyList<string> Warnings => _warnings;
        /// <summary>ファイルにあった行（番号 0 を含む。雛形の書き出しで既存の行を残すために使う）</summary>
        public IReadOnlyList<ThDeviceEntry> Entries => _entries;
        /// <summary>JSON が壊れていて読めなかったか（雛形で既存のファイルを上書きしないための判断に使う）</summary>
        public bool Broken { get; private set; }

        /// <summary>
        /// JSON から読む。null・空（ファイルが無い）は空の表で警告なし。<paramref name="readWarning"/> はファイルを読めなかったときの警告をそのまま足す。
        /// </summary>
        public static ThDeviceNumbers Parse(string json, string readWarning = null)
        {
            var r = new ThDeviceNumbers();
            if (!string.IsNullOrEmpty(readWarning)) { r._warnings.Add(readWarning); r.Broken = true; }
            if (string.IsNullOrWhiteSpace(json)) return r;
            ThDeviceFile file;
            try
            {
                file = JsonUtility.FromJson<ThDeviceFile>(json);
            }
            catch (Exception e)
            {
                r._warnings.Add($"{ThSettingsFiles.DevicesFileName} が壊れているので端末番号は使いません（{e.Message}）");
                r.Broken = true;
                return r;
            }
            if (file == null || file.devices == null)
            {
                r._warnings.Add($"{ThSettingsFiles.DevicesFileName} に devices がないので端末番号は使いません");
                r.Broken = true;
                return r;
            }
            var byNumber = new Dictionary<int, string>();
            foreach (var e in file.devices)
            {
                if (e == null || string.IsNullOrEmpty(e.deviceId)) { r._warnings.Add($"{ThSettingsFiles.DevicesFileName}: deviceId が空の行は無視しました"); continue; }
                if (e.number < 0) { r._warnings.Add($"{ThSettingsFiles.DevicesFileName}: {e.deviceId} の番号 {e.number} が負なので未登録にしました"); e.number = 0; }
                if (r._numbers.ContainsKey(e.deviceId))
                {
                    r._warnings.Add($"{ThSettingsFiles.DevicesFileName}: DeviceId {e.deviceId} が重複しています（後の行を使います）");
                    r._entries.RemoveAll(x => x.deviceId == e.deviceId);
                }
                r._numbers[e.deviceId] = e.number;
                r._entries.Add(new ThDeviceEntry { deviceId = e.deviceId, number = e.number });
            }
            foreach (var kv in r._numbers)
            {
                if (kv.Value <= 0) continue;
                if (byNumber.TryGetValue(kv.Value, out var other)) r._warnings.Add($"{ThSettingsFiles.DevicesFileName}: 端末番号 {kv.Value} が重複しています（{other} と {kv.Key}）");
                else byNumber[kv.Value] = kv.Key;
            }
            return r;
        }

        /// <summary>端末番号。未登録は 0</summary>
        public int NumberOf(string deviceId) =>
            deviceId != null && _numbers.TryGetValue(deviceId, out int n) && n > 0 ? n : 0;

        public bool IsRegistered(string deviceId) => NumberOf(deviceId) > 0;

        /// <summary>並び順: 端末番号の小さい順、未登録は後ろに DeviceId の文字列順。同じ番号は DeviceId の文字列順</summary>
        public int Compare(string a, string b)
        {
            int na = NumberOf(a), nb = NumberOf(b);
            if (na > 0 && nb > 0)
            {
                int c = na.CompareTo(nb);
                return c != 0 ? c : string.CompareOrdinal(a, b);
            }
            if (na > 0) return -1;
            if (nb > 0) return 1;
            return string.CompareOrdinal(a, b);
        }

        /// <summary>
        /// 雛形の JSON。ファイルにあった行（番号を含む）をそのまま残し、<paramref name="connectedDeviceIds"/> のうち無い端末を number=0 で足す。
        /// 足す端末は DeviceId の文字列順。
        /// </summary>
        public string BuildTemplateJson(IEnumerable<string> connectedDeviceIds)
        {
            var file = new ThDeviceFile();
            foreach (var e in _entries) file.devices.Add(new ThDeviceEntry { deviceId = e.deviceId, number = e.number });
            var add = new List<string>();
            if (connectedDeviceIds != null)
                foreach (var id in connectedDeviceIds)
                    if (!string.IsNullOrEmpty(id) && !_numbers.ContainsKey(id) && !add.Contains(id)) add.Add(id);
            add.Sort(string.CompareOrdinal);
            foreach (var id in add) file.devices.Add(new ThDeviceEntry { deviceId = id, number = 0 });
            return JsonUtility.ToJson(file, true);
        }
    }
}
