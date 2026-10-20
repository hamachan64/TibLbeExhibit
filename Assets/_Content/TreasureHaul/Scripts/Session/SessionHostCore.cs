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
        /// <summary>回収した合計額</summary>
        public int TotalAmount => _items?.Total ?? 0;

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
        }

        void Adopt(ThParamsLoadResult p, ThGameSettings settings, IEnumerable<string> settingsWarnings)
        {
            Params = p.Params;
            ParamsVersion = p.Version;
            ParamsJson = JsonUtility.ToJson(p.Params);
            Settings = settings ?? new ThGameSettings();
            Registry.HostParamsVersion = p.Version;
            Machine.TrySetTimes(p.Params.progress);
            var all = new List<string>(p.Warnings);
            if (settingsWarnings != null) all.AddRange(settingsWarnings);
            _baseWarnings = all.ToArray();
            _warnings.Value = _baseWarnings;
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

        // ---- 端末の出入り ----

        // 端末の登録は ThResync（と ClientNo の付け替え）だけで行う。ステルスの運営PC・クローンを接続中に数えないため。

        public void OnClientDisconnected(int clientNo, double now)
        {
            var deviceId = Registry.GetDeviceId(clientNo);
            Registry.Disconnect(clientNo);
            // 切断した端末の M の片手予約を解除し、持っている・判定役のお宝をその場にそっと置かせる（設計書 3.3、F-GRAB-8）
            // TODO(#44): 見学への切り替え（運営PCの UI）でも同じく ReleaseReservationsOf と ForceRestItemsOf を呼ぶ
            if (_items != null && Machine.State == RoundState.Playing)
            {
                _items.ReleaseReservationsOf(deviceId);
                _items.ForceRestItemsOf(deviceId, now);
            }
            Refresh(now);
        }

        // ---- 運営の操作 ----

        public bool Start(double now, long unixMs, int seed)
        {
            if (!Machine.TryStart(now, unixMs, seed)) return false;
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
            if (!Machine.Abort(now)) return false;
            SendState(now);
            Refresh(now);
            return true;
        }

        // ---- 毎フレーム ----

        public void Tick(double now)
        {
            if (Machine.Tick(now)) SendState(now);
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
            if (_items != null && _items.Items.Count > 0
                && (Machine.State == RoundState.Tutorial || Machine.State == RoundState.Playing)
                && now - _lastItemsSentAt >= Params.progress.itemsInterval)
            {
                _lastItemsSentAt = now;
                _tx.Send(Rpc.ThItems, ThMessages.Encode(Machine.RoundId, _items.BuildItems()));
            }
            Refresh(now);
        }

        // ---- 受信 ----

        public void OnRpc(Rpc rpc, int senderClientNo, string[] args, double now)
        {
            if (senderClientNo == OwnClientNo) return;   // 自分が送った RPC の戻り
            RebindSender(senderClientNo);
            if (rpc == Rpc.ThResync) HandleResync(senderClientNo, args, now);
            else if (rpc == Rpc.ThGrab || rpc == Rpc.ThLift || rpc == Rpc.ThRelease || rpc == Rpc.ThRest || rpc == Rpc.ThCollect
                || rpc == Rpc.ThDamage || rpc == Rpc.ThPoses) HandleItemMessage(rpc, senderClientNo, args, now);
        }

        /// <summary>お宝のメッセージ（設計書 6.1）。RoundId・送り手が今の参加端末・本編（Playing）中のときだけ。</summary>
        void HandleItemMessage(Rpc rpc, int sender, string[] args, double now)
        {
            if (_items == null || Machine.State != RoundState.Playing) return;
            string device;
            switch (rpc)
            {
                case Rpc.ThGrab:
                    if (!ThMessages.TryDecode(args, out long r1, out ThGrabMsg grab) || !Gate.Accept(r1, sender)) return;
                    device = Registry.GetDeviceId(sender);
                    _items.OnGrab(device, sender, grab);
                    break;
                case Rpc.ThLift:
                    if (!ThMessages.TryDecode(args, out long r7, out ThLiftMsg lift) || !Gate.Accept(r7, sender)) return;
                    device = Registry.GetDeviceId(sender);
                    _items.OnLift(device, sender, lift);
                    break;
                case Rpc.ThRelease:
                    if (!ThMessages.TryDecode(args, out long r2, out ThReleaseMsg rel) || !Gate.Accept(r2, sender)) return;
                    device = Registry.GetDeviceId(sender);
                    _items.OnRelease(device, rel);
                    break;
                case Rpc.ThRest:
                    if (!ThMessages.TryDecode(args, out long r3, out ThRestMsg rest) || !Gate.Accept(r3, sender)) return;
                    device = Registry.GetDeviceId(sender);
                    _items.OnRest(device, rest);
                    break;
                case Rpc.ThCollect:
                    if (!ThMessages.TryDecode(args, out long r4, out ThCollectMsg col) || !Gate.Accept(r4, sender)) return;
                    device = Registry.GetDeviceId(sender);
                    _items.OnCollect(device, col);
                    break;
                case Rpc.ThDamage:
                    if (!ThMessages.TryDecode(args, out long r5, out ThDamageMsg dmg) || !Gate.Accept(r5, sender)) return;
                    device = Registry.GetDeviceId(sender);
                    _items.OnDamage(device, dmg, now);
                    break;
                case Rpc.ThPoses:
                    // 位置は端末どうしで補間する。ホストは減額の免除の報告だけを読む
                    if (!ThMessages.TryDecode(args, out long r6, out ThPosesMsg poses) || !Gate.Accept(r6, sender)) return;
                    device = Registry.GetDeviceId(sender);
                    _items.OnPoses(device, poses);
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
                tutorialColors = new int[0],
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
                _itemsRound = Machine.RoundId;
                _lastItemsSentAt = double.NegativeInfinity;
            }
        }

        /// <summary>カタログのハッシュが合わない端末の警告を、起動時の警告に足す（運営UIに出る）</summary>
        void UpdateCatalogWarnings()
        {
            List<string> extra = null;
            foreach (var d in Registry.Devices)
            {
                if (!d.Connected || d.CatalogHash == Registry.HostCatalogHash) continue;
                (extra ?? (extra = new List<string>())).Add($"お宝カタログのハッシュが違うので参加できません: {d.DeviceId}（端末 {d.CatalogHash} / 運営PC {Registry.HostCatalogHash}）");
            }
            int n = extra?.Count ?? 0;
            var cur = _warnings.Value;
            bool same = cur.Length == _baseWarnings.Length + n;
            if (same)
            {
                for (int i = 0; i < n && same; i++) same = cur[_baseWarnings.Length + i] == extra[i];
            }
            if (same) return;
            var all = new List<string>(_baseWarnings);
            if (extra != null) all.AddRange(extra);
            _warnings.Value = all.ToArray();
        }

        void Refresh(double now)
        {
            SyncItems();
            UpdateCatalogWarnings();
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
