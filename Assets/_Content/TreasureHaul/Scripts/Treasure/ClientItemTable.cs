using System;
using System.Collections.Generic;
using R3;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>端末側のお宝の 1 つ（モデル。見た目は Treasure / TreasureClient が受け持つ）</summary>
    public sealed class ClientItem
    {
        public int Index;
        public TreasureKind Kind;
        public int BaseValue;
        public int CatalogIndex;
        /// <summary>配置された位置・向き（VenueRoot ローカル）</summary>
        public Vector3 HomePos;
        public Quaternion HomeRot;

        public ItemState State = ItemState.Resting;
        public bool Gone;
        public int Gen;
        /// <summary>ホストが認めた持ち主の ClientNo（いなければ -1）</summary>
        public int OwnerClientNo = -1;
        public bool Revealed;
        public int Value;
        /// <summary>ぶつけた回数（ホストが数えた値。ThDamaged・ThItems で反映）</summary>
        public int Hits;
        public Vector3 RestPos;
        public Quaternion RestRot = Quaternion.identity;

        /// <summary>自分が動かしている（申請中・持っている・落下中・置いた後の承認待ち）</summary>
        public bool Mine;
        /// <summary>自分が最後に出した申請ID（0 = 出していない）</summary>
        public int MyReqId;
        /// <summary>承認待ち</summary>
        public bool ReqPending;
        public double ReqSentAt;
        /// <summary>自分への承認が済んでいる</summary>
        public bool Approved;

        public bool PendingRest;
        public bool PendingCollect;
        public int PendingGen;
        public double LastSendAt;

        /// <summary>他の端末が動かしているときの位置の補間</summary>
        public readonly PoseInterpolator Interp = new PoseInterpolator();

        // ---- M（両手で持つ。#40。設計書 4.4 の M の手順）。S では使わない ----

        /// <summary>予約したつかむ範囲の番号（Treasure.GrabZones の何番目か。-1 = 予約なし）</summary>
        public int LockZone = -1;
        /// <summary>予約した手（ThHand。-1 = なし）</summary>
        public int ReserveHand = -1;
        /// <summary>予約が認められた（ThGrabbed の reserve を受けた）</summary>
        public bool Reserved;
        /// <summary>2 本目の手で握り、ThLift を送って ThLifted を待っている</summary>
        public bool LiftPending;
        public double LiftSentAt;
        /// <summary>予約の承認が届く前に 2 本目の手が握った（承認の瞬間に持ち上げへ進む）</summary>
        public bool LiftQueued;
        /// <summary>2 本目の手（ThHand。-1 = なし）と、握ったつかむ範囲の番号</summary>
        public int SecondHand = -1;
        public int SecondZone = -1;
        /// <summary>予約した手を見失って〔見失い待ち〕の最中（始まった時刻）</summary>
        public bool ReserveLostWait;
        public double ReserveLostSince;
        /// <summary>両手で持っている間に片手が外れて〔M の猶予〕の最中</summary>
        public bool GraceActive;
        public double GraceSince;
        /// <summary>外れた手（ThHand）と、その理由が見失いか</summary>
        public int GraceHand = -1;
        public bool GraceLost;

        // ---- 見失い・手の飛び・そっと置く（#41。設計書 4.2・4.3） ----

        /// <summary>見失い待ち（Hovering）が始まった時刻。Hovering は持ち主の端末だけの状態（ホストの表は Held のまま）</summary>
        public double HoverSince;
        /// <summary>見失い待ちから戻った手が、離す側のしきい値未満のまま見え続けている開始時刻（NaN = 続いていない）。S は A、M は予約した手が A・2 本目が B</summary>
        public double HoverOpenSinceA = double.NaN, HoverOpenSinceB = double.NaN;
        /// <summary>そっと置く（Placing）が始まった時刻</summary>
        public double PlaceSince;
        /// <summary>降ろす先が決まったか（Raycast は TreasureClient が行い、SetPlaceTarget で渡す）</summary>
        public bool PlaceTargetSet;
        public Vector3 PlacePos;
        public Quaternion PlaceRot = Quaternion.identity;
        /// <summary>〔補正の無判定時間〕の終わり（この時刻より前は減額の免除中で、「離れたら置く」も判定しない）</summary>
        public double CorrectionUntil = double.NegativeInfinity;

        /// <summary>減額の免除中か（設計書 5章）: 手元へ飛ぶ途中・Hovering・Placing・〔補正の無判定時間〕の間。flying = 飛ぶ途中の動き</summary>
        public bool IsExemptAt(double now, bool flying = false) =>
            flying || State == ItemState.Flying || State == ItemState.Hovering || State == ItemState.Placing || now < CorrectionUntil;
    }

    public enum ItemEventKind
    {
        /// <summary>自分が申請して手元へ飛び始める</summary>
        GrabStarted,
        /// <summary>自分への承認が届いた（Held にしてよい）</summary>
        Approved,
        /// <summary>自分の申請が否認・取りやめになり、元の位置へ戻す（Resting）</summary>
        Reverted,
        /// <summary>自分が手を開いて落とした（Falling。物理を動かす）</summary>
        Dropped,
        /// <summary>自分が持っていると思っていたが、ホストの状態表と違った</summary>
        OwnershipLost,
        /// <summary>他の端末が持った（以後は ThPoses の位置へ補間）</summary>
        RemoteHeld,
        /// <summary>つかめるに戻った（RestPos に置く）</summary>
        Freed,
        Collected,
        /// <summary>値札を値段にする</summary>
        Revealed,
        /// <summary>高い宝を初めて持った（発見演出）</summary>
        Discovered,
        /// <summary>結果に入った。その場で止める</summary>
        Frozen,
        /// <summary>減額された（ThDamaged）。「-NNG」・ヒビ・音を出す。Damage に中身</summary>
        Damaged,
        /// <summary>ぶつけた回数が増えていた（ThItems で追いついた）。ヒビの段階だけ合わせる</summary>
        HitsSynced,
        /// <summary>3 回目で砕けた（ThDamaged）。砕ける演出・音を出して消す</summary>
        Destroyed,
        /// <summary>消滅していた（ThItems で追いついた）。演出・音なしで消す</summary>
        DestroyedQuiet,
        /// <summary>M を片手で握って予約を申請した（お宝は動かさない。値段も出さない）</summary>
        LockStarted,
        /// <summary>M の両手が成立して ThLift を送った。両手の目標へ飛び始める</summary>
        LiftStarted,
        /// <summary>M の予約を自分で解除した（お宝は動いていないので、手から外すだけ）</summary>
        Unreserved,
        /// <summary>M を両手で持っている間に片手が外れた（〔M の猶予〕の始まり。残った手で持ち続ける）</summary>
        GraceStarted,
        /// <summary>猶予の間に外れた手が握り直して、両手持ちに戻った</summary>
        GraceEnded,
        /// <summary>他の人の M の予約に切り替わった（または置き位置が変わった）。RestPos（置いた位置）に置き直す</summary>
        RemoteLocked,
        /// <summary>持っている手を見失った（見失い待ち）。その場で止める。手は外さない</summary>
        Hovered,
        /// <summary>見失い待ちから手が戻って持ち続ける。手への追従を再開する（〔補正の無判定時間〕が始まっている）</summary>
        HoverResumed,
        /// <summary>自分のお宝をそっと置く（Placing）。降ろす先を決めて SetPlaceTarget で渡し、手から外して降ろす</summary>
        PlaceStarted,
        /// <summary>ホストの ThForceRest（持ち主の切断など）。降ろす先は RestPos・RestRot。自分が持っていたなら手から外す</summary>
        ForceRested,
    }

    public readonly struct ItemEvent
    {
        public readonly ClientItem Item;
        public readonly ItemEventKind Kind;
        /// <summary>Damaged / Destroyed のときの ThDamaged の中身</summary>
        public readonly ThDamagedMsg Damage;
        /// <summary>初めてぶつけた人への一文を出すか（Damaged / Destroyed で、自分が動かしているお宝のとき）</summary>
        public readonly bool ShowNote;
        public ItemEvent(ClientItem item, ItemEventKind kind, ThDamagedMsg damage = null, bool showNote = false)
        {
            Item = item; Kind = kind; Damage = damage; ShowNote = showNote;
        }
    }

    /// <summary>
    /// 端末側のお宝の表と、つかむ手順（設計書 4.4 の端末側）・状態表への追いつき（6.2）。純粋クラス。時刻は引数。
    /// 見た目・物理は購読者（TreasureClient）が <see cref="Events"/> を見て行う。
    /// </summary>
    public sealed partial class ClientItemTable : IDisposable
    {
        readonly IThTransport _tx;
        readonly Func<long> _roundId;
        readonly Func<int> _ownClientNo;
        readonly ThParams _p;
        readonly Bounds _collectBounds;
        readonly List<ClientItem> _items = new List<ClientItem>();
        // 番号 → お宝。番号は連続しなくてよい（練習用は 100〜）
        readonly Dictionary<int, ClientItem> _byIndex = new Dictionary<int, ClientItem>();
        readonly Subject<ItemEvent> _events = new Subject<ItemEvent>();
        int _nextReqId;
        bool _noteShown;

        public IReadOnlyList<ClientItem> Items => _items;
        public Observable<ItemEvent> Events => _events;
        /// <summary>結果に入った。以後は何もつかめない</summary>
        public bool Frozen { get; private set; }
        /// <summary>「ぶつけると値段が下がる」の一文を、この回で出したか（回ごとに 1 回）</summary>
        public bool DamageNoteShown => _noteShown;
        /// <summary>
        /// つかめる局面か（つかめる条件。毎フレーム <see cref="SessionClientCore.UpdateGrabPhase"/> が更新）。
        /// 本編の表: 自分が参加端末で Playing 中 / 練習用の表: 自分が参加端末で Tutorial 中
        /// </summary>
        public bool ParticipantPlaying { get; set; }
        /// <summary>練習用の表か（Tutorial の間だけ。お宝は練習用お宝だけ。減額の報告はしない）</summary>
        public bool IsPractice { get; }
        /// <summary>自分に割り当てられた練習用お宝の番号（練習用の表のうち、つかめるのはこれだけ。無ければ -1）</summary>
        public int PracticeOwnIndex { get; set; } = -1;

        public ClientItemTable(IThTransport tx, Func<long> roundId, Func<int> ownClientNo, ThParams p, ThTreasureSetup setup, IReadOnlyList<SpawnedTreasure> spawned, bool practice = false)
        {
            IsPractice = practice;
            _tx = tx; _roundId = roundId; _ownClientNo = ownClientNo; _p = p;
            _collectBounds = setup.CollectBounds;
            foreach (var s in spawned)
            {
                var rot = Quaternion.Euler(0f, s.YawDegrees, 0f);
                var row = new ClientItem
                {
                    Index = s.Index, Kind = s.Kind, BaseValue = s.Value, Value = s.Value, CatalogIndex = s.CatalogIndex,
                    HomePos = s.LocalPosition, HomeRot = rot, RestPos = s.LocalPosition, RestRot = rot,
                };
                _items.Add(row);
                _byIndex[row.Index] = row;
            }
        }

        public void Dispose() => _events.Dispose();

        public ClientItem Get(int index) => _byIndex.TryGetValue(index, out var it) ? it : null;

        public bool InCollectZone(Vector3 localPos) => _collectBounds.Contains(localPos);

        /// <summary>ビームの対象にしてよいか（他の人が持っている・予約している・飛んでいる・荷台の範囲内・回収済み・S と M 以外ではない）</summary>
        public bool IsGrabbable(ClientItem it) =>
            !Frozen && !it.Gone && (it.Kind == TreasureKind.S || it.Kind == TreasureKind.M) && it.State == ItemState.Resting
            && it.OwnerClientNo < 0 && !it.Mine && !it.PendingRest && !it.PendingCollect && !it.ReqPending
            && !InCollectZone(it.RestPos)
            && (!IsPractice || it.Index == PracticeOwnIndex);   // 練習用の表は、自分に割り当てられたお宝だけ

        void Send<T>(Rpc rpc, T dto) where T : class => _tx.Send(rpc, ThMessages.Encode(_roundId(), dto));
        void Raise(ClientItem it, ItemEventKind k) => _events.OnNext(new ItemEvent(it, k));

        // ---- 自分の操作 ----

        /// <summary>つかむ。条件を満たせば申請を出して Flying にする（手順 1）</summary>
        public bool TryBeginGrab(int index, double now)
        {
            var it = Get(index);
            if (it == null || !ParticipantPlaying || it.Kind != TreasureKind.S || !IsGrabbable(it)) return false;
            it.Mine = true;
            it.MyReqId = ++_nextReqId;
            it.ReqPending = true;
            it.Approved = false;
            it.ReqSentAt = now;
            it.State = ItemState.Flying;
            it.Interp.Clear();
            Send(Rpc.ThGrab, new ThGrabMsg { item = it.Index, reqId = it.MyReqId, gen = it.Gen, kind = (int)it.Kind });
            Raise(it, ItemEventKind.GrabStarted);
            return true;
        }

        /// <summary>
        /// S を握っていた手が外れた。lost = 解除の理由が見失い。
        /// 承認前 = 取りやめ / 承認済みで開いた = 落とす / 承認済みで見失った = 落とさず Hovering（その場で待つ。設計書 4.3）。
        /// </summary>
        public void OnHandReleased(int index, double now, bool lost = false)
        {
            var it = Get(index);
            if (it == null || !it.Mine || it.Gone || Frozen) return;
            if (it.ReqPending) { Revert(it, true); return; }   // 承認前に手を開いた・見失った（手順 4）
            if (it.Approved && (it.State == ItemState.Held || it.State == ItemState.Flying))
            {
                if (lost) { BeginHover(it, now); return; }
                ResetTracking(it);
                it.State = ItemState.Falling;
                Send(Rpc.ThRelease, new ThReleaseMsg { item = it.Index, reqId = it.MyReqId, gen = it.Gen, reason = (int)ReleaseReason.Dropped });
                Raise(it, ItemEventKind.Dropped);
            }
        }

        /// <summary>落下が止まったとみなす時間静止した（荷台の範囲外）。ThRest を送る</summary>
        public void NoteSettled(int index, Vector3 localPos, Quaternion localRot, double now)
        {
            var it = Get(index);
            if (Frozen || it == null || !it.Mine || it.State != ItemState.Falling || it.PendingCollect) return;
            it.State = ItemState.Resting;
            it.RestPos = localPos;
            it.RestRot = localRot;
            it.PendingRest = true;
            it.PendingGen = it.Gen;
            it.LastSendAt = now;
            SendRest(it);
        }

        /// <summary>落下中のお宝が荷台の回収範囲の中にある。ThCollect を送る（1 回だけ。以後は再送のみ）</summary>
        public void NoteInCollectZone(int index, double now)
        {
            var it = Get(index);
            if (Frozen || it == null || !it.Mine || it.State != ItemState.Falling || it.PendingCollect) return;
            it.PendingCollect = true;
            it.PendingGen = it.Gen;
            it.LastSendAt = now;
            Send(Rpc.ThCollect, new ThCollectMsg { item = it.Index, gen = it.PendingGen });
        }

        void SendRest(ClientItem it) =>
            Send(Rpc.ThRest, new ThRestMsg { item = it.Index, gen = it.PendingGen, pos = it.RestPos, rot = it.RestRot });

        /// <summary>毎フレーム。承認の待ち上限と、ThRest・ThCollect の送り直し</summary>
        public void Tick(double now)
        {
            if (Frozen) return;
            foreach (var it in _items)
            {
                if (!it.Mine || it.Gone) continue;
                if (it.ReqPending && now - it.ReqSentAt >= _p.grip.approvalWaitSeconds) { Revert(it, true); continue; }
                if (it.State == ItemState.Hovering)
                {
                    if (now - it.HoverSince >= _p.grip.lostWaitSeconds) BeginPlace(it, now);   // 戻らなかった
                    continue;
                }
                if (it.State == ItemState.Placing)
                {
                    if (now - it.PlaceSince >= _p.grip.placeSeconds) SettlePlace(it, now);
                    continue;
                }
                if (it.Kind == TreasureKind.M && TickTwoHand(it, now)) continue;
                if ((it.PendingRest || it.PendingCollect) && now - it.LastSendAt >= _p.progress.resendInterval)
                {
                    it.LastSendAt = now;
                    if (it.PendingCollect) Send(Rpc.ThCollect, new ThCollectMsg { item = it.Index, gen = it.PendingGen });
                    else SendRest(it);
                }
            }
        }

        /// <summary>否認・待ち上限・承認前に手を開いた → 元の位置へ戻して Resting にし、ThRelease(取りやめ) を送る（手順 4）</summary>
        void Revert(ClientItem it, bool sendCancel)
        {
            it.ReqPending = false;
            it.Approved = false;
            it.Mine = false;
            ClearTwoHand(it);
            if (it.OwnerClientNo == _ownClientNo()) it.OwnerClientNo = -1;   // M の予約が認められていた分
            it.State = ItemState.Resting;
            if (sendCancel && it.MyReqId > 0)
                Send(Rpc.ThRelease, new ThReleaseMsg { item = it.Index, reqId = it.MyReqId, gen = it.Gen, reason = (int)ReleaseReason.Cancel });
            Raise(it, ItemEventKind.Reverted);
        }

        /// <summary>結果に入った。持っているお宝は手から外し（メッセージは送らない）、すべてその場で止める</summary>
        public void Freeze()
        {
            if (Frozen) return;
            Frozen = true;
            foreach (var it in _items)
            {
                if (it.Gone) continue;
                if (it.State == ItemState.Hovering)
                {
                    // 見失い待ちは終わる（手・猶予・両手の状態も消す）。その場で止める見た目は Frozen のイベントで
                    ClearTwoHand(it);
                    it.Mine = false; it.Approved = false; it.State = ItemState.Resting;
                }
                Raise(it, ItemEventKind.Frozen);
            }
        }

        /// <summary>
        /// 自分が動かしているお宝がぶつかった。〔数える衝撃〕以上なら ThDamage をホストへ送る（送り直さない。設計書 6.2）。
        /// 自分が参加端末で Playing 中、結果に入る前（Frozen でない）、かつ自分が動かしている物理中のお宝（Mine で Held の追従中か Falling）だけ。
        /// </summary>
        /// <param name="otherIndex">相手のお宝の番号（障害物なら -1）</param>
        /// <param name="now">時刻（〔補正の無判定時間〕の判定に使う。省略すると免除中にならない）</param>
        /// <param name="localPos">当たった点（VenueRoot ローカル）</param>
        public bool TryReportDamage(int index, int otherIndex, Vector3 localPos, float speed, double now = double.MaxValue)
        {
            if (Frozen || !ParticipantPlaying || IsPractice) return false;   // 練習用お宝は減額しない
            var it = Get(index);
            if (it == null || it.Gone || !it.Mine || !it.Approved) return false;
            if (it.State != ItemState.Held && it.State != ItemState.Falling) return false;
            if (speed < _p.treasure.impactSpeed) return false;
            // 免除中（〔補正の無判定時間〕など）: 障害物なら送らない。お宝どうしなら exemptA で知らせる（相手の免除はホストが判断する）
            bool exempt = it.IsExemptAt(now);
            if (exempt && otherIndex < 0) return false;
            Send(Rpc.ThDamage, new ThDamageMsg { a = index, b = otherIndex, pos = localPos, speed = speed, exemptA = exempt });
            return true;
        }

        // ---- ホストからのメッセージ ----

        /// <summary>減額（ThDamaged）。ぶつけた回数が今より大きいときだけ反映する（同じ・古い物は捨てる）</summary>
        public void OnDamaged(ThDamagedMsg m)
        {
            var it = m == null ? null : Get(m.item);
            if (it == null || it.Gone || Frozen) return;
            if (m.hits <= it.Hits) return;
            it.Hits = m.hits;
            it.Value = m.value;
            it.Gen = Math.Max(it.Gen, m.gen);
            // 初めての一文: 自分が動かしているお宝で、この回で初めてのとき（値段が「？」でも出す）
            bool note = it.Mine && it.State != ItemState.Locked && !_noteShown;   // 予約中の M は動いていないので、本人にも出さない
            if (note) _noteShown = true;
            if (m.destroyed)
            {
                ClearOwnership(it);
                it.State = ItemState.Destroyed;
                it.Gone = true;
                Raise(new ItemEvent(it, ItemEventKind.Destroyed, m, note));
                return;
            }
            Raise(new ItemEvent(it, ItemEventKind.Damaged, m, note));
        }

        void Raise(ItemEvent e) => _events.OnNext(e);

        /// <summary>消滅: 自分の申請・送り直しを消す</summary>
        static void ClearOwnership(ClientItem it)
        {
            it.Mine = false; it.ReqPending = false; it.Approved = false; it.PendingRest = false; it.PendingCollect = false;
            ClearTwoHand(it);
            it.OwnerClientNo = -1;
        }

        public void OnGrabbed(ThGrabbedMsg m, double now)
        {
            var it = Get(m.item);
            if (it == null || it.Gone || Frozen) return;
            if (m.gen < it.Gen) return;   // 古い
            int own = _ownClientNo();

            if (m.clientNo == own)
            {
                if (m.reserve) { OnReserveApproved(it, m, now); return; }
                if (it.MyReqId == m.reqId && it.Approved) return;   // 重複
                if (it.MyReqId == m.reqId && it.ReqPending && it.Mine)
                {
                    it.Gen = m.gen;
                    Approve(it, m.value, true);
                    return;
                }
                // 最新ではない申請への承認（取りやめ→再申請の後に届いた）。その申請IDで取りやめを返す（手順 5）。ホストの世代は進む
                it.Gen = Math.Max(it.Gen, m.gen);
                Send(Rpc.ThRelease, new ThReleaseMsg { item = m.item, reqId = m.reqId, gen = m.gen, reason = (int)ReleaseReason.Cancel });
                return;
            }

            // 他の端末が持った
            // 同じ持ち主の予約（世代が同じ ThGrabbed(reserve)）が、持ち上げ済み（Held）の後に届いても Held を Locked に戻さない
            if (m.reserve && !it.Mine && it.State == ItemState.Held && it.OwnerClientNo == m.clientNo) return;
            bool lostRace = it.Mine;
            it.Gen = m.gen;
            if (lostRace) { var wasPending = it.ReqPending; Revert(it, wasPending); }
            if (m.reserve) { SetRemoteLocked(it, m.clientNo); return; }   // M の予約: 値段は公開しない・お宝は置いた位置のまま
            SetRemoteHeld(it, m.clientNo);
            SetRevealed(it, m.value, true);
        }

        void Approve(ClientItem it, int value, bool discover)
        {
            it.ReqPending = false;
            it.Approved = true;
            it.OwnerClientNo = _ownClientNo();
            it.State = ItemState.Held;
            SetRevealed(it, value, discover);
            Raise(it, ItemEventKind.Approved);
        }

        void SetRevealed(ClientItem it, int value, bool discover)
        {
            it.Value = value;
            if (it.Revealed) { Raise(it, ItemEventKind.Revealed); return; }
            it.Revealed = true;
            Raise(it, ItemEventKind.Revealed);
            if (discover && value >= _p.treasure.discoverValue) Raise(it, ItemEventKind.Discovered);
        }

        void SetRemoteHeld(ClientItem it, int ownerClientNo)
        {
            bool changed = it.OwnerClientNo != ownerClientNo || it.State != ItemState.Held;
            it.OwnerClientNo = ownerClientNo;
            it.State = ItemState.Held;
            if (changed) { it.Interp.Clear(); Raise(it, ItemEventKind.RemoteHeld); }
        }

        public void OnDenied(ThGrabDeniedMsg m, double now)
        {
            var it = Get(m.item);
            if (it == null || it.Gone || Frozen) return;
            if (m.clientNo != _ownClientNo()) return;
            if (it.MyReqId != m.reqId || !(it.ReqPending || it.LiftPending)) return;   // M は予約の否認も ThLift の否認もここ
            Revert(it, true);
        }

        public void OnFreed(ThFreedMsg m)
        {
            var it = Get(m.item);
            if (it == null || it.Gone || Frozen) return;
            if (m.gen < it.Gen) return;
            it.Gen = m.gen;
            if (it.Mine && it.ReqPending)
            {
                // 古い申請の取りやめで出た ThFreed が、新しい申請の承認待ちの間に届いた。新しい申請は消さず、世代と置き位置だけ更新する
                it.RestPos = m.pos;
                it.RestRot = m.rot;
                return;
            }
            ApplyFreed(it, m.pos, m.rot);
        }

        void ApplyFreed(ClientItem it, Vector3 pos, Quaternion rot)
        {
            it.Mine = false;
            it.Approved = false;
            it.ReqPending = false;
            it.PendingRest = false;
            it.PendingCollect = false;
            ClearTwoHand(it);
            it.OwnerClientNo = -1;
            it.State = ItemState.Resting;
            it.RestPos = pos;
            it.RestRot = rot;
            it.Interp.Clear();
            Raise(it, ItemEventKind.Freed);
        }

        public void OnCollected(ThCollectedMsg m)
        {
            var it = Get(m.item);
            if (it == null) return;
            MarkCollected(it);
        }

        void MarkCollected(ClientItem it)
        {
            if (it.Gone) return;
            it.Gone = true;
            it.Mine = false; it.ReqPending = false; it.Approved = false; it.PendingRest = false; it.PendingCollect = false;
            ClearTwoHand(it);
            it.State = ItemState.Collected;
            Raise(it, ItemEventKind.Collected);
        }

        void MarkDestroyedQuiet(ClientItem it)
        {
            if (it.Gone) return;
            ClearOwnership(it);
            it.State = ItemState.Destroyed;
            it.Gone = true;
            Raise(it, ItemEventKind.DestroyedQuiet);
        }

        /// <summary>他の端末が動かしているお宝の位置（受信時刻で並べる）</summary>
        public void OnPoses(ThPosesMsg m, double now)
        {
            if (m?.poses == null || Frozen) return;
            foreach (var e in m.poses)
            {
                var it = e == null ? null : Get(e.item);
                if (it == null || it.Mine || it.Gone) continue;
                it.Interp.Add(now, e.pos, e.rot);
            }
        }

        /// <summary>状態表（ThItems）に合わせる。ThGrabbed・ThFreed・ThCollected を取りこぼしても追いつく（6.2）</summary>
        public void OnItems(ThItemsMsg m, double now)
        {
            if (m?.items == null || Frozen) return;
            foreach (var e in m.items)
            {
                var it = e == null ? null : Get(e.item);
                if (it == null) continue;
                if (e.hits > it.Hits && !it.Gone)
                {
                    // ThDamaged を取りこぼした。ヒビの段階だけ合わせる（「-NNG」と音は出さない）
                    it.Hits = e.hits;
                    it.Value = e.value;
                    Raise(it, ItemEventKind.HitsSynced);
                }
                if (e.gone)
                {
                    if ((ItemState)e.state == ItemState.Destroyed) MarkDestroyedQuiet(it);
                    else MarkCollected(it);
                    continue;
                }
                if (it.Gone) continue;
                if (e.gen < it.Gen) continue;
                Reconcile(it, e, now);
            }
        }

        void Reconcile(ClientItem it, ThItemEntry e, double now)
        {
            int own = _ownClientNo();
            var state = (ItemState)e.state;
            int oldGen = it.Gen;
            it.Gen = Math.Max(it.Gen, e.gen);
            if (e.revealed) SetRevealed(it, e.value, false);
            else it.Value = e.value;

            if (e.owner >= 0)
            {
                if (e.owner == own)
                {
                    if (it.Mine && it.MyReqId == e.reqId)
                    {
                        if (it.Kind == TreasureKind.M) ReconcileOwnM(it, e, state, now);
                        else if (it.ReqPending) Approve(it, e.value, false);   // ThGrabbed を取りこぼした
                    }
                    else
                    {
                        // ホストは自分が持ち主と思っているが、こちらは覚えていない申請 → 返す
                        Send(Rpc.ThRelease, new ThReleaseMsg { item = it.Index, reqId = e.reqId, gen = e.gen, reason = (int)ReleaseReason.Cancel });
                    }
                    return;
                }
                bool posChanged = false;
                if (state == ItemState.Locked)
                {
                    // 同じ持ち主ですでに Held と知っているなら、同じ世代の Locked の状態表で戻さない
                    if (!it.Mine && it.State == ItemState.Held && it.OwnerClientNo == e.owner) return;
                    // 予約中の M は誰も位置を送らないので、ホストの置き位置に合わせる
                    posChanged = (it.RestPos - e.pos).sqrMagnitude > 1e-6f;
                    it.RestPos = e.pos;
                    it.RestRot = e.rot;
                }
                if (it.Mine)
                {
                    bool pending = it.ReqPending || it.LiftPending;
                    it.Mine = false; it.ReqPending = false; it.Approved = false; it.PendingRest = false; it.PendingCollect = false;
                    ClearTwoHand(it);
                    it.State = ItemState.Resting;
                    if (pending) Raise(it, ItemEventKind.Reverted); else Raise(it, ItemEventKind.OwnershipLost);
                }
                if (state == ItemState.Locked) SetRemoteLocked(it, e.owner, posChanged);   // 他の人の M の予約（値段は公開しない）
                else SetRemoteHeld(it, e.owner);
                return;
            }

            // 持ち主なし
            if (state == ItemState.Placing)
            {
                // ホストが持ち主の切断などでそっと置かせた。ThForceRest を取りこぼした（世代が新しいのに知らない）なら、受けたのと同じにする
                if (e.gen > oldGen) ApplyForceRest(it, e.gen, e.pos, e.rot);
                return;
            }
            if (state == ItemState.Resting)
            {
                if (it.Mine && it.ReqPending) return;   // 申請の途中（ホストはまだ受けていない）
                if (it.Mine && it.Kind == TreasureKind.M && (it.Reserved || it.LiftPending) && e.gen > oldGen)
                {
                    // 予約中のつもりが、ホストでは解除されて世代が進んでいた（切断扱い・ThFreed の取りこぼしなど）→ 予約を消して手から外す
                    ApplyFreed(it, e.pos, e.rot);
                    return;
                }
                if (it.Mine && it.Approved && it.State != ItemState.Falling && it.State != ItemState.Resting)
                {
                    // 持っていたつもりが、ホストでは置かれた → 手放す
                    it.Mine = false; it.Approved = false;
                    Raise(it, ItemEventKind.OwnershipLost);
                    ApplyFreed(it, e.pos, e.rot);
                    return;
                }
                if (it.Mine && (it.PendingRest || it.PendingCollect))
                {
                    if (e.gen > it.PendingGen) ApplyFreed(it, e.pos, e.rot);   // 反映された。送り直しをやめる
                    return;
                }
                if (!it.Mine && (it.State != ItemState.Resting || it.OwnerClientNo >= 0 || (it.RestPos - e.pos).sqrMagnitude > 1e-6f))
                    ApplyFreed(it, e.pos, e.rot);
                return;
            }

            if (state == ItemState.Falling && !it.Mine)
            {
                if (it.State != ItemState.Falling) it.Interp.Clear();
                it.State = ItemState.Falling;
                it.OwnerClientNo = -1;
            }
        }
    }
}
