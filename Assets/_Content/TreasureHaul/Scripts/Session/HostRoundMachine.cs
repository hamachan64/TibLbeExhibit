using System.Collections.Generic;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// ホストの状態遷移（設計書 3.1）。時刻は引数（now = 単調増加の秒、unixMs = RoundId 用）で渡す。
    /// 保留・「本編開始」・目標額のみモードは #42。遷移の判断は <see cref="DecideNext"/> の 1 か所にまとめてある。
    /// </summary>
    public sealed class HostRoundMachine
    {
        readonly DeviceRegistry _registry;
        ThProgressParams _times;

        double _enteredAt;
        readonly List<string> _participants = new List<string>();

        public HostRoundMachine(DeviceRegistry registry, ThProgressParams times)
        {
            _registry = registry;
            _times = times;
        }

        public RoundState State { get; private set; } = RoundState.Waiting;
        /// <summary>状態が変わるたびに +1</summary>
        public int Seq { get; private set; }
        /// <summary>今の回の RoundId。Waiting 中は 0（回が無い）</summary>
        public long RoundId { get; private set; }
        /// <summary>直前に使った RoundId（次の RoundId を必ず増やすため。Waiting に戻っても残る）</summary>
        public long LastRoundId { get; private set; }
        public int Seed { get; private set; }
        public IReadOnlyList<string> Participants => _participants;

        /// <summary>待機中だけ差し替えられる（パラメータの再読み込み）。</summary>
        public bool TrySetTimes(ThProgressParams times)
        {
            if (State != RoundState.Waiting) return false;
            _times = times;
            return true;
        }

        public bool CanStart => State == RoundState.Waiting && _registry.EligibleCount >= 1;

        /// <summary>残り秒。各状態の長さから数える。Waiting は 0。</summary>
        public float RemainingSeconds(double now)
        {
            float d = DurationOf(State);
            if (d <= 0f) return 0f;
            return (float)System.Math.Max(0.0, d - (now - _enteredAt));
        }

        float DurationOf(RoundState s)
        {
            switch (s)
            {
                case RoundState.Opening: return _times.openingSeconds;
                case RoundState.Tutorial: return _times.practiceSeconds;
                case RoundState.Playing: return _times.playingSeconds;
                case RoundState.Result: return _times.resultSeconds;
                default: return 0f;
            }
        }

        /// <summary>「開始」。Waiting 中かつ参加できる端末が 1 台以上のときだけ。</summary>
        public bool TryStart(double now, long unixMs, int seed)
        {
            if (!CanStart) return false;
            RoundId = RoundIdGenerator.Next(unixMs, LastRoundId);
            LastRoundId = RoundId;
            Seed = seed;
            _participants.Clear();
            // 版の一致は問わない（版が違う端末には ThParams を送り、オープニング中に一致すれば残る。設計書 7章）
            _participants.AddRange(_registry.JoinableDeviceIds());
            EnterState(RoundState.Opening, now);
            return true;
        }

        /// <summary>「中断」。どの状態からでも Waiting へ。Waiting 中は何もしない。</summary>
        public bool Abort(double now)
        {
            if (State == RoundState.Waiting) return false;
            EnterState(RoundState.Waiting, now);
            return true;
        }

        /// <summary>時間経過による遷移。状態が変わったら true。</summary>
        public bool Tick(double now)
        {
            var next = DecideNext(now);
            if (next == null) return false;
            EnterState(next.Value, now);
            return true;
        }

        /// <summary>遷移の判断（唯一の場所）。次の状態を返す。まだなら null。</summary>
        RoundState? DecideNext(double now)
        {
            if (State == RoundState.Waiting) return null;
            if (now - _enteredAt < DurationOf(State)) return null;
            switch (State)
            {
                case RoundState.Opening: return RoundState.Tutorial;
                case RoundState.Tutorial: return RoundState.Playing;
                case RoundState.Playing: return RoundState.Result;
                case RoundState.Result: return RoundState.Waiting;
                default: return null;
            }
        }

        void EnterState(RoundState next, double now)
        {
            var prev = State;
            State = next;
            _enteredAt = now;
            Seq++;
            if (next == RoundState.Waiting)
            {
                RoundId = 0;
                _participants.Clear();
                _registry.ClearWaitNextRound();
            }
            else if (prev == RoundState.Opening || prev == RoundState.Tutorial)
            {
                // Opening → Tutorial: その時点で参加できない端末（版の不一致を含む）を外す。
                // Tutorial → Playing: 参加端末を確定する（本編中は変えない。F-FLOW-4）。
                _participants.RemoveAll(id => !_registry.IsEligible(id));
            }
        }
    }
}
