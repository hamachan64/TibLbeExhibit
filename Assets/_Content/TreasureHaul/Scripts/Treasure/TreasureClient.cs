using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 端末側のお宝の生成・同期をまとめる配線（UserClient）。SessionClient の状態に従い、ClientItemTable のイベントを見て
    /// 見た目（Treasure・値札・演出・表示板）と物理を動かす。判断は ClientItemTable にある。
    /// 本編に入るとお宝を VenueRoot の下に生成し、待機に戻る（結果の終わり・中断）と全部消す（購読・Tween も）。
    /// </summary>
    public sealed class TreasureClient : MonoBehaviour
    {
        [SerializeField] SessionClient _session;
        [SerializeField] HandGrabber _left;
        [SerializeField] HandGrabber _right;
        [SerializeField, Tooltip("値札のフォント（日本語フォント）")] TMP_FontAsset _labelFont;
        [SerializeField, Tooltip("値札の高さ（お宝の上 m）。お宝の高さ 0.2〜0.5m の頭上に出るよう 0.3m")] float _labelHeight = 0.3f;

        const string TreasureLayerName = "Treasure";
        const string GrabZoneLayerName = "TreasureGrabZone";
        /// <summary>高い壁のレイヤー。ビームを止める（低い瓦礫は別レイヤーで止めない）</summary>
        public const string WallLayerName = "ThWall";
        const string HandBoneLayerName = FingertipTouchCollider.HandBoneLayerName;
        /// <summary>VenueRoot の床よりこれ以上下に落ちたら、その場の床の高さに戻す（m）</summary>
        const float FloorKillDepth = 0.5f;

        sealed class View
        {
            public Treasure Treasure;
            public PriceLabel Label;
            /// <summary>今付けているヒビ（お宝の子）と、その段階（ぶつけた回数）</summary>
            public GameObject Crack;
            public int CrackStage;
            /// <summary>練習用お宝の上の記号・「あなたの」（練習用以外は null）</summary>
            public PriceLabel Tag;
        }

        /// <summary>M を両手で持っている間の状態（見た目の側。判断は ClientItemTable）</summary>
        sealed class TwoHand
        {
            public Treasure Treasure;
            /// <summary>予約した手（1 本目）と 2 本目の手</summary>
            public HandGrabber Reserve, Second;
            /// <summary>お宝が追う目標（位置 = 手に合わせる点、向き = お宝の向き）。両手の中点か、猶予の間は残った手についていく点</summary>
            public Transform Target;
            public bool Grace;
            /// <summary>猶予の間についていく手と、外れた瞬間の「残った手 → 手に合わせる点・お宝」の相対</summary>
            public HandGrabber Remaining;
            public Vector3 GraceRelPos;
            public Quaternion GraceRelRot = Quaternion.identity;
        }

        /// <summary>「-NNG」の赤。ぶつけて値段が下がったことが一目でわかる色</summary>
        static readonly Color DamageColor = new Color(1f, 0.2f, 0.2f);
        /// <summary>初めての一文の色。赤い「-NNG」と区別して読める白</summary>
        static readonly Color NoteColor = Color.white;
        /// <summary>値札の上に重ねない高さ（m）。値札（高さ 0.3m）の上に、一文 → 「-NNG」の順に積む</summary>
        const float NoteOffset = 0.1f, AmountOffset = 0.18f;
        const float AmountFontSize = 1.0f, NoteFontSize = 0.45f;
        /// <summary>練習用お宝の記号・「あなたの」: 値札（高さ 0.3m）の上に重ならない高さ（m）と文字の大きさ</summary>
        const float TagOffset = 0.22f, TagFontSize = 0.7f;
        /// <summary>「両手で持とう」: 値札の上に重ねない高さ（m）・文字の大きさ・表示時間（秒）。読める長さの数秒（見た目の調整値なのでコードの定数）</summary>
        const float HintOffset = 0.12f, HintFontSize = 0.45f, HintSeconds = 4f;
        /// <summary>砕ける演出の片付けまでの秒数（プレハブが自分で消えない場合の保険）</summary>
        const float ShatterLifetime = 5f;

        SessionClientCore _core;
        ClientItemTable _table;
        ThVenue _venue;
        ThContentSet _content;
        CompositeDisposable _tableSubs;
        readonly Dictionary<int, View> _views = new Dictionary<int, View>();
        readonly Dictionary<int, HandGrabber> _holders = new Dictionary<int, HandGrabber>();
        readonly List<ThPoseEntry> _poseBuffer = new List<ThPoseEntry>();
        readonly List<DamagePopup> _popups = new List<DamagePopup>();
        readonly Dictionary<int, TwoHand> _twoHands = new Dictionary<int, TwoHand>();
        readonly List<KeyValuePair<int, TwoHand>> _twoHandBuf = new List<KeyValuePair<int, TwoHand>>();
        readonly TwoHandHint _hint = new TwoHandHint();
        DamagePopup _hintPopup;
        int _hintItem = -1;
        HandGrabber _grabbing;
        ThParams _configuredFor;

        int _treasureLayer = -1, _grabZoneLayer = -1, _wallLayer = -1, _handBoneLayer = -1;
        // アバターのコライダーとお宝の本体コライダーを個別に Physics.IgnoreCollision で外す（アバターのレイヤーは変えない）
        readonly List<Collider> _avatarColliders = new List<Collider>();
        readonly HashSet<Collider> _avatarSet = new HashSet<Collider>();
        readonly List<Collider> _colliderBuf = new List<Collider>();
        /// <summary>新しいアバターが現れたかの保険の確認間隔（秒）。普段は接続イベントで拾う</summary>
        const double AvatarFallbackScanSeconds = 10.0;
        double _nextAvatarScan;
        Transform _head;
        System.IDisposable _boardSub;

        public ThGripParams Grip => _core != null ? _core.Params.grip : new ThGripParams();
        public bool CanGrab => _table != null && _table.ParticipantPlaying && !_table.Frozen;
        public ClientItemTable Table => _table;
        public int ViewCount => _views.Count;
        public Treasure GetTreasure(int index) => _views.TryGetValue(index, out var v) ? v.Treasure : null;
        /// <summary>〔飛びの判定〕（m）。手の目標が 1 フレームでこれ以上動いたら飛び</summary>
        public float JumpDistance => _core != null ? _core.Params.treasure.jumpDistance : new ThTreasureParams().jumpDistance;
        public HandGrabber Left => _left;
        public HandGrabber Right => _right;

        static double Now => Time.realtimeSinceStartupAsDouble;

        void Awake()
        {
            _treasureLayer = LayerMask.NameToLayer(TreasureLayerName);
            _grabZoneLayer = LayerMask.NameToLayer(GrabZoneLayerName);
            _wallLayer = LayerMask.NameToLayer(WallLayerName);
            _handBoneLayer = LayerMask.NameToLayer(HandBoneLayerName);
            if (_treasureLayer < 0 || _grabZoneLayer < 0 || _handBoneLayer < 0)
                Debug.LogWarning($"[TreasureClient] レイヤーがありません（{TreasureLayerName}={_treasureLayer} {GrabZoneLayerName}={_grabZoneLayer} {WallLayerName}={_wallLayer} {HandBoneLayerName}={_handBoneLayer}）");
            // F-DMG-1: お宝は手（HandBone）とは当たらない。アバターとは生成時・アバター出現時に個別に外す
            if (_treasureLayer >= 0 && _handBoneLayer >= 0) Physics.IgnoreLayerCollision(_treasureLayer, _handBoneLayer, true);
        }

        void Update()
        {
            if (_session == null) return;
            if (_session.Core != _core) Bind(_session.Core);
            if (_core == null) return;

            double now = Now;
            ConfigureHands();
            UpdateBeamColor();
            if (now >= _nextAvatarScan) { _nextAvatarScan = now + AvatarFallbackScanSeconds; RefreshAvatarColliders(); }
            if (_table == null) return;

            // 結果に入った後は位置も ThRest・ThCollect も送らない
            if (_table.Frozen) return;
            _core.UpdateGrabPhase(_table);   // 本編の表は Playing 中、練習用の表は Tutorial 中だけつかめる
            var venueRoot = _venue.Root;
            var grip = Grip;
            UpdateTwoHands(now);
            UpdateTwoHandHint();
            _poseBuffer.Clear();
            bool slotChecked = false, slotDue = false;   // 送る枠が来たフレームだけ詰める（枠は、送る物があるときだけ消費する）

            foreach (var kv in _views)
            {
                var v = kv.Value;
                var m = v.Treasure.Model;
                if (m.Gone) continue;
                if (m.Mine)
                {
                    if (m.State == ItemState.Falling) UpdateFalling(v, now, grip, venueRoot);
                    else if (m.State == ItemState.Hovering) ObserveHover(v, now);
                    else if (m.State == ItemState.Held && m.Approved) CheckDetach(v, now);
                    if (m.Approved && !m.PendingRest
                        && (m.State == ItemState.Held || m.State == ItemState.Flying || m.State == ItemState.Falling || m.State == ItemState.Hovering || m.State == ItemState.Placing))
                    {
                        if (!slotChecked) { slotChecked = true; slotDue = _core.TryTakePoseSlot(now); }
                        if (slotDue)
                        {
                            var tr = v.Treasure.transform;
                            _poseBuffer.Add(new ThPoseEntry { item = m.Index, pos = tr.localPosition, rot = tr.localRotation, exempt = v.Treasure.IsDamageExempt });
                        }
                    }
                }
                else if (m.Interp.HasSamples && (m.State == ItemState.Held || m.State == ItemState.Falling || m.State == ItemState.Flying))
                {
                    if (m.Interp.TrySample(now, grip.interpDelay, grip.interpDelay, out var p, out var r))
                    {
                        v.Treasure.transform.localPosition = p;
                        v.Treasure.transform.localRotation = r;
                    }
                }
            }

            if (_poseBuffer.Count > 0) _core.SendPoses(_poseBuffer);
        }

        void LateUpdate()
        {
            if (_views.Count == 0) return;
            foreach (var kv in _views)
            {
                var t = kv.Value.Treasure;
                if (t == null) continue;
                // 自分が予約中の M は、片手で握ったことがわかるように「狙っている」光のままにする
                bool want = _table != null && !_table.Frozen
                    && ((_left != null && _left.Aimed == t) || (_right != null && _right.Aimed == t) || (t.Model != null && ClientItemTable.IsOwnReservation(t.Model)));
                if (t.Aimed != want) t.SetAimed(want);
            }
        }

        /// <summary>見失い待ち: 持っていた手（M は両手）の様子を ClientItemTable に渡す。戻ったら握りの強さで、持ち続ける／そっと置くが決まる</summary>
        void ObserveHover(View v, double now)
        {
            var m = v.Treasure.Model;
            if (m.Kind == TreasureKind.M)
            {
                if (!_twoHands.TryGetValue(m.Index, out var th) || th.Reserve == null || th.Second == null || th.Reserve.Tracker == null || th.Second.Tracker == null) return;
                _table.OnMHoverObserved(m.Index,
                    th.Reserve.Tracker.IsTracked.CurrentValue, th.Reserve.Tracker.Strength.CurrentValue,
                    th.Second.Tracker.IsTracked.CurrentValue, th.Second.Tracker.Strength.CurrentValue, now);
                return;
            }
            if (!_holders.TryGetValue(m.Index, out var g) || g == null || g.Tracker == null) return;   // 手が無ければ〔見失い待ち〕で置く
            _table.OnHoverObserved(m.Index, g.Tracker.IsTracked.CurrentValue, g.Tracker.Strength.CurrentValue, now);
        }

        /// <summary>持っているお宝の手に合わせる点と、手の目標との距離を ClientItemTable に渡す（〔離れたら置く距離〕以上ならそっと置く）</summary>
        void CheckDetach(View v, double now)
        {
            var t = v.Treasure;
            var m = t.Model;
            Transform target = null;
            if (m.Kind == TreasureKind.M) { if (_twoHands.TryGetValue(m.Index, out var th)) target = th.Target; }
            else if (_holders.TryGetValue(m.Index, out var g) && g != null) target = g.Anchor;
            if (target == null) return;
            _table.CheckDetach(m.Index, Vector3.Distance(target.position, t.HoldPointWorld), t.IsFlying, now);
        }

        /// <summary>HandGrabber から: 手の目標が飛んだ。その手で持っているお宝の〔補正の無判定時間〕を始める</summary>
        public void OnHandJumped(HandGrabber grabber)
        {
            var t = grabber.Held;
            if (t == null || _table == null || t.Model == null) return;
            _table.NoteJump(t.Model.Index, Now);
        }

        void UpdateFalling(View v, double now, ThGripParams grip, Transform venueRoot)
        {
            var t = v.Treasure;
            var m = t.Model;
            var local = t.transform.localPosition;
            if (local.y < -FloorKillDepth)
            {
                // 床より明らかに下に落ちた: その場の床の高さに戻して Resting
                local.y = 0f;
                t.StopInPlace();
                t.transform.localPosition = local;
                _table.NoteSettled(m.Index, local, t.transform.localRotation, Now);
                return;
            }
            if (_table.InCollectZone(local)) { _table.NoteInCollectZone(m.Index, now); return; }
            if (t.PollSettled(now, _core.Params.treasure.stopSeconds))
            {
                t.StopInPlace();
                _table.NoteSettled(m.Index, t.transform.localPosition, t.transform.localRotation, now);
            }
        }

        // ---- 配線 ----

        void Bind(SessionClientCore core)
        {
            _core = core;
            _boardSub?.Dispose();
            _boardSub = null;
            if (core == null) return;
            _content = _session.Content;
            _venue = ThVenue.Instance;
            core.Items.Subscribe(OnTableChanged).AddTo(this);
            var nm = Styly.NetSync.NetSyncManager.Instance;
            if (nm != null) nm.OnAvatarConnected.AsObservable().Subscribe(_ => RefreshAvatarsLater().Forget()).AddTo(this);
            RefreshAvatarColliders();
            var board = _venue != null ? _venue.Board : null;
            if (board != null && _content != null)
            {
                _boardSub = core.Total.CombineLatest(core.Tracker.State, (total, state) => (total, state)).Subscribe(x =>
                {
                    board.SetText(_content.text.Format(ThTextId.BoardTotalFormat, x.total));
                    board.SetVisible(x.state == RoundState.Tutorial || x.state == RoundState.Playing || x.state == RoundState.Result);
                });
            }
            core.HandCountProvider = TrackedHandCount;   // 状況報告（ThStatus）で運営UIの端末一覧に出す
            _configuredFor = null;
            var fx = _content?.fx;
            var beamMat = fx?.Find(ThFxId.Beam)?.material;
            if (_left != null) _left.Initialize(this, _left.Tracker, _left.GetComponent<LineRenderer>(), beamMat, _grabZoneLayer, _wallLayer);
            if (_right != null) _right.Initialize(this, _right.Tracker, _right.GetComponent<LineRenderer>(), beamMat, _grabZoneLayer, _wallLayer);
        }

        /// <summary>手の認識の台数（追跡できていて手のひらが取れている手の数。0〜2）</summary>
        int TrackedHandCount()
        {
            int n = 0;
            if (IsHandTracked(_left)) n++;
            if (IsHandTracked(_right)) n++;
            return n;
        }

        static bool IsHandTracked(HandGrabber h) =>
            h != null && h.Tracker != null && h.Tracker.IsTracked.CurrentValue && h.Tracker.HasPalm;

        void ConfigureHands()
        {
            if (ReferenceEquals(_configuredFor, _core.Params)) return;
            _configuredFor = _core.Params;
            var g = _core.Params.grip;
            foreach (var h in new[] { _left, _right })
                if (h != null && h.Tracker != null) h.Tracker.Configure(g.gripThreshold, g.releaseThreshold, g.releaseHoldSeconds, g.trackingLossGrace);
        }

        /// <summary>
        /// 両手のビームの色。自分の色があれば Tutorial〜Result の間その色、待機中と色のない端末は既定の色（palette の defaultBeamColor）。
        /// </summary>
        void UpdateBeamColor()
        {
            var palette = _content != null ? _content.palette : null;
            if (palette == null) return;
            var state = _core.Tracker.State.CurrentValue;
            bool active = state == RoundState.Tutorial || state == RoundState.Playing || state == RoundState.Result;
            var color = active ? palette.BeamColor(_core.Tracker.OwnColor.CurrentValue) : palette.defaultBeamColor;
            if (_left != null) _left.SetBeamColor(color);
            if (_right != null) _right.SetBeamColor(color);
        }

        void OnTableChanged(ClientItemTable table)
        {
            ClearViews();
            _table = table;
            if (table == null) return;
            _tableSubs = new CompositeDisposable();
            _venue = ThVenue.Instance;
            BuildViews(table);
            table.Events.Subscribe(OnItemEvent).AddTo(_tableSubs);
            Debug.Log($"[TreasureClient] spawned items={table.Items.Count} fingerprint={_core.SpawnFingerprint} catalogHash={_core.TreasureSetup?.CatalogHash}");
        }

        void BuildViews(ClientItemTable table)
        {
            var catalog = _content != null ? _content.treasures : null;
            if (catalog == null || _venue == null) { Debug.LogWarning("[TreasureClient] カタログまたは ThVenue がありません"); return; }
            var fx = _content.fx;
            var outline = fx?.Find(ThFxId.AimOutline)?.material;
            foreach (var m in table.Items)
            {
                GameObject prefab;
                if (table.IsPractice) prefab = catalog.practicePrefab;   // 練習用お宝（TH_Practice）。色は MaterialPropertyBlock で塗る
                else if (m.CatalogIndex >= 0 && m.CatalogIndex < catalog.entries.Count) prefab = catalog.entries[m.CatalogIndex].prefab;
                else prefab = null;
                if (prefab == null) { Debug.LogWarning($"[TreasureClient] お宝 {m.Index} の見た目がありません"); continue; }
                var go = Instantiate(prefab, _venue.Root);
                go.name = table.IsPractice ? $"Practice_{m.Index}" : $"Treasure_{m.Index}_{m.Kind}";
                var t = go.GetComponent<Treasure>();
                t.Initialize(m, Grip, outline, _treasureLayer, _grabZoneLayer);
                t.ConfigureDamage(_core.Params.treasure.speedSmoothFrames, _core.Params.treasure.redamageInterval, _core.Params.grip.poseSendInterval, _core.Params.grip.followSpeedMax);
                t.PlaceKinematic(m.HomePos, m.HomeRot);
                t.Impacts.Subscribe(imp => OnImpact(t, imp)).AddTo(_tableSubs);
                var label = PriceLabel.Create(_venue.Root, t.LabelAnchor, _labelHeight, _content.text, _labelFont);
                label.SetState(m.Revealed, m.Value);
                var view = new View { Treasure = t, Label = label };
                if (table.IsPractice) DecoratePractice(view, m);
                _views[m.Index] = view;
                IgnoreAvatars(t);
            }
        }

        /// <summary>練習用お宝: その色で塗り、上に色の記号（●▲■★）、自分のお宝にはさらに「あなたの」を出す。値札は本編と同じ（「？」→ 持った瞬間に値段）</summary>
        void DecoratePractice(View v, ClientItem m)
        {
            var slot = _content.palette != null ? _content.palette.Get(ThPractice.ColorOf(m.Index)) : null;
            if (slot == null) return;   // 色の一覧が無ければ塗らない・記号も出さない
            v.Treasure.PaintBody(slot.practiceColor);
            bool mine = m.Index == _table.PracticeOwnIndex;
            string symbol = $"<color=#{ColorUtility.ToHtmlStringRGB(slot.practiceColor)}>{slot.symbol}</color>";
            var tag = PriceLabel.Create(_venue.Root, v.Treasure.LabelAnchor, _labelHeight + TagOffset, _content.text, _labelFont);
            tag.SetFixedText(mine ? _content.text.Get(ThTextId.TagYours) + "\n" + symbol : symbol, TagFontSize);
            v.Tag = tag;
        }

        void ClearViews()
        {
            _tableSubs?.Dispose();
            _tableSubs = null;
            foreach (var kv in _views)
            {
                if (kv.Value.Treasure != null) Destroy(kv.Value.Treasure.gameObject);   // ヒビはお宝の子なので一緒に消える
                if (kv.Value.Label != null) Destroy(kv.Value.Label.gameObject);
                if (kv.Value.Tag != null) Destroy(kv.Value.Tag.gameObject);
            }
            _views.Clear();
            foreach (var p in _popups) if (p != null) Destroy(p.gameObject);   // 表示中の「-NNG」・一文（Tween は SetLink で止まる）
            _popups.Clear();
            _holders.Clear();
            foreach (var th in _twoHands.Values) if (th.Target != null) Destroy(th.Target.gameObject);
            _twoHands.Clear();
            if (_hintPopup != null) Destroy(_hintPopup.gameObject);
            _hintPopup = null;
            _hintItem = -1;
            _hint.Reset();
            if (_left != null) { _left.Held = null; _left.ClearAim(); }
            if (_right != null) { _right.Held = null; _right.ClearAim(); }
            _table = null;
        }

        void OnDestroy()
        {
            _boardSub?.Dispose();
            ClearViews();   // VenueRoot の下に作ったお宝・値札も消す
        }

        // ---- HandGrabber から ----

        public bool IsTargetable(Treasure t, ThHand hand, int zoneIndex) => _table != null && t != null && t.Model != null && _table.IsTargetFor(t.Model, hand, zoneIndex);

        public bool TryGrab(HandGrabber grabber, Treasure t, int zoneIndex)
        {
            if (!CanGrab || t == null) return false;
            if (t.Model.Kind == TreasureKind.M) return TryGrabM(grabber, t, zoneIndex);
            _grabbing = grabber;
            bool ok = _table.TryBeginGrab(t.Model.Index, Now);
            _grabbing = null;
            if (!ok) return false;
            _holders[t.Model.Index] = grabber;
            grabber.Held = t;
            return true;
        }

        /// <summary>M: 予約中でなければ片手予約、予約中なら 2 本目の手で両手成立</summary>
        bool TryGrabM(HandGrabber grabber, Treasure t, int zoneIndex)
        {
            int index = t.Model.Index;
            if (ClientItemTable.IsOwnReservation(t.Model))
            {
                if (!_twoHands.TryGetValue(index, out var th)) return false;
                th.Second = grabber;   // LiftStarted のイベントが手を見られるように、先に覚える
                if (!_table.TryBeginLift(index, grabber.Hand, zoneIndex, Now)) { th.Second = null; return false; }
                grabber.Held = t;
                return true;
            }
            var created = new TwoHand { Treasure = t, Reserve = grabber };
            _twoHands[index] = created;
            if (!_table.TryBeginLock(index, grabber.Hand, zoneIndex, Now)) { _twoHands.Remove(index); return false; }
            grabber.Held = t;
            return true;
        }

        public void OnHandReleased(HandGrabber grabber, bool lost)
        {
            var t = grabber.Held;
            if (t == null || _table == null) { grabber.Held = null; return; }
            if (t.Model.Kind == TreasureKind.M)
            {
                // 予約・猶予・見失い待ち・取りやめの判断は ClientItemTable。結果はイベントで受ける
                _table.OnMHandReleased(t.Model.Index, grabber.Hand, lost, Now);
                // 見失い待ち（Hovering）の間は、戻ってまた見失った手も含めて両手の Held を外さない
                if (t.Model.State != ItemState.Hovering) grabber.Held = null;
                // 承認前に 2 本目が開いて待ちが取り消されたら、古い手を覚えたままにしない
                if (_twoHands.TryGetValue(t.Model.Index, out var rel) && rel.Second == grabber && t.Model.SecondHand < 0) rel.Second = null;
                return;
            }
            _table.OnHandReleased(t.Model.Index, Now, lost);
            if (t.Model.State == ItemState.Hovering) return;   // 見失い待ち: その場で待つ。手は外さない（ビームを出さず、別の物をつかまない）
            grabber.Held = null;
            _holders.Remove(t.Model.Index);
        }

        /// <summary>猶予の間に外れた手・予約中に見失った手が握り直した。戻れたら true（ビームの当て直しは要らない）</summary>
        public bool TryRegrip(HandGrabber grabber)
        {
            if (_table == null) return false;
            foreach (var kv in _twoHands)
            {
                var th = kv.Value;
                if (th.Reserve != grabber && th.Second != grabber) continue;
                if (!_table.OnMHandRegripped(kv.Key, grabber.Hand, Now)) continue;
                grabber.Held = th.Treasure;
                return true;
            }
            return false;
        }

        // ---- M の両手 ----

        /// <summary>両手の目標を更新する。猶予の間は残った手についていく、予約した手を見失っている間は、戻ったときの開き具合を見る</summary>
        void UpdateTwoHands(double now)
        {
            if (_twoHands.Count == 0) return;
            _twoHandBuf.Clear();
            _twoHandBuf.AddRange(_twoHands);   // 予約の解除でイベントが _twoHands を書き換えるので、写しを回す
            foreach (var kv in _twoHandBuf)
            {
                var th = kv.Value;
                var m = th.Treasure != null ? th.Treasure.Model : null;
                if (m != null && m.State == ItemState.Hovering) continue;   // 見失い待ち: 止めたまま。手の目標は戻ってから合わせる
                if (m == null || th.Target == null) { if (m != null) ObserveReserveLostWait(kv.Key, th, m); continue; }
                if (th.Grace)
                {
                    var a = th.Remaining != null ? th.Remaining.Anchor : null;
                    if (a == null) continue;
                    th.Target.SetPositionAndRotation(a.TransformPoint(th.GraceRelPos), a.rotation * th.GraceRelRot);
                }
                else if (th.Reserve != null && th.Second != null && m.SecondZone >= 0 && m.LockZone >= 0)
                {
                    var axis = th.Treasure.GrabZoneLocalPosition(m.SecondZone) - th.Treasure.GrabZoneLocalPosition(m.LockZone);
                    var pa = th.Reserve.Anchor.position;
                    var pb = th.Second.Anchor.position;
                    // axis はお宝のローカルのベクトル。お宝は今の向きから、目標の向きへ追従で寄っていく
                    if (TwoHandPose.TrySolve(pa, pb, axis, out var rot))
                        th.Target.SetPositionAndRotation(TwoHandPose.Midpoint(pa, pb), rot);
                }
                ObserveReserveLostWait(kv.Key, th, m);
            }
        }

        /// <summary>予約した手を見失っている間に、手が開いた状態で戻ったら予約を解除する（握った状態で戻れば OnGripChanged が拾う）</summary>
        void ObserveReserveLostWait(int index, TwoHand th, ClientItem m)
        {
            if (!m.ReserveLostWait || th.Reserve == null || th.Reserve.Tracker == null) return;
            var tr = th.Reserve.Tracker;
            if (tr.IsTracked.CurrentValue && tr.Strength.CurrentValue < Grip.releaseThreshold)
                _table.OnReserveHandReturnedOpen(index, th.Reserve.Hand);
        }

        void EnsureTarget(TwoHand th)
        {
            if (th.Target != null) return;
            var go = new GameObject("TwoHandTarget_" + th.Treasure.Model.Index);
            go.transform.SetParent(transform, false);
            th.Target = go.transform;
            // 目標の姿勢を決める前に原点へ飛ばないよう、今のお宝の手に合わせる点・向きで初期化する
            go.transform.SetPositionAndRotation(th.Treasure.HoldPointWorld, th.Treasure.transform.rotation);
        }

        // ---- 「両手で持とう」（F-ITEM-5） ----

        void UpdateTwoHandHint()
        {
            // 出ている間: その M が消えたら表示も消す
            if (_hintPopup != null && _hintItem >= 0)
            {
                var hm = _table.Get(_hintItem);
                if (hm == null || hm.Gone) { Destroy(_hintPopup.gameObject); _hintPopup = null; _hintItem = -1; }
            }
            if (_hint.Shown || _venue == null || _content == null) return;
            if (_head == null) { var cam = Camera.main; if (cam == null) return; _head = cam.transform; }
            var headLocal = _venue.Root.InverseTransformPoint(_head.position);
            int idx = _hint.Poll(_table.Items, headLocal, Grip.beamDistance, _table.ParticipantPlaying);
            if (idx < 0 || !_views.TryGetValue(idx, out var v) || v.Treasure == null) return;
            _hintItem = idx;
            _hintPopup = DamagePopup.Spawn(_venue.Root, v.Treasure.LabelAnchor, _labelHeight + HintOffset,
                _content.text.Get(ThTextId.HintTwoHands), _labelFont, NoteColor, HintFontSize, 0f, HintSeconds);
        }

        public Vector3 EstimateShoulder(Handedness hand)
        {
            if (_head == null)
            {
                var cam = Camera.main;
                if (cam == null) return Vector3.zero;
                _head = cam.transform;
            }
            var g = Grip;
            var yaw = Quaternion.Euler(0f, _head.eulerAngles.y, 0f);
            float side = hand == Handedness.Left ? -g.shoulderSide : g.shoulderSide;
            return _head.position + yaw * new Vector3(side, -g.shoulderDown, 0f);
        }

        // ---- イベント ----

        void OnItemEvent(ItemEvent e)
        {
            if (!_views.TryGetValue(e.Item.Index, out var v)) return;
            var t = v.Treasure;
            var m = e.Item;
            switch (e.Kind)
            {
                case ItemEventKind.GrabStarted:
                    if (_grabbing != null) t.FlyToHand(_grabbing.Anchor, Grip.flyTime);
                    break;
                case ItemEventKind.LiftStarted:
                    if (_twoHands.TryGetValue(m.Index, out var lifting))
                    {
                        EnsureTarget(lifting);
                        UpdateTwoHands(Now);   // 飛び始める前に目標を今の両手に合わせる
                        t.FlyToHand(lifting.Target, Grip.flyTime, true);
                    }
                    break;
                case ItemEventKind.RemoteLocked:
                    t.PlaceKinematic(m.RestPos, m.RestRot);   // 他の人の予約: 置き位置のまま
                    break;
                case ItemEventKind.Unreserved:
                    ReleaseHolder(m.Index);   // お宝は動いていないので、手から外すだけ
                    break;
                case ItemEventKind.GraceStarted:
                    if (_twoHands.TryGetValue(m.Index, out var gr)) BeginGrace(gr, m);
                    break;
                case ItemEventKind.GraceEnded:
                    if (_twoHands.TryGetValue(m.Index, out var back)) { back.Grace = false; back.Remaining = null; }
                    break;
                case ItemEventKind.Hovered:
                    t.StopInPlace();   // その場で止める（飛ぶ途中なら飛ぶのをやめる）。手は外さない
                    if (_twoHands.TryGetValue(m.Index, out var hov))
                    {
                        hov.Grace = false;
                        hov.Remaining = null;
                        if (hov.Reserve != null) hov.Reserve.Held = t;   // 両手とも別の物をつかまない
                        if (hov.Second != null) hov.Second.Held = t;
                    }
                    break;
                case ItemEventKind.HoverResumed:
                    ResumeFromHover(m, t);
                    break;
                case ItemEventKind.PlaceStarted:
                    PlaceDown(m, t);
                    break;
                case ItemEventKind.ForceRested:
                    ReleaseHolder(m.Index);   // 自分が持っていたなら手から外す
                    t.ReturnTo(m.RestPos, m.RestRot, Grip.placeSeconds);
                    break;
                case ItemEventKind.Approved:
                    Debug.Log($"[TreasureClient] grab approved item={m.Index} value={m.Value} gen={m.Gen}");
                    break;
                case ItemEventKind.Reverted:
                    ReleaseHolder(m.Index);
                    t.ReturnTo(m.RestPos, m.RestRot, Grip.flyTime);
                    break;
                case ItemEventKind.Dropped:
                    ReleaseHolder(m.Index);
                    t.BeginFalling();
                    break;
                case ItemEventKind.OwnershipLost:
                    ReleaseHolder(m.Index);
                    t.StopInPlace();
                    break;
                case ItemEventKind.RemoteHeld:
                    t.StopInPlace();
                    break;
                case ItemEventKind.Freed:
                    ReleaseHolder(m.Index);
                    t.PlaceKinematic(m.RestPos, m.RestRot);
                    break;
                case ItemEventKind.Revealed:
                    v.Label.SetState(m.Revealed, m.Value);
                    break;
                case ItemEventKind.Discovered:
                    PlayDiscover(t);
                    break;
                case ItemEventKind.Collected:
                    Debug.Log($"[TreasureClient] collected item={m.Index} total={_core.Total.CurrentValue}");
                    ReleaseHolder(m.Index);
                    if (t != null) Destroy(t.gameObject);
                    if (v.Label != null) Destroy(v.Label.gameObject);
                    if (v.Tag != null) Destroy(v.Tag.gameObject);
                    break;
                case ItemEventKind.Frozen:
                    ReleaseHolder(m.Index);
                    // そっと置いている途中なら、空中で止めず降ろす先に置く（見た目だけ。メッセージは送らない）
                    if (m.State == ItemState.Placing && m.Mine && m.PlaceTargetSet) t.PlaceKinematic(m.PlacePos, m.PlaceRot);
                    else if (m.State == ItemState.Placing && !m.Mine) t.PlaceKinematic(m.RestPos, m.RestRot);
                    else t.StopInPlace();
                    break;
                case ItemEventKind.Damaged:
                    OnDamagedEvent(v, e);
                    break;
                case ItemEventKind.HitsSynced:
                    // ThDamaged を取りこぼした: ヒビと値札だけ合わせる（「-NNG」と音は出さない）
                    v.Label.SetState(m.Revealed, m.Value);
                    SetCrack(v, m.Hits);
                    break;
                case ItemEventKind.Destroyed:
                    OnDestroyedEvent(v, e, true);
                    break;
                case ItemEventKind.DestroyedQuiet:
                    OnDestroyedEvent(v, e, false);
                    break;
            }
        }

        /// <summary>見失い待ちから手が戻って持ち続ける: 握りの判定を握っている状態へ戻し、手（M は両手の目標）への追従を再開する</summary>
        void ResumeFromHover(ClientItem m, Treasure t)
        {
            if (m.Kind == TreasureKind.M)
            {
                if (!_twoHands.TryGetValue(m.Index, out var th) || th.Target == null) return;
                // 戻ってまた見失った手の Held が外れていても、両手とも M にしておく
                if (th.Reserve != null) { th.Reserve.Held = t; if (th.Reserve.Tracker != null) th.Reserve.Tracker.ResumeGripping(); }
                if (th.Second != null) { th.Second.Held = t; if (th.Second.Tracker != null) th.Second.Tracker.ResumeGripping(); }
                UpdateTwoHands(Now);   // 追従を始める前に、目標を今の両手に合わせる
                t.BeginFollow(th.Target);
                return;
            }
            if (!_holders.TryGetValue(m.Index, out var g) || g == null) return;
            if (g.Tracker != null) g.Tracker.ResumeGripping();
            t.BeginFollow(g.Anchor);
        }

        /// <summary>
        /// そっと置く: 降ろす先（真下の床・荷台・瓦礫の上。向きは Y 回転だけ残して立てる）を決めて ClientItemTable に渡し、
        /// 手から外して〔置く時間〕で降ろす（kinematic。減額しない）。
        /// </summary>
        void PlaceDown(ClientItem m, Treasure t)
        {
            var rot = ThPlaceSolver.Upright(t.transform.localRotation);
            var dest = ThPlaceSolver.SolveClear(_venue != null ? _venue.Root : null, t.transform.localPosition, rot, t.BodyColliders, t.transform);
            _table.SetPlaceTarget(m.Index, dest, rot);
            ReleaseHolder(m.Index);
            t.ReturnTo(dest, rot, Grip.placeSeconds);
        }

        // ---- 減額（設計書 5章・9章） ----

        /// <summary>自分が動かしているお宝がぶつかった。数える衝撃以上なら ThDamage をホストへ送る（判断は ClientItemTable）</summary>
        void OnImpact(Treasure t, TreasureImpact imp)
        {
            if (_table == null || _venue == null || t == null || t.Model == null) return;
            var local = _venue.Root.InverseTransformPoint(imp.WorldPoint);
            _table.TryReportDamage(t.Model.Index, imp.OtherIndex, local, imp.Speed, Now);
        }

        void OnDamagedEvent(View v, ItemEvent e)
        {
            var m = e.Item;
            var d = e.Damage;
            v.Label.SetState(m.Revealed, m.Value);   // 値段が「？」のお宝は「？」のまま
            SetCrack(v, m.Hits);
            if (d == null) return;
            ThAudioPlayer.Play(_content.audio, ThSoundId.Damage, _venue.Root.TransformPoint(d.pos));
            ShowDamageTexts(v.Treasure, d, e.ShowNote);
        }

        void OnDestroyedEvent(View v, ItemEvent e, bool withEffects)
        {
            var m = e.Item;
            var t = v.Treasure;
            if (withEffects && t != null && e.Damage != null)
            {
                ShowDamageTexts(t, e.Damage, e.ShowNote);   // 砕ける前に「-NNG」を出す（お宝が消えても最後の位置に残る）
                var pos = t.transform.position;
                var prefab = _content.fx?.Find(ThFxId.Shatter)?.prefab;
                if (prefab != null) Destroy(Instantiate(prefab, pos, Quaternion.identity), ShatterLifetime);
                ThAudioPlayer.Play(_content.audio, ThSoundId.Shatter, pos);
            }
            ReleaseHolder(m.Index);
            if (t != null) Destroy(t.gameObject);
            if (v.Label != null) Destroy(v.Label.gameObject);
        }

        /// <summary>「-NNG」（値段公開済みのお宝だけ）と、初めてぶつけた人への一文（その端末だけ）</summary>
        void ShowDamageTexts(Treasure t, ThDamagedMsg d, bool showNote)
        {
            if (t == null || _content == null || _venue == null) return;
            _popups.RemoveAll(p => p == null);
            if (d.revealed)
                _popups.Add(DamagePopup.Spawn(_venue.Root, t.LabelAnchor, _labelHeight + AmountOffset,
                    _content.text.Format(ThTextId.DamageFormat, d.amount), _labelFont, DamageColor, AmountFontSize, DamagePopup.RiseHeight, DamagePopup.AmountSeconds));
            if (showNote)
                _popups.Add(DamagePopup.Spawn(_venue.Root, t.LabelAnchor, _labelHeight + NoteOffset,
                    _content.text.Get(ThTextId.PracticeDamageNote), _labelFont, NoteColor, NoteFontSize, 0f, DamagePopup.NoteSeconds));
        }

        /// <summary>ヒビを回数の段階に合わせる（1 回目 = Crack1、2 回目 = Crack2。前の段階は外す）。お宝のモデルの大きさに合わせて拡大する</summary>
        void SetCrack(View v, int hits)
        {
            if (v.CrackStage == hits || v.Treasure == null) return;
            v.CrackStage = hits;
            if (v.Crack != null) Destroy(v.Crack);
            v.Crack = null;
            var id = hits == 1 ? ThFxId.Crack1 : hits == 2 ? ThFxId.Crack2 : (ThFxId?)null;
            if (id == null) return;
            var prefab = _content?.fx?.Find(id.Value)?.prefab;
            if (prefab == null) return;
            var go = Instantiate(prefab, v.Treasure.transform);
            var b = v.Treasure.ModelBounds;
            go.transform.localPosition = b.center;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = b.size;
            v.Crack = go;
        }

        /// <summary>猶予の始まり: 外れた瞬間の「残った手 → お宝」の相対を覚え、残った手についていく</summary>
        void BeginGrace(TwoHand th, ClientItem m)
        {
            var released = th.Reserve != null && (int)th.Reserve.Hand == m.GraceHand ? th.Reserve : th.Second;
            var remaining = released == th.Reserve ? th.Second : th.Reserve;
            if (released != null) released.Held = null;
            th.Remaining = remaining;
            if (remaining == null || remaining.Anchor == null || th.Treasure == null || th.Target == null) return;
            var a = remaining.Anchor;
            th.GraceRelPos = a.InverseTransformPoint(th.Treasure.HoldPointWorld);
            th.GraceRelRot = Quaternion.Inverse(a.rotation) * th.Treasure.transform.rotation;
            th.Target.SetPositionAndRotation(th.Treasure.HoldPointWorld, th.Treasure.transform.rotation);
            th.Grace = true;
        }

        void ReleaseHolder(int index)
        {
            if (_twoHands.TryGetValue(index, out var th))
            {
                if (th.Reserve != null && th.Reserve.Held == th.Treasure) th.Reserve.Held = null;
                if (th.Second != null && th.Second.Held == th.Treasure) th.Second.Held = null;
                if (th.Target != null) Destroy(th.Target.gameObject);
                _twoHands.Remove(index);
            }
            if (_holders.TryGetValue(index, out var g))
            {
                if (g != null && g.Held != null && g.Held.Model.Index == index) g.Held = null;
                _holders.Remove(index);
            }
        }

        void PlayDiscover(Treasure t)
        {
            if (_content == null) return;
            var pos = t.transform.position;
            var prefab = _content.fx?.Find(ThFxId.DiscoverLight)?.prefab;
            if (prefab != null) Destroy(Instantiate(prefab, pos, Quaternion.identity), 4f);
            ThAudioPlayer.Play(_content.audio, ThSoundId.Discover, pos);
        }

        /// <summary>お宝の本体コライダーを、今わかっているアバターのコライダーすべてと衝突させない（F-DMG-1）</summary>
        void IgnoreAvatars(Treasure t)
        {
            foreach (var a in _avatarColliders)
                if (a != null) foreach (var b in t.BodyColliders) Physics.IgnoreCollision(a, b, true);
        }

        /// <summary>アバター（NetSync）のコライダーを集め、新しく見つけた物をすべてのお宝と衝突させないようにする。アバターのレイヤーは変えない</summary>
        void RefreshAvatarColliders()
        {
            _avatarColliders.RemoveAll(c => c == null);
            foreach (var avatar in FindObjectsByType<Styly.NetSync.NetSyncAvatar>(FindObjectsSortMode.None))
            {
                avatar.GetComponentsInChildren(true, _colliderBuf);
                foreach (var c in _colliderBuf)
                {
                    if (!_avatarSet.Add(c)) continue;
                    _avatarColliders.Add(c);
                    foreach (var kv in _views)
                        if (kv.Value.Treasure != null) foreach (var b in kv.Value.Treasure.BodyColliders) Physics.IgnoreCollision(c, b, true);
                }
            }
            _avatarSet.RemoveWhere(c => c == null);
        }

        /// <summary>アバターが接続されたとき、少し待ってからコライダーを集め直す（アバターの生成を待つ）</summary>
        async UniTaskVoid RefreshAvatarsLater()
        {
            try
            {
                await UniTask.Delay(1000, cancellationToken: destroyCancellationToken);
                RefreshAvatarColliders();
            }
            catch (System.OperationCanceledException) { }
        }
    }
}
