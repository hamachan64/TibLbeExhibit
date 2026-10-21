using System;
using R3;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 端末側のホスト追従（設計書 3.2）。ThState / ThHeartbeat を受けて状態・残り時間を採用し、
    /// 取りこぼし・版の不一致・回の途中参加・ホスト途絶を判断する。時刻は引数（単調増加の秒）で渡す。
    /// RoundId が 0 のメッセージは「回が無い（Waiting）」を表す。
    /// </summary>
    public sealed class ClientRoundTracker : IDisposable
    {
        readonly string _deviceId;
        ThProgressParams _times;

        readonly ReactiveProperty<RoundState> _state = new ReactiveProperty<RoundState>(RoundState.Waiting);
        readonly ReactiveProperty<float> _remaining = new ReactiveProperty<float>(0f);
        readonly ReactiveProperty<bool> _isParticipant = new ReactiveProperty<bool>(false);
        readonly ReactiveProperty<bool> _isHostLost = new ReactiveProperty<bool>(false);
        readonly ReactiveProperty<bool> _isHeld = new ReactiveProperty<bool>(false);
        readonly ReactiveProperty<int> _ownColor = new ReactiveProperty<int>(-1);

        public ReadOnlyReactiveProperty<RoundState> State => _state;
        public ReadOnlyReactiveProperty<float> RemainingSeconds => _remaining;
        public ReadOnlyReactiveProperty<bool> IsParticipant => _isParticipant;
        public ReadOnlyReactiveProperty<bool> IsHostLost => _isHostLost;
        /// <summary>保留中か（練習の残り秒を数えない）</summary>
        public ReadOnlyReactiveProperty<bool> IsHeld => _isHeld;
        /// <summary>自分の色番号（ホストの ThState の tutorialColors から。割り当てが無い・Waiting では -1。回が変わる・待機に戻ると消える）</summary>
        public ReadOnlyReactiveProperty<int> OwnColor => _ownColor;
        /// <summary>DeviceId → 色番号（全員分。Tutorial 〜 Result の間。待機に戻る・回が変わると空になる）</summary>
        public System.Collections.Generic.IReadOnlyDictionary<string, int> DeviceColors => _deviceColors;
        /// <summary>その端末の色番号（無ければ -1）</summary>
        public int ColorOf(string deviceId) => deviceId != null && _deviceColors.TryGetValue(deviceId, out int c) ? c : -1;
        System.Collections.Generic.Dictionary<string, int> _deviceColors = new System.Collections.Generic.Dictionary<string, int>();

        /// <summary>目標額（ホストの ThState から。Playing 以降。無ければ 0。待機に戻る・回が変わると消える）</summary>
        public int Target { get; private set; }
        /// <summary>結果（Result のときだけ。無ければ null）</summary>
        public ThResultMsg Result { get; private set; }

        /// <summary>知っている RoundId（0 = 回が無い）</summary>
        public long RoundId { get; private set; }
        /// <summary>知っている状態の通し番号（-1 = まだ受けていない）</summary>
        public int Seq { get; private set; } = -1;
        public int Seed { get; private set; }
        public string[] Participants { get; private set; } = new string[0];
        public ThGameSettings Settings { get; private set; } = new ThGameSettings();
        /// <summary>ホストの ThState が載せていたパラメータ版</summary>
        public string HostParamsVersion { get; private set; }
        /// <summary>この端末が持っているパラメータ版（ThParams を採用したら SessionClient が更新する）</summary>
        public string LocalParamsVersion { get; set; } = "";
        /// <summary>回の途中から入ったので、その回は参加しない（次の Waiting で解除）</summary>
        public bool WaitNextRound { get; private set; }

        double _anchorTime;
        float _anchorRemaining;
        double _lastAlive;
        double _lastResyncAt = double.NegativeInfinity;
        long _abortedRoundId = -1;

        // 運営PCの再起動の検出（hostEpoch が変わったら状況報告を送り直す）
        long _hostEpoch;
        bool _epochKnown;
        // 「今のホストの起動値に対して、自分が報告したか」。報告したときに知っていた起動値（未知なら未記録）
        bool _reportedValid;
        long _reportedEpoch;
        bool _forceResync;

        // ホストが最後に言った内容（Heartbeat）
        int _hostSeq = -1;
        long _hostRoundId;
        bool _hostSeen;

        public ClientRoundTracker(string deviceId, ThProgressParams times, double now)
        {
            _deviceId = deviceId;
            _times = times;
            _lastAlive = now;
        }

        public void UpdateTimes(ThProgressParams times) => _times = times;

        public void Dispose()
        {
            _state.Dispose(); _remaining.Dispose(); _isParticipant.Dispose(); _isHostLost.Dispose(); _isHeld.Dispose(); _ownColor.Dispose();
        }

        double Remaining(double now) => _isHeld.Value ? _anchorRemaining : Math.Max(0.0, _anchorRemaining - (now - _anchorTime));

        /// <summary>RoundId が古い（0 でなく、知っている回より前）か</summary>
        bool IsOld(long roundId) => roundId != 0 && roundId < RoundId;

        void MarkAlive(double now)
        {
            _lastAlive = now;
            if (_isHostLost.Value) _isHostLost.Value = false;
        }

        public void OnState(long roundId, ThStateMsg msg, double now)
        {
            if (msg == null || IsOld(roundId)) return;
            var state = (RoundState)msg.state;
            if (!Enum.IsDefined(typeof(RoundState), msg.state)) return;
            MarkAlive(now);

            // 中止した回の続きは受け付けない（その回は終わったことにする）
            if (state != RoundState.Waiting && roundId == _abortedRoundId) return;

            // 同じ回の古い ThState は捨てる（Waiting は運営PC再起動で通し番号が戻るので採用する）
            if (state != RoundState.Waiting && roundId == RoundId && msg.seq < Seq) return;

            bool newRound = roundId != RoundId;
            if (state == RoundState.Waiting)
            {
                WaitNextRound = false;
                _abortedRoundId = -1;
            }
            else if (newRound)
            {
                // Opening から見ていれば参加できる。それより後の状態で初めて知った回は、次の回を待つ。
                WaitNextRound = state != RoundState.Opening;
                if (WaitNextRound) _forceResync = true;   // ホストに「この回は参加しない」と報告する
            }

            RoundId = roundId;
            Seq = msg.seq;
            Seed = msg.seed;
            Participants = msg.participants ?? new string[0];
            Settings = msg.settings ?? new ThGameSettings();
            HostParamsVersion = msg.paramsVersion ?? "";
            NoteEpoch(msg.hostEpoch);
            _hostSeen = true;
            _hostSeq = msg.seq;
            _hostRoundId = roundId;
            _anchorTime = now;
            _anchorRemaining = Math.Max(0f, msg.remaining);
            // 保留（Tutorial 中だけ有効）。残り秒の基準を置いた後、残り秒の計算より先に反映する
            _isHeld.Value = state == RoundState.Tutorial && msg.held;
            Target = state == RoundState.Waiting ? 0 : Math.Max(0, msg.target);
            Result = state == RoundState.Result && msg.result != null && msg.result.present ? msg.result : null;

            // 色（練習の割り当て）。参加・状態の通知より先に揃える
            ApplyColors(state, msg);

            // 購読者が State の通知を受けたとき、残り秒・参加が新しい状態に揃っているように、State を最後に変える
            _remaining.Value = state == RoundState.Waiting ? 0f : (float)Remaining(now);
            SetParticipant(ComputeParticipant(state));
            _state.Value = state;
        }

        void ApplyColors(RoundState state, ThStateMsg msg)
        {
            _deviceColors = state == RoundState.Waiting ? new System.Collections.Generic.Dictionary<string, int>() : PracticeColors.Read(msg.participants, msg.tutorialColors);
            int own = ColorOf(_deviceId);
            if (_ownColor.Value != own) _ownColor.Value = own;
        }

        public void OnHeartbeat(long roundId, ThHeartbeatMsg msg, double now)
        {
            if (msg == null || IsOld(roundId)) return;
            MarkAlive(now);
            if (roundId == _abortedRoundId && roundId != 0) return;

            _hostSeen = true;
            _hostSeq = msg.seq;
            _hostRoundId = roundId;
            HostParamsVersion = msg.paramsVersion ?? HostParamsVersion;
            NoteEpoch(msg.hostEpoch);

            // 時間合わせ: 同じ通し番号のときだけ、ずれが〔時間を合わせるずれ〕以上なら合わせる
            if (msg.seq == Seq && roundId == RoundId && _state.Value != RoundState.Waiting)
            {
                bool held = _state.Value == RoundState.Tutorial && msg.held;
                if (held != _isHeld.Value)
                {
                    // 保留の切り替え。ホストの残り秒に合わせ直して、解除ならそこから数える
                    _isHeld.Value = held;
                    _anchorTime = now;
                    _anchorRemaining = Math.Max(0f, msg.remaining);
                    _remaining.Value = _anchorRemaining;
                    return;
                }
                double local = Remaining(now);
                if (Math.Abs(local - msg.remaining) >= _times.syncTolerance)
                {
                    _anchorTime = now;
                    _anchorRemaining = Math.Max(0f, msg.remaining);
                    _remaining.Value = _anchorRemaining;
                }
            }
        }

        /// <summary>ホストの起動値を記録する。報告済みの起動値と違えば NeedsResync が true になり、報告を送ったとき（TryTakeResync / NoteResyncSent）だけ解消する。</summary>
        void NoteEpoch(long epoch)
        {
            // 起動値が変わった（運営PCの再起動）か、初めて知った場合は、NeedsResync の規則で 1 回報告する
            _hostEpoch = epoch;
            _epochKnown = true;
        }

        /// <summary>状況報告を送りたい（間隔制限を通して TryTakeResync で送る）。ThParams の採用後、ClientNo の変化など。</summary>
        public void RequestResync() => _forceResync = true;

        /// <summary>毎フレーム呼ぶ。残り秒を数え、ホストの途絶を見る。</summary>
        public void Tick(double now)
        {
            if (_state.Value != RoundState.Waiting) _remaining.Value = (float)Remaining(now);

            if (now - _lastAlive >= _times.hostLostSeconds)
            {
                if (!_isHostLost.Value) _isHostLost.Value = true;
                if (_state.Value != RoundState.Waiting)
                {
                    // 回を中止して Waiting に戻る（F-FLOW-8）。その回の続きは受け付けない。
                    _abortedRoundId = RoundId;
                    WaitNextRound = true;
                    _forceResync = true;
                    Participants = new string[0];
                    Target = 0;
                    Result = null;
                    _deviceColors = new System.Collections.Generic.Dictionary<string, int>();
                    _ownColor.Value = -1;
                    _isHeld.Value = false;
                    _remaining.Value = 0f;
                    SetParticipant(false);
                    _state.Value = RoundState.Waiting;
                }
            }
        }

        /// <summary>再送要求（ThResync）が必要か。通し番号の不一致、知らない新しい回、パラメータ版の不一致。</summary>
        public bool NeedsResync
        {
            get
            {
                if (_abortedRoundId != -1 && _hostRoundId == _abortedRoundId && _hostRoundId != 0) return false;
                if (_forceResync) return true;
                if (_epochKnown && !(_reportedValid && _reportedEpoch == _hostEpoch)) return true;
                if (!_hostSeen) return false;
                if (_hostSeq != Seq) return true;
                if (_hostRoundId != RoundId) return true;
                if (!string.IsNullOrEmpty(HostParamsVersion) && HostParamsVersion != LocalParamsVersion) return true;
                return false;
            }
        }

        /// <summary>再送要求を今送るべきなら true（〔再送間隔〕より短い間隔では繰り返さない）。true を返したら送ったことにする。</summary>
        public bool TryTakeResync(double now)
        {
            if (!NeedsResync) return false;
            if (now - _lastResyncAt < _times.resendInterval) return false;
            MarkReported(now);
            return true;
        }

        /// <summary>起動時などに無条件で送る報告の時刻を記録する（直後の再送要求と重ねない）。</summary>
        public void NoteResyncSent(double now) => MarkReported(now);

        void MarkReported(double now)
        {
            _lastResyncAt = now;
            _forceResync = false;
            _reportedValid = _epochKnown;   // 起動値をまだ知らなければ「未知」で報告したことにする
            _reportedEpoch = _hostEpoch;
        }

        bool ComputeParticipant(RoundState state) =>
            state != RoundState.Waiting && !WaitNextRound && Array.IndexOf(Participants, _deviceId) >= 0;

        void SetParticipant(bool p)
        {
            if (_isParticipant.Value != p) _isParticipant.Value = p;
        }
    }
}
