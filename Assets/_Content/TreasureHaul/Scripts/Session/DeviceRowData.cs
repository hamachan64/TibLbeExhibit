using System;
using System.Collections.Generic;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>運営UIの端末一覧に出す手の認識の状態</summary>
    public enum ThHandStatus { Unknown = 0, None = 1, One = 2, Both = 3 }

    /// <summary>端末一覧の 1 行ぶんの表示内容（F-OPS-1。値だけ。文言への変換は View）</summary>
    public sealed class DeviceRowData : IEquatable<DeviceRowData>
    {
        public string DeviceId = "";
        /// <summary>端末番号。未登録は 0（View は「未登録」＋DeviceId の末尾 6 文字を出す）</summary>
        public int Number;
        public bool Connected;
        public bool AlignmentConfirmed;
        public bool Spectator;
        public ThHandStatus Hand;
        /// <summary>電池（%）。不明は -1</summary>
        public int Battery = -1;
        /// <summary>電池が〔電池の警告〕以下（不明は false）</summary>
        public bool BatteryWarning;
        /// <summary>練習完了の列を出す局面か（Tutorial〜Result の間だけ）</summary>
        public bool PracticeVisible;
        public bool PracticeDone;

        public bool Equals(DeviceRowData o) =>
            o != null && DeviceId == o.DeviceId && Number == o.Number && Connected == o.Connected && AlignmentConfirmed == o.AlignmentConfirmed
            && Spectator == o.Spectator && Hand == o.Hand && Battery == o.Battery && BatteryWarning == o.BatteryWarning
            && PracticeVisible == o.PracticeVisible && PracticeDone == o.PracticeDone;

        public override bool Equals(object obj) => Equals(obj as DeviceRowData);
        public override int GetHashCode() => DeviceId == null ? 0 : DeviceId.GetHashCode();

        /// <summary>未登録の端末に出す DeviceId の末尾 6 文字（6 文字未満はそのまま）</summary>
        public static string ShortId(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return "";
            return deviceId.Length <= 6 ? deviceId : deviceId.Substring(deviceId.Length - 6);
        }

        /// <summary>2 つの一覧が同じ内容か（長さと各行）</summary>
        public static bool SameList(IReadOnlyList<DeviceRowData> a, IReadOnlyList<DeviceRowData> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (!a[i].Equals(b[i])) return false;
            return true;
        }
    }

    /// <summary>端末一覧を作る純粋関数（端末番号の順。未登録は後ろに DeviceId 順）</summary>
    public static class DeviceRowBuilder
    {
        /// <param name="statusInterval">状況報告の間隔。これの 3 倍を過ぎて届かない手・電池は「不明」</param>
        public static DeviceRowData[] Build(DeviceRegistry registry, RoundState state, IReadOnlyDictionary<string, double> practiceDone,
            double now, float statusInterval, int batteryWarningPercent)
        {
            var ids = registry.SortedDeviceIds();
            var rows = new DeviceRowData[ids.Count];
            bool practiceVisible = state == RoundState.Tutorial || state == RoundState.Playing || state == RoundState.Result;
            for (int i = 0; i < rows.Length; i++)
            {
                var d = registry.Get(ids[i]);
                bool fresh = d.Connected && now - d.StatusAt <= statusInterval * 3f;
                var row = new DeviceRowData
                {
                    DeviceId = d.DeviceId,
                    Number = registry.Numbers.NumberOf(d.DeviceId),
                    Connected = d.Connected,
                    AlignmentConfirmed = d.AlignmentConfirmed,
                    Spectator = d.Spectator,
                    Hand = !fresh || d.HandCount < 0 ? ThHandStatus.Unknown
                        : d.HandCount == 0 ? ThHandStatus.None : d.HandCount == 1 ? ThHandStatus.One : ThHandStatus.Both,
                    Battery = fresh ? d.BatteryPercent : -1,
                    PracticeVisible = practiceVisible,
                    PracticeDone = practiceDone != null && practiceDone.ContainsKey(d.DeviceId),
                };
                row.BatteryWarning = row.Battery >= 0 && row.Battery <= batteryWarningPercent;
                rows[i] = row;
            }
            return rows;
        }
    }
}
