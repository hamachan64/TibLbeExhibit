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
        const string HandleLayerName = "TreasureHandle";
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
        }

        /// <summary>「-NNG」の赤。ぶつけて値段が下がったことが一目でわかる色</summary>
        static readonly Color DamageColor = new Color(1f, 0.2f, 0.2f);
        /// <summary>初めての一文の色。赤い「-NNG」と区別して読める白</summary>
        static readonly Color NoteColor = Color.white;
        /// <summary>値札の上に重ねない高さ（m）。値札（高さ 0.3m）の上に、一文 → 「-NNG」の順に積む</summary>
        const float NoteOffset = 0.1f, AmountOffset = 0.18f;
        const float AmountFontSize = 1.0f, NoteFontSize = 0.45f;
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
        HandGrabber _grabbing;
        ThParams _configuredFor;

        int _treasureLayer = -1, _handleLayer = -1, _handBoneLayer = -1;
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
        public HandGrabber Left => _left;
        public HandGrabber Right => _right;

        static double Now => Time.realtimeSinceStartupAsDouble;

        void Awake()
        {
            _treasureLayer = LayerMask.NameToLayer(TreasureLayerName);
            _handleLayer = LayerMask.NameToLayer(HandleLayerName);
            _handBoneLayer = LayerMask.NameToLayer(HandBoneLayerName);
            if (_treasureLayer < 0 || _handleLayer < 0 || _handBoneLayer < 0)
                Debug.LogWarning($"[TreasureClient] レイヤーがありません（{TreasureLayerName}={_treasureLayer} {HandleLayerName}={_handleLayer} {HandBoneLayerName}={_handBoneLayer}）");
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
            if (now >= _nextAvatarScan) { _nextAvatarScan = now + AvatarFallbackScanSeconds; RefreshAvatarColliders(); }
            if (_table == null) return;

            // 結果に入った後は位置も ThRest・ThCollect も送らない
            if (_table.Frozen) return;
            _table.ParticipantPlaying = _core.Tracker.IsParticipant.CurrentValue && _core.Tracker.State.CurrentValue == RoundState.Playing;
            var venueRoot = _venue.Root;
            var grip = Grip;
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
                    if (m.Approved && !m.PendingRest && (m.State == ItemState.Held || m.State == ItemState.Flying || m.State == ItemState.Falling))
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
                bool want = _table != null && !_table.Frozen && ((_left != null && _left.Aimed == t) || (_right != null && _right.Aimed == t));
                if (t.Aimed != want) t.SetAimed(want);
            }
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
            _configuredFor = null;
            var fx = _content?.fx;
            var beamMat = fx?.Find(ThFxId.HandleAimed)?.material;
            if (_left != null) _left.Initialize(this, _left.Tracker, _left.GetComponent<LineRenderer>(), beamMat, _handleLayer);
            if (_right != null) _right.Initialize(this, _right.Tracker, _right.GetComponent<LineRenderer>(), beamMat, _handleLayer);
        }

        void ConfigureHands()
        {
            if (ReferenceEquals(_configuredFor, _core.Params)) return;
            _configuredFor = _core.Params;
            var g = _core.Params.grip;
            foreach (var h in new[] { _left, _right })
                if (h != null && h.Tracker != null) h.Tracker.Configure(g.gripThreshold, g.releaseThreshold, g.releaseHoldSeconds, g.trackingLossGrace);
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
            var normal = fx?.Find(ThFxId.HandleNormal)?.material;
            var aimed = fx?.Find(ThFxId.HandleAimed)?.material;
            foreach (var m in table.Items)
            {
                if (m.CatalogIndex < 0 || m.CatalogIndex >= catalog.entries.Count) { Debug.LogWarning($"[TreasureClient] お宝 {m.Index} の見た目がありません"); continue; }
                var prefab = catalog.entries[m.CatalogIndex].prefab;
                var go = Instantiate(prefab, _venue.Root);
                go.name = $"Treasure_{m.Index}_{m.Kind}";
                var t = go.GetComponent<Treasure>();
                t.Initialize(m, Grip, normal, aimed, _treasureLayer, _handleLayer);
                t.ConfigureDamage(_core.Params.treasure.speedSmoothFrames, _core.Params.treasure.redamageInterval, _core.Params.grip.poseSendInterval, _core.Params.grip.followSpeedMax);
                t.PlaceKinematic(m.HomePos, m.HomeRot);
                t.Impacts.Subscribe(imp => OnImpact(t, imp)).AddTo(_tableSubs);
                var label = PriceLabel.Create(_venue.Root, t.LabelAnchor, _labelHeight, _content.text, _labelFont);
                label.SetState(m.Revealed, m.Value);
                _views[m.Index] = new View { Treasure = t, Label = label };
                IgnoreAvatars(t);
            }
        }

        void ClearViews()
        {
            _tableSubs?.Dispose();
            _tableSubs = null;
            foreach (var kv in _views)
            {
                if (kv.Value.Treasure != null) Destroy(kv.Value.Treasure.gameObject);   // ヒビはお宝の子なので一緒に消える
                if (kv.Value.Label != null) Destroy(kv.Value.Label.gameObject);
            }
            _views.Clear();
            foreach (var p in _popups) if (p != null) Destroy(p.gameObject);   // 表示中の「-NNG」・一文（Tween は SetLink で止まる）
            _popups.Clear();
            _holders.Clear();
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

        public bool IsTargetable(Treasure t) => _table != null && t != null && t.Model != null && _table.IsGrabbable(t.Model);

        public bool TryGrab(HandGrabber grabber, Treasure t)
        {
            if (!CanGrab || t == null) return false;
            _grabbing = grabber;
            bool ok = _table.TryBeginGrab(t.Model.Index, Now);
            _grabbing = null;
            if (!ok) return false;
            _holders[t.Model.Index] = grabber;
            grabber.Held = t;
            return true;
        }

        public void OnHandReleased(HandGrabber grabber)
        {
            var t = grabber.Held;
            if (t == null || _table == null) { grabber.Held = null; return; }
            grabber.Held = null;
            _holders.Remove(t.Model.Index);
            _table.OnHandReleased(t.Model.Index, Now);
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
                    break;
                case ItemEventKind.Frozen:
                    ReleaseHolder(m.Index);
                    t.StopInPlace();
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

        // ---- 減額（設計書 5章・9章） ----

        /// <summary>自分が動かしているお宝がぶつかった。数える衝撃以上なら ThDamage をホストへ送る（判断は ClientItemTable）</summary>
        void OnImpact(Treasure t, TreasureImpact imp)
        {
            if (_table == null || _venue == null || t == null || t.Model == null) return;
            var local = _venue.Root.InverseTransformPoint(imp.WorldPoint);
            _table.TryReportDamage(t.Model.Index, imp.OtherIndex, local, imp.Speed);
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

        void ReleaseHolder(int index)
        {
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
