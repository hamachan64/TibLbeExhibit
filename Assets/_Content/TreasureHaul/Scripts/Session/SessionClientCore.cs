using System;
using System.Collections.Generic;
using R3;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>端末の追従の中身。SessionClient（MonoBehaviour）は通信・時計をここにつなぐだけ。</summary>
    public sealed class SessionClientCore : IDisposable
    {
        readonly string _deviceId;
        readonly IThTransport _tx;
        readonly ThParams _defaults;
        readonly ThEnvironmentInfo _env;

        public ClientRoundTracker Tracker { get; private set; }
        public ThParams Params { get; private set; }
        int _ownClientNo = -1;
        /// <summary>自分の ClientNo。サーバーの再起動などで変わることがあり、変わったら状況報告を送り直す。</summary>
        public int OwnClientNo
        {
            get => _ownClientNo;
            set
            {
                if (_ownClientNo == value) return;
                bool changed = _ownClientNo > 0 && value > 0;
                _ownClientNo = value;
                if (changed) Tracker.RequestResync();
            }
        }

        public const int MaxWarnings = 20;
        readonly List<string> _warnings = new List<string>();
        public IReadOnlyList<string> Warnings => _warnings;

        void AddWarning(string w)
        {
            if (_warnings.Count < MaxWarnings && !_warnings.Contains(w)) _warnings.Add(w);
        }

        // お宝（#38）
        readonly ThTreasureSetup _setup;
        readonly ReactiveProperty<ClientItemTable> _items = new ReactiveProperty<ClientItemTable>(null);
        readonly ReactiveProperty<int> _total = new ReactiveProperty<int>(0);
        long _itemsRound;
        bool _itemsPractice;
        double _lastPoseSentAt = double.NegativeInfinity;
        IDisposable _stateSub;

        /// <summary>今の回のお宝の表（Tutorial では練習用お宝の表、Playing に入ると本編の表に替わる。待機に戻るまで。回の途中から入った端末は null）</summary>
        public ReadOnlyReactiveProperty<ClientItemTable> Items => _items;
        /// <summary>荷台に回収した合計額（ThCollected と ThHeartbeat から。待機に戻ったら 0）</summary>
        public ReadOnlyReactiveProperty<int> Total => _total;
        /// <summary>配置の指紋（ホストの SpawnFingerprint と突き合わせるログ用）</summary>
        public string SpawnFingerprint { get; private set; } = "";
        public ThTreasureSetup TreasureSetup => _setup;

        public SessionClientCore(string deviceId, IThTransport tx, ThParams defaults, ThEnvironmentInfo env, double now, ThTreasureSetup treasureSetup = null)
        {
            _deviceId = deviceId;
            _tx = tx;
            _defaults = defaults;
            _env = env;
            _setup = treasureSetup;
            var r = ThParamsLoader.Load(defaults, null, env);
            Params = r.Params;
            Tracker = new ClientRoundTracker(deviceId, r.Params.progress, now) { LocalParamsVersion = r.Version };
            _stateSub = Tracker.State.Subscribe(s => OnTrackerState(s));
        }

        public void Dispose()
        {
            _stateSub?.Dispose();
            _items.Value?.Dispose();
            _items.Dispose();
            _total.Dispose();
            Tracker.Dispose();
        }

        /// <summary>状態に合わせて、お宝の表を作る・止める・消す</summary>
        void OnTrackerState(RoundState s)
        {
            if (s == RoundState.Waiting)
            {
                var old = _items.Value;
                _items.Value = null;      // 購読者（TreasureClient）が見た目を消した後で捨てる
                old?.Dispose();
                _itemsRound = 0;
                _itemsPractice = false;
                SpawnFingerprint = "";
                _total.Value = 0;
                return;
            }
            // 待機を取りこぼして別の回に入ったときは、Playing 以外でも前の回の表を消す
            if (_itemsRound != 0 && _itemsRound != Tracker.RoundId)
            {
                var stale = _items.Value;
                _items.Value = null;
                stale?.Dispose();
                _itemsRound = 0;
                _itemsPractice = false;
                SpawnFingerprint = "";
                _total.Value = 0;
            }
            if (_setup == null || Tracker.WaitNextRound) return;
            if (s == RoundState.Tutorial)
            {
                // 練習用お宝の表（割り当てのある色ごとに 1 つ。TutorialSlot の無い環境では作らない）
                if (_items.Value == null && _setup.PracticeSlots.Count > 0) BuildPracticeTable();
            }
            else if (s == RoundState.Playing && (_items.Value == null || _itemsRound != Tracker.RoundId || _itemsPractice))
            {
                var spawn = TreasureSpawner.Spawn(Tracker.Seed, Tracker.Participants.Length, Params, _setup.Points, _setup.Catalog);
                SpawnFingerprint = spawn.Fingerprint();
                foreach (var w in spawn.Warnings) Debug.LogWarning("[SessionClient] 配置の警告: " + w);
                var old = _items.Value;   // 練習用の表。新しい表に替えて見た目を作り直させた後で捨てる（練習用お宝は全端末から消える）
                _itemsRound = Tracker.RoundId;
                _itemsPractice = false;
                _total.Value = 0;
                _items.Value = new ClientItemTable(_tx, () => Tracker.RoundId, () => OwnClientNo, Params, _setup, spawn.Items);
                old?.Dispose();
            }
            else if (s == RoundState.Result)
            {
                if (_itemsPractice)
                {
                    // 本編の ThState を取りこぼして Tutorial から Result に飛んだ: 練習用の表は結果の間も残さず捨てる
                    var old = _items.Value;
                    _items.Value = null;
                    old?.Dispose();
                    _itemsRound = 0;
                    _itemsPractice = false;
                }
                else _items.Value?.Freeze();
            }
        }

        void BuildPracticeTable()
        {
            var colors = new SortedSet<int>(Tracker.DeviceColors.Values);
            var spawned = new List<SpawnedTreasure>();
            foreach (int color in colors)
            {
                if (color < 0 || color >= _setup.PracticeSlots.Count) continue;
                var slot = _setup.PracticeSlots[color];
                spawned.Add(new SpawnedTreasure
                {
                    Index = ThPractice.IndexOf(color), Kind = TreasureKind.S, Value = Params.practice.practiceValue,
                    PointIndex = -1, LocalPosition = slot.LocalPosition, YawDegrees = slot.YawDegrees, CatalogIndex = -1,
                });
            }
            _itemsRound = Tracker.RoundId;
            _itemsPractice = true;
            _total.Value = 0;
            _items.Value = new ClientItemTable(_tx, () => Tracker.RoundId, () => OwnClientNo, Params, _setup, spawned, practice: true)
            {
                PracticeOwnIndex = OwnPracticeIndex,
            };
        }

        /// <summary>自分の練習用お宝の番号（色が無ければ -1）</summary>
        public int OwnPracticeIndex => Tracker.OwnColor.CurrentValue >= 0 ? ThPractice.IndexOf(Tracker.OwnColor.CurrentValue) : -1;

        /// <summary>
        /// 表がつかめる局面かを更新する（毎フレーム）。本編の表: 参加端末で Playing 中 / 練習用の表: 参加端末で Tutorial 中。
        /// 練習用の表でつかめるのは、自分に割り当てられた練習用お宝だけ。
        /// </summary>
        public void UpdateGrabPhase(ClientItemTable items)
        {
            if (items == null) return;
            var state = Tracker.State.CurrentValue;
            items.ParticipantPlaying = Tracker.IsParticipant.CurrentValue && state == (items.IsPractice ? RoundState.Tutorial : RoundState.Playing);
            if (items.IsPractice) items.PracticeOwnIndex = OwnPracticeIndex;
        }

        /// <summary>持っているお宝の位置を送る間隔（〔位置の送信間隔〕）が来たか</summary>
        public bool TryTakePoseSlot(double now)
        {
            if (now - _lastPoseSentAt < Params.grip.poseSendInterval) return false;
            _lastPoseSentAt = now;
            return true;
        }

        /// <summary>動かしているお宝の位置を、1 つの RPC にまとめて送る（無ければ送らない）。32KB を超えないよう件数を絞る</summary>
        public void SendPoses(IReadOnlyList<ThPoseEntry> poses)
        {
            if (poses == null || poses.Count == 0 || Tracker.RoundId == 0) return;
            // 1 件 約 100 バイト × 200 件 = 約 20KB で、RPC の上限 32KB（設計書 6章）に収まる
            const int MaxEntries = 200;
            int n = System.Math.Min(poses.Count, MaxEntries);
            var arr = new ThPoseEntry[n];
            for (int i = 0; i < n; i++) arr[i] = poses[i];
            _tx.Send(Rpc.ThPoses, ThMessages.Encode(Tracker.RoundId, new ThPosesMsg { poses = arr }));
        }

        /// <summary>NetSync の準備ができたとき 1 回。状況報告（ThResync）を送る。</summary>
        public void OnReady(double now) => SendResync(now);

        public void OnRpc(Rpc rpc, int senderClientNo, string[] args, double now)
        {
            if (senderClientNo == OwnClientNo) return;   // 自分が送った RPC の戻り
            switch (rpc)
            {
                case Rpc.ThState:
                    if (ThMessages.TryDecode(args, out long sr, out ThStateMsg st)) Tracker.OnState(sr, st, now);
                    break;
                case Rpc.ThHeartbeat:
                    if (ThMessages.TryDecode(args, out long hr, out ThHeartbeatMsg hb))
                    {
                        Tracker.OnHeartbeat(hr, hb, now);
                        if (hr == Tracker.RoundId && hr != 0 && hb.total > _total.Value) _total.Value = hb.total;
                    }
                    break;
                case Rpc.ThGrabbed:
                    if (TryItemMsg(args, out ThGrabbedMsg gm)) _items.Value.OnGrabbed(gm, now);
                    break;
                case Rpc.ThLifted:
                    if (TryItemMsg(args, out ThLiftedMsg lm)) _items.Value.OnLifted(lm, now);
                    break;
                case Rpc.ThGrabDenied:
                    if (TryItemMsg(args, out ThGrabDeniedMsg dm)) _items.Value.OnDenied(dm, now);
                    break;
                case Rpc.ThFreed:
                    if (TryItemMsg(args, out ThFreedMsg fm)) _items.Value.OnFreed(fm);
                    break;
                case Rpc.ThForceRest:
                    if (TryItemMsg(args, out ThForceRestMsg frm)) _items.Value.OnForceRest(frm);
                    break;
                case Rpc.ThCollected:
                    if (TryItemMsg(args, out ThCollectedMsg cm))
                    {
                        if (cm.total > _total.Value) _total.Value = cm.total;   // 先に合計を更新（購読者が見たときに合っているように）
                        _items.Value.OnCollected(cm);
                    }
                    break;
                case Rpc.ThDamaged:
                    if (TryItemMsg(args, out ThDamagedMsg dgm)) _items.Value.OnDamaged(dgm);
                    break;
                case Rpc.ThItems:
                    if (TryItemMsg(args, out ThItemsMsg im)) _items.Value.OnItems(im, now);
                    break;
                case Rpc.ThPoses:
                    if (TryItemMsg(args, out ThPosesMsg pm2)) _items.Value.OnPoses(pm2, now);
                    break;
                case Rpc.ThParams:
                    if (ThMessages.TryDecode(args, out long _, out ThParamsMsg pm)) HandleParams(pm, now);
                    break;
            }
        }

        /// <summary>お宝のメッセージを読む。今の回のもので、お宝の表があるときだけ true</summary>
        bool TryItemMsg<T>(string[] args, out T dto) where T : class
        {
            dto = null;
            if (_items.Value == null) return false;
            if (!ThMessages.TryDecode(args, out long roundId, out dto)) return false;
            return roundId == Tracker.RoundId && roundId != 0;
        }

        void HandleParams(ThParamsMsg pm, double now)
        {
            if (pm.targetClientNo != -1 && pm.targetClientNo != OwnClientNo) return;
            var r = ThParamsLoader.Load(_defaults, pm.json, _env);
            Params = r.Params;
            Tracker.UpdateTimes(r.Params.progress);
            Tracker.LocalParamsVersion = r.Version;
            foreach (var w in r.Warnings) AddWarning(w);
            if (r.Version != pm.version) AddWarning($"ホストの版 {pm.version} と採用した版 {r.Version} が違います");
            Tracker.RequestResync();   // 新しい版を報告する（間隔制限を通して Tick で送る）
        }

        public void Tick(double now)
        {
            Tracker.Tick(now);
            var items = _items.Value;
            if (items != null)
            {
                UpdateGrabPhase(items);
                items.Tick(now);
            }
            if (Tracker.TryTakeResync(now)) SendResync(now);
        }

        void SendResync(double now)
        {
            Tracker.NoteResyncSent(now);
            _tx.Send(Rpc.ThResync, ThMessages.Encode(Tracker.RoundId, new ThResyncMsg
            {
                deviceId = _deviceId,
                seq = Tracker.Seq,
                paramsVersion = Tracker.LocalParamsVersion,
                waitNextRound = Tracker.WaitNextRound,
                catalogHash = _setup?.CatalogHash ?? "",
            }));
        }
    }
}
