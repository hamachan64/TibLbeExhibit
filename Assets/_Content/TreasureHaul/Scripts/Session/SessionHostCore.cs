using System;
using System.Collections.Generic;
using R3;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 運営PC（ホスト）の進行の中身。SessionHost（MonoBehaviour）は通信・時計・ファイルをここにつなぐだけ。
    /// 時刻は引数で渡す（now = 単調増加の秒、unixMs = RoundId 用）。
    /// </summary>
    public sealed class SessionHostCore : IDisposable
    {
        readonly IThTransport _tx;

        public DeviceRegistry Registry { get; } = new DeviceRegistry();
        public HostRoundMachine Machine { get; }
        public HostMessageGate Gate { get; }

        public ThParams Params { get; private set; }
        public string ParamsVersion { get; private set; } = "";
        public string ParamsJson { get; private set; } = "";
        public ThGameSettings Settings { get; private set; } = new ThGameSettings();
        public int OwnClientNo { get; set; } = -1;
        /// <summary>この運営PCアプリを起動した時刻（Unix ミリ秒）。再起動で変わる</summary>
        public long HostEpoch { get; }

        double _lastHeartbeatAt = double.NegativeInfinity;

        /// <summary>送り手の ClientNo から今の DeviceId を引く（本番は NetSync の GetDeviceIdByClientNo）。null なら引き直さない。</summary>
        readonly Func<int, string> _resolveDeviceId;
        // 端末ごとの送信の抑制（版の合わない端末と ThParams / ThResync が往復し続けないように）
        readonly Dictionary<string, double> _lastParamsSentAt = new Dictionary<string, double>();
        readonly Dictionary<string, double> _lastStateReplyAt = new Dictionary<string, double>();

        // 運営UIが購読する状態
        readonly ReactiveProperty<RoundState> _state = new ReactiveProperty<RoundState>(RoundState.Waiting);
        readonly ReactiveProperty<float> _remaining = new ReactiveProperty<float>(0f);
        readonly ReactiveProperty<long> _roundId = new ReactiveProperty<long>(0);
        readonly ReactiveProperty<int> _connectedCount = new ReactiveProperty<int>(0);
        readonly ReactiveProperty<int> _eligibleCount = new ReactiveProperty<int>(0);
        readonly ReactiveProperty<string> _paramsVersionRp = new ReactiveProperty<string>("");
        readonly ReactiveProperty<string[]> _warnings = new ReactiveProperty<string[]>(new string[0]);
        // #42
        readonly ReactiveProperty<bool> _held = new ReactiveProperty<bool>(false);
        readonly ReactiveProperty<bool> _canHold = new ReactiveProperty<bool>(false);
        readonly ReactiveProperty<int> _holdRemaining = new ReactiveProperty<int>(0);
        readonly ReactiveProperty<int> _target = new ReactiveProperty<int>(0);
        readonly ReactiveProperty<int> _total = new ReactiveProperty<int>(0);
        // #44
        readonly ReactiveProperty<DeviceRowData[]> _devices = new ReactiveProperty<DeviceRowData[]>(new DeviceRowData[0]);
        readonly Subject<RoundRecord> _roundFinished = new Subject<RoundRecord>();

        public ReadOnlyReactiveProperty<RoundState> State => _state;
        public ReadOnlyReactiveProperty<float> RemainingSeconds => _remaining;
        public ReadOnlyReactiveProperty<long> RoundId => _roundId;
        public ReadOnlyReactiveProperty<int> ConnectedCount => _connectedCount;
        public ReadOnlyReactiveProperty<int> EligibleCount => _eligibleCount;
        public ReadOnlyReactiveProperty<string> ParamsVersionText => _paramsVersionRp;
        public ReadOnlyReactiveProperty<string[]> Warnings => _warnings;
        /// <summary>保留中か</summary>
        public ReadOnlyReactiveProperty<bool> IsHeld => _held;
        /// <summary>今「保留」を押せるか（Tutorial 中・保留中でない・上限に余りがある）</summary>
        public ReadOnlyReactiveProperty<bool> CanHold => _canHold;
        /// <summary>保留の上限まであと何秒か（切り上げた整数。保留中は減っていく）</summary>
        public ReadOnlyReactiveProperty<int> HoldRemainingSeconds => _holdRemaining;
        /// <summary>目標額（Playing 以降。それより前は 0）</summary>
        public ReadOnlyReactiveProperty<int> Target => _target;
        /// <summary>回収した合計額</summary>
        public ReadOnlyReactiveProperty<int> Total => _total;
        /// <summary>端末一覧（端末番号の順。未登録は後ろに DeviceId 順）。内容が変わったときだけ流れる（F-OPS-1）</summary>
        public ReadOnlyReactiveProperty<DeviceRowData[]> Devices => _devices;
        /// <summary>回が終わったとき（Result → Waiting、または中断）に 1 回流れる、その回の記録（F-OPS-7。RoundLogger が書く）</summary>
        public Observable<RoundRecord> RoundFinished => _roundFinished;

        // お宝（#38）
        ThTreasureSetup _setup;
        HostItemTable _items;
        /// <summary>降ろす先を決める関数（VenueRoot ローカル → 降ろす先。本番は ThVenue の Raycast。null なら床 y=0）</summary>
        readonly Func<Vector3, Vector3> _placeSolver;
        long _itemsRound;
        double _lastItemsSentAt = double.NegativeInfinity;
        string[] _baseWarnings = new string[0];
        /// <summary>状態表（本編の間だけ。それ以外は null）</summary>
        public HostItemTable Items => _items;
        /// <summary>本編で作った配置の指紋（ホストと端末で同じことを突き合わせる用のログ）</summary>
        public string SpawnFingerprint { get; private set; } = "";
        /// <summary>回収した合計額（本編のお宝だけ。練習用は入れない）</summary>
        public int TotalAmount => _items?.Total ?? 0;

        // 練習（#43）
        HostItemTable _practice;
        readonly Dictionary<string, int> _colors = new Dictionary<string, int>();
        long _colorsRound;
        readonly Dictionary<string, double> _practiceDoneAt = new Dictionary<string, double>();
        /// <summary>練習用お宝の状態表（Tutorial の間だけ。それ以外は null）</summary>
        public HostItemTable PracticeItems => _practice;
        /// <summary>DeviceId → 色番号（Opening → Tutorial で割り当て、その回の結果まで持つ。Waiting・新しい「開始」で消える。#44 が使う）</summary>
        public IReadOnlyDictionary<string, int> ColorAssignments => _colors;
        /// <summary>練習用お宝を回収した端末の DeviceId → 時刻（Tick の now。1 回だけ記録。Waiting・新しい「開始」で消える。#44 の記録の材料）</summary>
        public IReadOnlyDictionary<string, double> PracticeCompletedAt => _practiceDoneAt;

        public SessionHostCore(IThTransport tx, ThParamsLoadResult initialParams, ThGameSettings settings, IEnumerable<string> settingsWarnings = null, long hostEpoch = 0, Func<int, string> resolveDeviceId = null, ThTreasureSetup treasureSetup = null, Func<Vector3, Vector3> placeSolver = null)
        {
            _placeSolver = placeSolver;
            _tx = tx;
            _resolveDeviceId = resolveDeviceId;
            HostEpoch = hostEpoch;
            _setup = treasureSetup;
            Registry.HostCatalogHash = treasureSetup?.CatalogHash ?? "";
            Machine = new HostRoundMachine(Registry, initialParams.Params.progress);
            Gate = new HostMessageGate(Machine, Registry);
            Adopt(initialParams, settings, settingsWarnings);
        }

        public void Dispose()
        {
            _state.Dispose(); _remaining.Dispose(); _roundId.Dispose(); _connectedCount.Dispose();
            _eligibleCount.Dispose(); _paramsVersionRp.Dispose(); _warnings.Dispose();
            _held.Dispose(); _canHold.Dispose(); _holdRemaining.Dispose(); _target.Dispose(); _total.Dispose();
            _devices.Dispose(); _roundFinished.Dispose();
        }

        void Adopt(ThParamsLoadResult p, ThGameSettings settings, IEnumerable<string> settingsWarnings)
        {
            Params = p.Params;
            ParamsVersion = p.Version;
            ParamsJson = JsonUtility.ToJson(p.Params);
            Settings = settings ?? new ThGameSettings();
            Registry.HostParamsVersion = p.Version;
            Registry.AutoConfirm = p.Params.operation.autoConfirmAlignment;   // 以後に登録・再接続する端末から効く
            Machine.TrySetTimes(p.Params.progress);
            var all = new List<string>(p.Warnings);
            if (settingsWarnings != null) all.AddRange(settingsWarnings);
            _baseWarnings = all.ToArray();
            UpdateWarnings();
            _paramsVersionRp.Value = p.Version;
        }

        /// <summary>待機中の「再読み込み」。待機中以外は何もしない。新しい版は全員へ配る。</summary>
        public bool ReloadParams(ThParamsLoadResult p, ThGameSettings settings, IEnumerable<string> settingsWarnings, double now)
        {
            if (Machine.State != RoundState.Waiting) return false;
            Adopt(p, settings, settingsWarnings);
            SendParams(-1);
            Refresh(now);
            return true;
        }

        /// <summary>配置とカタログの束を後から渡す（Environment シーンの読み込みを待つため）。待機中だけ。</summary>
        public bool SetTreasureSetup(ThTreasureSetup setup, double now)
        {
            if (Machine.State != RoundState.Waiting) return false;
            _setup = setup;
            Registry.HostCatalogHash = setup?.CatalogHash ?? "";
            Refresh(now);
            return true;
        }

        /// <summary>端末番号の対応表（treasure_haul_devices.json）を差し替える。並び順・練習の色の順に使う。読み込みの警告は運営UIの警告に出る</summary>
        public void SetDeviceNumbers(ThDeviceNumbers numbers, double now)
        {
            numbers = numbers ?? ThDeviceNumbers.Empty;
            Registry.Numbers = numbers;
            var w = new string[numbers.Warnings.Count];
            for (int i = 0; i < w.Length; i++) w[i] = numbers.Warnings[i];
            _numberWarnings = w;
            Refresh(now);
        }

        // ---- 端末の出入り ----

        // 端末の登録は ThResync（と ClientNo の付け替え）だけで行う。ステルスの運営PC・クローンを接続中に数えないため。

        public void OnClientDisconnected(int clientNo, double now)
        {
            _now = now;
            var deviceId = Registry.GetDeviceId(clientNo);
            Registry.Disconnect(clientNo);
            ReleaseItemsOf(deviceId, now);
            Refresh(now);
        }

        /// <summary>
        /// その端末の M の片手予約を解除し、持っている・判定役のお宝をその場にそっと置かせる（設計書 3.3、F-GRAB-8）。
        /// 切断と、回の途中の見学・位置合わせ未確認への切り替えで使う。本編は本編の表、練習中は練習用の表。参加人数・目標額は変えない。
        /// </summary>
        void ReleaseItemsOf(string deviceId, double now)
        {
            if (deviceId == null) return;
            if (_items != null && Machine.State == RoundState.Playing)
            {
                _items.ReleaseReservationsOf(deviceId);
                _items.ForceRestItemsOf(deviceId, now);
            }
            // 練習中に切れた端末の練習用お宝も、その場にそっと置かせる（置いた後は _practice.Tick が、範囲外ならつかめるに戻し、範囲内なら回収する）
            else if (_practice != null && Machine.State == RoundState.Tutorial)
            {
                _practice.ReleaseReservationsOf(deviceId);
                _practice.ForceRestItemsOf(deviceId, now);
            }
        }

        // ---- 見学・位置合わせ確認（F-OPS-4） ----

        /// <summary>
        /// 「見学」の切り替え。見学にした端末は、回の途中なら持っている・予約しているお宝を解放する。
        /// 参加人数・目標額は変えない（本編開始時の参加端末のまま）。見学の間、その端末のつかむ申請は <see cref="HostMessageGate"/> が捨てる。
        /// 参加に戻すと、本編開始時の参加端末に入っていた端末だけがすぐに再びつかめる【仮】（入っていなかった端末は次の回から）。
        /// 知らない端末なら false。
        /// </summary>
        public bool SetSpectator(string deviceId, bool spectator, double now)
        {
            if (Registry.Get(deviceId) == null) return false;
            _now = now;
            Registry.SetSpectator(deviceId, spectator);
            if (spectator) ReleaseItemsOf(deviceId, now);
            Refresh(now);
            return true;
        }

        /// <summary>「位置合わせ確認」の切り替え。未確認にした端末の扱いは <see cref="SetSpectator"/> の見学と同じ。知らない端末なら false。</summary>
        public bool SetAlignmentConfirmed(string deviceId, bool confirmed, double now)
        {
            if (Registry.Get(deviceId) == null) return false;
            _now = now;
            Registry.SetAlignmentConfirmed(deviceId, confirmed);
            if (!confirmed) ReleaseItemsOf(deviceId, now);
            Refresh(now);
            return true;
        }

        public bool ToggleSpectator(string deviceId, double now)
        {
            var d = Registry.Get(deviceId);
            return d != null && SetSpectator(deviceId, !d.Spectator, now);
        }

        public bool ToggleAlignmentConfirmed(string deviceId, double now)
        {
            var d = Registry.Get(deviceId);
            return d != null && SetAlignmentConfirmed(deviceId, !d.AlignmentConfirmed, now);
        }

        // ---- 運営の操作 ----

        public bool Start(double now, long unixMs, int seed)
        {
            if (!Machine.TryStart(now, unixMs, seed)) return false;
            _now = now;
            ClearPracticeRound();
            BeginRecord(now, unixMs);
            // 版が違う端末にはその場で ThParams を送り直す（N-7）
            foreach (var d in Registry.Devices)
                if (d.Connected && d.ParamsVersion != ParamsVersion && Due(_lastParamsSentAt, d.DeviceId, now)) SendParams(d.ClientNo);
            SendState(now);
            Refresh(now);
            return true;
        }

        /// <summary>「保留」（練習の延長）。Tutorial 中・上限に余りがあるときだけ。</summary>
        public bool Hold(double now)
        {
            if (!Machine.TryHold(now)) return false;
            SendState(now);
            Refresh(now);
            return true;
        }

        /// <summary>「保留を解除」。保留中だけ。</summary>
        public bool ReleaseHold(double now)
        {
            if (!Machine.TryReleaseHold(now)) return false;
            SendState(now);
            Refresh(now);
            return true;
        }

        /// <summary>「本編開始」。Tutorial 中なら保留の有無にかかわらず即 Playing（F-TUT-6）。</summary>
        public bool BeginPlay(double now)
        {
            if (!Machine.TryBeginPlay(now)) return false;
            SendState(now);
            Refresh(now);
            return true;
        }

        public bool Abort(double now)
        {
            var before = Machine.State;
            if (!Machine.Abort(now)) return false;
            _now = now;
            FinishRound(true, before, now);   // お宝の表・参加端末の控えが残っているうちに記録する
            SendState(now);
            Refresh(now);
            return true;
        }

        // ---- 毎フレーム ----

        public void Tick(double now)
        {
            _now = now;
            var before = Machine.State;
            bool changed = Machine.Tick(now);
            if (changed && before != RoundState.Waiting && Machine.State == RoundState.Waiting) FinishRound(false, before, now);   // Result → Waiting
            if (changed) SendState(now);
            if (now - _lastHeartbeatAt >= Params.progress.heartbeatInterval)
            {
                _lastHeartbeatAt = now;
                _tx.Send(Rpc.ThHeartbeat, ThMessages.Encode(Machine.RoundId, new ThHeartbeatMsg
                {
                    seq = Machine.Seq,
                    remaining = Machine.RemainingSeconds(now),
                    total = TotalAmount,
                    paramsVersion = ParamsVersion,
                    hostEpoch = HostEpoch,
                    held = Machine.IsHeld,
                }));
            }
            _items?.Tick(now, Machine.State == RoundState.Playing);   // そっと置かせたお宝を、〔置く時間〕の後につかめる／回収にする
            _practice?.Tick(now, Machine.State == RoundState.Tutorial);
            var sendTable = Machine.State == RoundState.Tutorial ? _practice : Machine.State == RoundState.Playing ? _items : null;
            if (sendTable != null && sendTable.Items.Count > 0 && now - _lastItemsSentAt >= Params.progress.itemsInterval)
            {
                _lastItemsSentAt = now;
                _tx.Send(Rpc.ThItems, ThMessages.Encode(Machine.RoundId, sendTable.BuildItems()));
            }
            Refresh(now);
        }

        // ---- 受信 ----

        public void OnRpc(Rpc rpc, int senderClientNo, string[] args, double now)
        {
            if (senderClientNo == OwnClientNo) return;   // 自分が送った RPC の戻り
            _now = now;
            RebindSender(senderClientNo);
            if (rpc == Rpc.ThResync) HandleResync(senderClientNo, args, now);
            else if (rpc == Rpc.ThStatus) HandleStatus(senderClientNo, args, now);
            else if (rpc == Rpc.ThGrab || rpc == Rpc.ThLift || rpc == Rpc.ThRelease || rpc == Rpc.ThRest || rpc == Rpc.ThCollect
                || rpc == Rpc.ThDamage || rpc == Rpc.ThPoses) HandleItemMessage(rpc, senderClientNo, args, now);
        }

        /// <summary>
        /// お宝のメッセージ（設計書 6.1）。RoundId・送り手が今の参加端末のときだけ。
        /// Playing 中は本編の表、Tutorial 中は練習用の表（ThGrab・ThRelease・ThRest・ThCollect・ThPoses だけ。ThDamage・ThLift は捨てる）。
        /// </summary>
        void HandleItemMessage(Rpc rpc, int sender, string[] args, double now)
        {
            HostItemTable table;
            if (Machine.State == RoundState.Playing) table = _items;
            else if (Machine.State == RoundState.Tutorial && rpc != Rpc.ThDamage && rpc != Rpc.ThLift) table = _practice;
            else return;
            if (table == null) return;
            string device;
            switch (rpc)
            {
                case Rpc.ThGrab:
                    if (!ThMessages.TryDecode(args, out long r1, out ThGrabMsg grab) || !Gate.Accept(r1, sender)) return;
                    device = Registry.GetDeviceId(sender);
                    table.OnGrab(device, sender, grab);
                    break;
                case Rpc.ThLift:
                    if (!ThMessages.TryDecode(args, out long r7, out ThLiftMsg lift) || !Gate.Accept(r7, sender)) return;
                    device = Registry.GetDeviceId(sender);
                    table.OnLift(device, sender, lift);
                    break;
                case Rpc.ThRelease:
                    if (!ThMessages.TryDecode(args, out long r2, out ThReleaseMsg rel) || !Gate.Accept(r2, sender)) return;
                    device = Registry.GetDeviceId(sender);
                    table.OnRelease(device, rel);
                    break;
                case Rpc.ThRest:
                    if (!ThMessages.TryDecode(args, out long r3, out ThRestMsg rest) || !Gate.Accept(r3, sender)) return;
                    device = Registry.GetDeviceId(sender);
                    table.OnRest(device, rest, now);
                    break;
                case Rpc.ThCollect:
                    if (!ThMessages.TryDecode(args, out long r4, out ThCollectMsg col) || !Gate.Accept(r4, sender)) return;
                    device = Registry.GetDeviceId(sender);
                    table.OnCollect(device, col, now);
                    break;
                case Rpc.ThDamage:
                    if (!ThMessages.TryDecode(args, out long r5, out ThDamageMsg dmg) || !Gate.Accept(r5, sender)) return;
                    device = Registry.GetDeviceId(sender);
                    table.OnDamage(device, dmg, now);
                    break;
                case Rpc.ThPoses:
                    // 位置は端末どうしで補間する。ホストは減額の免除の報告だけを読む
                    if (!ThMessages.TryDecode(args, out long r6, out ThPosesMsg poses) || !Gate.Accept(r6, sender)) return;
                    device = Registry.GetDeviceId(sender);
                    table.OnPoses(device, poses);
                    break;
            }
        }

        /// <summary>サーバーの再起動で ClientNo が入れ替わると、接続・切断イベントなしに対応が変わる。受け取るたびに引き直す。</summary>
        void RebindSender(int sender)
        {
            if (_resolveDeviceId == null) return;
            var known = Registry.GetDeviceId(sender);
            if (known == null) return;   // 未登録（ステルスなど）は登録しない。ThResync で登録する
            var actual = _resolveDeviceId(sender);
            if (!string.IsNullOrEmpty(actual) && actual != known) Registry.Connect(actual, sender);
        }

        bool Due(Dictionary<string, double> last, string deviceId, double now)
        {
            if (last.TryGetValue(deviceId, out var t) && now - t < Params.progress.resendInterval * 0.5) return false;
            last[deviceId] = now;
            return true;
        }

        /// <summary>端末の状況報告（手の認識・電池）。登録済みの端末だけ。ステルスの運営PC・クローンなど未登録の送り手は無視する</summary>
        void HandleStatus(int sender, string[] args, double now)
        {
            if (!ThMessages.TryDecode(args, out long _, out ThStatusMsg msg)) return;
            var deviceId = Registry.GetDeviceId(sender);
            if (deviceId == null) return;
            Registry.ReportStatus(deviceId, msg.hands, msg.battery, now);
            Refresh(now);
        }

        void HandleResync(int sender, string[] args, double now)
        {
            if (!ThMessages.TryDecode(args, out long knownRound, out ThResyncMsg msg)) return;
            if (string.IsNullOrEmpty(msg.deviceId)) return;
            // 接続イベントを取りこぼしていても、報告から端末を登録できる
            Registry.Connect(msg.deviceId, sender);
            Registry.ReportParamsVersion(msg.deviceId, msg.paramsVersion ?? "");
            Registry.ReportCatalogHash(msg.deviceId, msg.catalogHash ?? "");
            // 回の途中から入った端末は、その回だけ参加しない（古い報告で次の回を巻き込まないよう、今の回の報告だけ）
            if (msg.waitNextRound && Machine.State != RoundState.Waiting && knownRound == Machine.RoundId)
                Registry.SetWaitNextRound(msg.deviceId, true);
            if (msg.paramsVersion != ParamsVersion && Due(_lastParamsSentAt, msg.deviceId, now)) SendParams(sender);
            if (Due(_lastStateReplyAt, msg.deviceId, now)) SendState(now);
            Refresh(now);
        }

        // ---- 送信 ----

        void SendState(double now)
        {
            SyncItems();   // 遷移した直後の ThState に、色の割り当て・状態表が間に合うように
            var m = Machine;
            _tx.Send(Rpc.ThState, ThMessages.Encode(m.RoundId, new ThStateMsg
            {
                seq = m.Seq,
                state = (int)m.State,
                remaining = m.RemainingSeconds(now),
                seed = m.Seed,
                participants = new List<string>(m.Participants).ToArray(),
                settings = Settings,
                paramsVersion = ParamsVersion,
                hostEpoch = HostEpoch,
                tutorialColors = PracticeColors.ToArray(m.Participants, _colors),
                catalogHash = Registry.HostCatalogHash,
                held = m.IsHeld,
                target = TargetAmount,
                result = BuildResult(),
            }));
        }

        // ---- 目標額と結果（ホストが決める。F-FLOW-5） ----

        /// <summary>参加人数（1〜4 に丸める）の行の目標額。Playing / Result のときだけ。それ以外は 0。</summary>
        public int TargetAmount
        {
            get
            {
                var st = Machine.State;
                if (st != RoundState.Playing && st != RoundState.Result) return 0;
                return TargetFor(Machine.Participants.Count, Params);
            }
        }

        /// <summary>参加人数に対する目標額（<c>layout.rows[人数 - 1]</c>。人数は 1〜行数に丸める）</summary>
        public static int TargetFor(int participantCount, ThParams p)
        {
            var rows = p?.layout?.rows;
            if (rows == null || rows.Length == 0) return 0;
            int n = Mathf.Clamp(participantCount, 1, rows.Length);
            return rows[n - 1].targetAmount;
        }

        // 結果は Result に入った最初の ThState で確定し、その回の間は変えない
        long _resultRound;
        ThResultMsg _result;

        ThResultMsg BuildResult()
        {
            if (Machine.State != RoundState.Result)
            {
                _resultRound = 0;
                _result = null;
                return new ThResultMsg();
            }
            if (_result == null || _resultRound != Machine.RoundId)
            {
                int target = TargetAmount;
                int collected = TotalAmount;
                _result = new ThResultMsg { present = true, collected = collected, target = target, cleared = collected >= target };
                _resultRound = Machine.RoundId;
            }
            return _result;
        }

        void SendParams(int targetClientNo)
        {
            _tx.Send(Rpc.ThParams, ThMessages.Encode(Machine.RoundId, new ThParamsMsg
            {
                targetClientNo = targetClientNo,
                version = ParamsVersion,
                json = ParamsJson,
            }));
        }

        /// <summary>状態に合わせて状態表を作る・消す（本編に入ったら作り、待機に戻ったら消す）</summary>
        void SyncItems()
        {
            var st = Machine.State;
            // 練習: Tutorial に入ったら色を割り当て、練習用の表を作る。Tutorial を出たら表を捨てる（色は結果まで持つ）
            if (st == RoundState.Waiting) ClearPracticeRound();
            else if (st == RoundState.Tutorial && _colorsRound != Machine.RoundId)
            {
                _colors.Clear();
                foreach (var kv in PracticeColors.Assign(Machine.Participants, Registry.Numbers.Compare)) _colors[kv.Key] = kv.Value;   // 端末番号の順（未登録は後ろに DeviceId 順）
                _colorsRound = Machine.RoundId;
            }
            if (st == RoundState.Tutorial && _practice == null && _setup != null && _setup.PracticeSlots.Count > 0 && _colorsRound == Machine.RoundId)
            {
                BuildPractice();
                _lastItemsSentAt = double.NegativeInfinity;
            }
            else if (st != RoundState.Tutorial && _practice != null)
            {
                _practice = null;
                _lastItemsSentAt = double.NegativeInfinity;
            }
            if (st == RoundState.Waiting)
            {
                _items = null;
                _itemsRound = 0;
                _lastItemsSentAt = double.NegativeInfinity;
                SpawnFingerprint = "";
            }
            else if (st == RoundState.Playing && (_items == null || _itemsRound != Machine.RoundId) && _setup != null)
            {
                var spawn = TreasureSpawner.Spawn(Machine.Seed, Machine.Participants.Count, Params, _setup.Points, _setup.Catalog);
                SpawnFingerprint = spawn.Fingerprint();
                _items = new HostItemTable(spawn.Items, _setup.CollectBounds, (rpc, dto) => _tx.Send(rpc, ThMessages.EncodeObject(Machine.RoundId, dto)),
                    deviceId => Registry.Get(deviceId)?.ClientNo ?? -1, Params.treasure, Params.grip.placeSeconds);
                if (_placeSolver != null) _items.PlaceSolver = _placeSolver;
                _items.Log = Debug.Log;
                _items.Confirmed = OnItemConfirmed;
                _itemsRound = Machine.RoundId;
                _lastItemsSentAt = double.NegativeInfinity;
            }
        }

        /// <summary>色の割り当て・練習用の表・練習完了の記録を消す（待機に戻ったとき・新しい「開始」）</summary>
        void ClearPracticeRound()
        {
            _colors.Clear();
            _colorsRound = 0;
            _practiceDoneAt.Clear();
            _practice = null;
        }

        /// <summary>割り当てのある色ごとに、練習用お宝（番号 100＋色番号、S、練習お宝の価値、その色の TutorialSlot の位置）を 1 つ作る</summary>
        void BuildPractice()
        {
            var spawned = new List<SpawnedTreasure>();
            var deviceOf = new Dictionary<int, string>();
            foreach (var kv in _colors) deviceOf[kv.Value] = kv.Key;
            for (int color = 0; color < ThPractice.MaxColors && color < _setup.PracticeSlots.Count; color++)
            {
                if (!deviceOf.ContainsKey(color)) continue;
                var slot = _setup.PracticeSlots[color];
                spawned.Add(new SpawnedTreasure
                {
                    Index = ThPractice.IndexOf(color), Kind = TreasureKind.S, Value = Params.practice.practiceValue,
                    PointIndex = -1, LocalPosition = slot.LocalPosition, YawDegrees = slot.YawDegrees, CatalogIndex = -1,
                });
            }
            _practice = new HostItemTable(spawned, _setup.CollectBounds, (rpc, dto) => _tx.Send(rpc, ThMessages.EncodeObject(Machine.RoundId, dto)),
                deviceId => Registry.Get(deviceId)?.ClientNo ?? -1, Params.treasure, Params.grip.placeSeconds, practiceTable: true);
            if (_placeSolver != null) _practice.PlaceSolver = _placeSolver;
            foreach (var it in _practice.Items) it.PracticeDevice = deviceOf[ThPractice.ColorOf(it.Index)];
            _practice.PracticeCollected = (device, at) =>
            {
                if (string.IsNullOrEmpty(device) || _practiceDoneAt.ContainsKey(device)) return;
                _practiceDoneAt[device] = at;   // 1 回だけ
                _record?.NotePracticeDone(device, at);
            };
        }

        string[] _numberWarnings = new string[0];
        readonly SortedDictionary<string, string> _runtimeWarnings = new SortedDictionary<string, string>(StringComparer.Ordinal);

        /// <summary>実行中に起きた警告（記録 CSV を書けなかったなど）を運営UIの警告に足す。message が null なら消す。key ごとに 1 件</summary>
        public void SetRuntimeWarning(string key, string message)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (string.IsNullOrEmpty(message)) _runtimeWarnings.Remove(key);
            else _runtimeWarnings[key] = message;
            UpdateWarnings();
        }

        /// <summary>
        /// 運営UIの警告を組み直す（起動時の警告 + 端末番号の警告 + 実行中の警告 + カタログのハッシュが合わない端末）。内容が変わったときだけ差し替える
        /// </summary>
        void UpdateWarnings()
        {
            var all = new List<string>(_baseWarnings);
            all.AddRange(_numberWarnings);
            foreach (var w in _runtimeWarnings.Values) all.Add(w);
            foreach (var d in Registry.Devices)
            {
                if (!d.Connected || d.CatalogHash == Registry.HostCatalogHash) continue;
                all.Add($"お宝カタログのハッシュが違うので参加できません: {d.DeviceId}（端末 {d.CatalogHash} / 運営PC {Registry.HostCatalogHash}）");
            }
            var cur = _warnings.Value;
            bool same = cur.Length == all.Count;
            for (int i = 0; same && i < cur.Length; i++) same = cur[i] == all[i];
            if (same) return;
            _warnings.Value = all.ToArray();
        }

        // ---- 1 回ごとの記録（F-OPS-7。ホストが確定した出来事だけから作る） ----

        RoundRecord _record;
        double _now;

        void BeginRecord(double now, long unixMs)
        {
            _record = new RoundRecord
            {
                RoundId = Machine.RoundId,
                StartedAt = DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToLocalTime(),   // 運営PCのローカル時刻（CSV の日付はこれで決める）
                StartNow = now,
                Mode = Settings.mode.ToString(),
                Extras = RoundRecord.ExtrasOf(Settings),
                GripStyle = Settings.gripStyle.ToString(),
                ParamsVersion = ParamsVersion,
            };
            _record.SetParticipants(Machine.Participants, Registry.Numbers.Compare, Registry.Numbers.NumberOf);
        }

        /// <summary>ホストの状態表が確定した出来事（本編のお宝だけ。練習用の表には付けない）を記録へ渡す</summary>
        void OnItemConfirmed(HostItemEvent e)
        {
            var rec = _record;
            if (rec == null || e.Practice) return;
            switch (e.Kind)
            {
                case HostItemEventKind.Grabbed:
                case HostItemEventKind.Lifted:
                    rec.NoteFirstHold(e.Device, _now);
                    break;
                case HostItemEventKind.Damaged:
                    rec.NoteDamage(e.Device, e.Amount);
                    break;
                case HostItemEventKind.Collected:
                    rec.NoteCollect(e.Device, e.Amount, _now);
                    break;
            }
        }

        /// <summary>回の終わり（Result → Waiting なら aborted=false、中断なら true）。記録を確定して <see cref="RoundFinished"/> へ流す</summary>
        void FinishRound(bool aborted, RoundState stateAtEnd, double now)
        {
            var r = _record;
            if (r == null) return;
            _record = null;
            r.Aborted = aborted;
            r.AbortedState = aborted ? stateAtEnd : RoundState.Waiting;
            r.WasHeld = Machine.WasHeld;
            r.HeldSeconds = Machine.HeldSeconds(now);
            r.CollectedAmount = TotalAmount;
            r.TargetAmount = r.ReachedPlaying ? TargetFor(r.ParticipantCount, Params) : 0;   // 本編に入らなかった回の目標額は 0（その回には目標が無かった）
            r.DamageTotal = _items?.DamageTotal ?? 0;
            r.SetColors(_colors);
            _roundFinished.OnNext(r);
        }

        void Refresh(double now)
        {
            SyncItems();
            UpdateWarnings();
            if (_record != null && Machine.State == RoundState.Playing) _record.ReachedPlaying = true;
            if (_record != null && Machine.State != RoundState.Waiting)
                _record.SetParticipants(Machine.Participants, Registry.Numbers.Compare, Registry.Numbers.NumberOf);   // Opening → Tutorial・Tutorial → Playing で減った分を追う
            var rows = DeviceRowBuilder.Build(Registry, Machine.State, _practiceDoneAt, now, Params.progress.statusInterval, Params.operation.batteryWarningPercent);
            if (!DeviceRowData.SameList(_devices.Value, rows)) _devices.Value = rows;
            _state.Value = Machine.State;
            _remaining.Value = Machine.RemainingSeconds(now);
            _roundId.Value = Machine.RoundId;
            _held.Value = Machine.IsHeld;
            _canHold.Value = Machine.CanHold(now);
            _holdRemaining.Value = Mathf.CeilToInt(Machine.HoldRemainingSeconds(now));
            _target.Value = TargetAmount;
            _total.Value = TotalAmount;
            _connectedCount.Value = Registry.ConnectedCount;
            _eligibleCount.Value = Registry.EligibleCount;
        }
    }
}
