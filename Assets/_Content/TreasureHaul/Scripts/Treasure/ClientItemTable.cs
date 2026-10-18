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
    }

    public readonly struct ItemEvent
    {
        public readonly ClientItem Item;
        public readonly ItemEventKind Kind;
        public ItemEvent(ClientItem item, ItemEventKind kind) { Item = item; Kind = kind; }
    }

    /// <summary>
    /// 端末側のお宝の表と、つかむ手順（設計書 4.4 の端末側）・状態表への追いつき（6.2）。純粋クラス。時刻は引数。
    /// 見た目・物理は購読者（TreasureClient）が <see cref="Events"/> を見て行う。
    /// </summary>
    public sealed class ClientItemTable : IDisposable
    {
        readonly IThTransport _tx;
        readonly Func<long> _roundId;
        readonly Func<int> _ownClientNo;
        readonly ThParams _p;
        readonly Bounds _collectBounds;
        readonly List<ClientItem> _items = new List<ClientItem>();
        readonly Subject<ItemEvent> _events = new Subject<ItemEvent>();
        int _nextReqId;

        public IReadOnlyList<ClientItem> Items => _items;
        public Observable<ItemEvent> Events => _events;
        /// <summary>結果に入った。以後は何もつかめない</summary>
        public bool Frozen { get; private set; }
        /// <summary>自分が参加端末で、Playing 中か（つかめる条件。TreasureClient が毎フレーム更新）</summary>
        public bool ParticipantPlaying { get; set; }

        public ClientItemTable(IThTransport tx, Func<long> roundId, Func<int> ownClientNo, ThParams p, ThTreasureSetup setup, IReadOnlyList<SpawnedTreasure> spawned)
        {
            _tx = tx; _roundId = roundId; _ownClientNo = ownClientNo; _p = p;
            _collectBounds = setup.CollectBounds;
            foreach (var s in spawned)
            {
                var rot = Quaternion.Euler(0f, s.YawDegrees, 0f);
                _items.Add(new ClientItem
                {
                    Index = s.Index, Kind = s.Kind, BaseValue = s.Value, Value = s.Value, CatalogIndex = s.CatalogIndex,
                    HomePos = s.LocalPosition, HomeRot = rot, RestPos = s.LocalPosition, RestRot = rot,
                });
            }
        }

        public void Dispose() => _events.Dispose();

        public ClientItem Get(int index) => index >= 0 && index < _items.Count ? _items[index] : null;

        public bool InCollectZone(Vector3 localPos) => _collectBounds.Contains(localPos);

        /// <summary>ビームの対象にしてよいか（他の人が持っている・飛んでいる・荷台の範囲内・回収済み・M ではない）</summary>
        public bool IsGrabbable(ClientItem it) =>
            !Frozen && !it.Gone && it.Kind == TreasureKind.S && it.State == ItemState.Resting
            && it.OwnerClientNo < 0 && !it.Mine && !it.PendingRest && !it.PendingCollect && !it.ReqPending
            && !InCollectZone(it.RestPos);

        void Send<T>(Rpc rpc, T dto) where T : class => _tx.Send(rpc, ThMessages.Encode(_roundId(), dto));
        void Raise(ClientItem it, ItemEventKind k) => _events.OnNext(new ItemEvent(it, k));

        // ---- 自分の操作 ----

        /// <summary>つかむ。条件を満たせば申請を出して Flying にする（手順 1）</summary>
        public bool TryBeginGrab(int index, double now)
        {
            var it = Get(index);
            if (it == null || !ParticipantPlaying || !IsGrabbable(it)) return false;
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

        /// <summary>手を開いた（解除の理由が見失いのときも、#38 では同じ。#41 で Hovering に置き換える）</summary>
        public void OnHandReleased(int index, double now)
        {
            var it = Get(index);
            if (it == null || !it.Mine || it.Gone || Frozen) return;
            if (it.ReqPending) { Revert(it, true); return; }   // 承認前に手を開いた（手順 4）
            if (it.Approved && (it.State == ItemState.Held || it.State == ItemState.Flying))
            {
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
                Raise(it, ItemEventKind.Frozen);
            }
        }

        // ---- ホストからのメッセージ ----

        public void OnGrabbed(ThGrabbedMsg m, double now)
        {
            var it = Get(m.item);
            if (it == null || it.Gone || Frozen) return;
            if (m.gen < it.Gen) return;   // 古い
            int own = _ownClientNo();

            if (m.clientNo == own)
            {
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
            bool lostRace = it.Mine;
            it.Gen = m.gen;
            if (lostRace) { var wasPending = it.ReqPending; Revert(it, wasPending); }
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
            if (it.MyReqId != m.reqId || !it.ReqPending) return;
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
            it.State = ItemState.Collected;
            Raise(it, ItemEventKind.Collected);
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
                if (e.gone) { MarkCollected(it); continue; }
                if (it.Gone) continue;
                if (e.gen < it.Gen) continue;
                Reconcile(it, e);
            }
        }

        void Reconcile(ClientItem it, ThItemEntry e)
        {
            int own = _ownClientNo();
            var state = (ItemState)e.state;
            it.Gen = Math.Max(it.Gen, e.gen);
            if (e.revealed) SetRevealed(it, e.value, false);
            else it.Value = e.value;

            if (e.owner >= 0)
            {
                if (e.owner == own)
                {
                    if (it.Mine && it.MyReqId == e.reqId)
                    {
                        if (it.ReqPending) Approve(it, e.value, false);   // ThGrabbed を取りこぼした
                    }
                    else
                    {
                        // ホストは自分が持ち主と思っているが、こちらは覚えていない申請 → 返す
                        Send(Rpc.ThRelease, new ThReleaseMsg { item = it.Index, reqId = e.reqId, gen = e.gen, reason = (int)ReleaseReason.Cancel });
                    }
                    return;
                }
                if (it.Mine)
                {
                    bool pending = it.ReqPending;
                    it.Mine = false; it.ReqPending = false; it.Approved = false; it.PendingRest = false; it.PendingCollect = false;
                    it.State = ItemState.Resting;
                    if (pending) Raise(it, ItemEventKind.Reverted); else Raise(it, ItemEventKind.OwnershipLost);
                }
                SetRemoteHeld(it, e.owner);
                return;
            }

            // 持ち主なし
            if (state == ItemState.Resting)
            {
                if (it.Mine && it.ReqPending) return;   // 申請の途中（ホストはまだ受けていない）
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
