using System;
using System.Collections.Generic;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>参加端末 1 台の 1 回ぶんの記録（企画書 10.3。各「秒」は回の開始からの経過。無ければ null）</summary>
    public sealed class PlayerRecord
    {
        public string DeviceId = "";
        /// <summary>端末番号。未登録は 0</summary>
        public int Number;
        /// <summary>練習の色番号。割り当てが無ければ -1</summary>
        public int Color = -1;
        public double? PracticeDoneSeconds;
        public double? FirstGrabSeconds;
        public double? FirstCollectSeconds;
        public int CollectedCount;
        public int CollectedAmount;
        public int DamageCount;
        public int DamageAmount;
    }

    /// <summary>
    /// 1 回ぶんの記録の集計（純粋クラス。企画書 10.3、設計書 8章）。ホストが確定した出来事だけから作る（端末の申告は使わない）。
    /// 時刻は <see cref="StartNow"/> と同じ基準（ホストの now）で受け取り、回の開始からの秒にして持つ。
    /// </summary>
    public sealed class RoundRecord
    {
        readonly Dictionary<string, PlayerRecord> _players = new Dictionary<string, PlayerRecord>();
        readonly List<string> _participants = new List<string>();

        // ---- 回の開始時に決まる ----
        public long RoundId;
        /// <summary>回の開始時刻（運営PCのローカル）。CSV の日付とファイル名はこれで決める</summary>
        public DateTimeOffset StartedAt;
        /// <summary>回の開始時のホストの now（単調増加の秒）</summary>
        public double StartNow;
        public string Mode = "";
        public string Extras = "";
        public string GripStyle = "";
        public string ParamsVersion = "";

        // ---- 回の終わりに決まる ----
        /// <summary>中断で終わったか（false = 完了。Result → Waiting）</summary>
        public bool Aborted;
        /// <summary>中断したときの状態（完了は Waiting）</summary>
        public RoundState AbortedState = RoundState.Waiting;
        public bool WasHeld;
        public float HeldSeconds;
        public int CollectedAmount;
        public int TargetAmount;
        /// <summary>適用した減額の合計（損害額）</summary>
        public int DamageTotal;

        public int ParticipantCount => _participants.Count;
        public bool TargetReached => TargetAmount > 0 && CollectedAmount >= TargetAmount;

        /// <summary>追加要素の文字列（有効なものを「|」でつなぐ。無ければ空）</summary>
        public static string ExtrasOf(ThGameSettings s)
        {
            if (s == null) return "";
            var list = new List<string>();
            if (s.statue) list.Add("statue");
            if (s.large) list.Add("large");
            if (s.golem) list.Add("golem");
            if (s.secret) list.Add("secret");
            return string.Join("|", list);
        }

        /// <summary>参加端末（その時点の最新）を覚える。<paramref name="compare"/> の順に並べて行にする。人数はこの数</summary>
        public void SetParticipants(IEnumerable<string> deviceIds, Comparison<string> compare, Func<string, int> numberOf)
        {
            _participants.Clear();
            if (deviceIds != null) foreach (var id in deviceIds) if (!string.IsNullOrEmpty(id) && !_participants.Contains(id)) _participants.Add(id);
            if (compare != null) _participants.Sort(compare);
            foreach (var id in _participants) Get(id).Number = numberOf != null ? numberOf(id) : 0;
        }

        /// <summary>練習の色の割り当て（DeviceId → 色番号）を取り込む</summary>
        public void SetColors(IReadOnlyDictionary<string, int> colors)
        {
            if (colors == null) return;
            foreach (var id in _participants) Get(id).Color = colors.TryGetValue(id, out int c) ? c : -1;
        }

        PlayerRecord Get(string deviceId)
        {
            if (!_players.TryGetValue(deviceId, out var p)) { p = new PlayerRecord { DeviceId = deviceId }; _players[deviceId] = p; }
            return p;
        }

        double Rel(double now) => Math.Max(0.0, now - StartNow);

        /// <summary>初めて持った（S は ThGrabbed、M は ThLifted。片手予約は呼ばない）。2 回目以降は無視</summary>
        public void NoteFirstHold(string deviceId, double now)
        {
            if (string.IsNullOrEmpty(deviceId)) return;
            var p = Get(deviceId);
            if (p.FirstGrabSeconds == null) p.FirstGrabSeconds = Rel(now);
        }

        /// <summary>ホストが適用した減額（確定時の持ち主、いなければ判定役）</summary>
        public void NoteDamage(string deviceId, int amount)
        {
            if (string.IsNullOrEmpty(deviceId)) return;
            var p = Get(deviceId);
            p.DamageCount++;
            p.DamageAmount += amount;
        }

        /// <summary>本編のお宝の回収（回収した人 = 判定役 = 最後に持った人）</summary>
        public void NoteCollect(string deviceId, int value, double now)
        {
            if (string.IsNullOrEmpty(deviceId)) return;
            var p = Get(deviceId);
            if (p.FirstCollectSeconds == null) p.FirstCollectSeconds = Rel(now);
            p.CollectedCount++;
            p.CollectedAmount += value;
        }

        /// <summary>練習用お宝の回収の確定 = 練習完了（1 回だけ）</summary>
        public void NotePracticeDone(string deviceId, double now)
        {
            if (string.IsNullOrEmpty(deviceId)) return;
            var p = Get(deviceId);
            if (p.PracticeDoneSeconds == null) p.PracticeDoneSeconds = Rel(now);
        }

        /// <summary>参加端末ごとの行（SetParticipants で並べた順。記録が無い端末も空の行で出す）</summary>
        public List<PlayerRecord> Players()
        {
            var list = new List<PlayerRecord>(_participants.Count);
            foreach (var id in _participants) list.Add(Get(id));
            return list;
        }
    }
}
