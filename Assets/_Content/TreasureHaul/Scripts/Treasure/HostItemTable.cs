using System.Collections.Generic;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>ホストが全員へ送るメッセージの出口（SessionHostCore が RoundId をつけて送る）</summary>
    public delegate void ThEmit(Rpc rpc, object dto);

    /// <summary>ホストの状態表の 1 行（設計書 4.2）</summary>
    public sealed class HostItem
    {
        public int Index;
        public TreasureKind Kind;
        public ItemState State = ItemState.Resting;
        public string OwnerDevice;
        public int OwnerClientNo = -1;
        /// <summary>承認した申請ID</summary>
        public int AcceptedReqId;
        /// <summary>判定役（今の持ち主、いなければ最後の持ち主）</summary>
        public string ControllerDevice;
        public int ControllerClientNo = -1;
        public int Gen;
        public int BaseValue;
        public int Value;
        /// <summary>ぶつけた回数（#39 から。#38 では常に 0）</summary>
        public int Hits;
        public bool Revealed;
        public bool Gone;
        public Vector3 RestPos;
        public Quaternion RestRot = Quaternion.identity;
        /// <summary>練習用か（#43 から。#38 では常に false）</summary>
        public bool Practice;
    }

    /// <summary>
    /// ホストの状態表と、お宝のメッセージの受け付け（設計書 4.2・4.4・5章・6.1）。純粋クラス。
    /// 呼び出し側（SessionHostCore）が、RoundId・送り手が今の参加端末であること・Playing 中であること（HostMessageGate）を確かめた後に呼ぶ。
    /// 世代を +1 するのは「持ち主を認めたとき」「つかめるに戻したとき」「回収したとき」だけ。落とした（Dropped）では変えない。
    /// </summary>
    public sealed class HostItemTable
    {
        readonly List<HostItem> _items = new List<HostItem>();
        readonly Bounds _collectBounds;
        readonly ThEmit _emit;
        readonly System.Func<string, int> _clientNoOf;
        // 取りやめ済みの申請（端末, お宝, 申請ID）。後から届いても認めない
        readonly HashSet<string> _cancelled = new HashSet<string>();

        public IReadOnlyList<HostItem> Items => _items;
        /// <summary>回収した合計額（本編のお宝だけ）</summary>
        public int Total { get; private set; }

        /// <param name="clientNoOf">DeviceId から今の ClientNo を引く（再接続で ClientNo が付け替わっても ThGrabbed・ThItems に今の番号を載せるため）。引けなければ -1 を返す</param>
        public HostItemTable(IReadOnlyList<SpawnedTreasure> spawned, Bounds collectBounds, ThEmit emit, System.Func<string, int> clientNoOf = null)
        {
            _clientNoOf = clientNoOf;
            _collectBounds = collectBounds;
            _emit = emit;
            foreach (var s in spawned)
            {
                _items.Add(new HostItem
                {
                    Index = s.Index,
                    Kind = s.Kind,
                    BaseValue = s.Value,
                    Value = s.Value,
                    RestPos = s.LocalPosition,
                    RestRot = Quaternion.Euler(0f, s.YawDegrees, 0f),
                });
            }
        }

        public HostItem Get(int index) => index >= 0 && index < _items.Count ? _items[index] : null;

        public bool InCollectZone(Vector3 localPos) => _collectBounds.Contains(localPos);

        static string Key(string device, int item, int reqId) => device + "|" + item + "|" + reqId;

        // ---- つかむ ----

        public void OnGrab(string deviceId, int clientNo, ThGrabMsg m)
        {
            var it = Get(m.item);
            if (it == null || m.reqId <= 0) return;
            if (_cancelled.Contains(Key(deviceId, m.item, m.reqId))) return;   // 取りやめ済み。認めない（返事も要らない）

            // 同じ申請の再送: すでに認めていれば同じ承認を送り直す
            if (it.State == ItemState.Held && it.OwnerDevice == deviceId && it.AcceptedReqId == m.reqId)
            {
                EmitGrabbed(it);
                return;
            }

            bool ok = !it.Gone
                && it.Kind == TreasureKind.S   // #40 まで M はつかめない
                && !it.Practice
                && it.State == ItemState.Resting
                && !InCollectZone(it.RestPos);
            if (!ok)
            {
                _emit(Rpc.ThGrabDenied, new ThGrabDeniedMsg { item = it.Index, clientNo = clientNo, reqId = m.reqId });
                return;
            }

            it.Gen++;
            it.State = ItemState.Held;
            it.OwnerDevice = deviceId;
            it.OwnerClientNo = clientNo;
            it.AcceptedReqId = m.reqId;
            it.ControllerDevice = deviceId;
            it.ControllerClientNo = clientNo;
            it.Revealed = true;   // 以後ずっと公開
            EmitGrabbed(it);
        }

        int CurrentNo(string device, int stored)
        {
            if (device == null) return stored;
            int no = _clientNoOf != null ? _clientNoOf(device) : -1;
            return no > 0 ? no : stored;
        }

        void EmitGrabbed(HostItem it)
        {
            it.OwnerClientNo = CurrentNo(it.OwnerDevice, it.OwnerClientNo);
            _emit(Rpc.ThGrabbed, new ThGrabbedMsg { item = it.Index, clientNo = it.OwnerClientNo, reqId = it.AcceptedReqId, gen = it.Gen, value = it.Value });
        }

        // ---- 置く・取りやめ ----

        public void OnRelease(string deviceId, ThReleaseMsg m)
        {
            var it = Get(m.item);
            if (it == null || it.Gone) return;
            bool isOwner = it.OwnerDevice == deviceId && it.AcceptedReqId == m.reqId && it.OwnerDevice != null;

            if (m.reason == (int)ReleaseReason.Cancel)
            {
                if (isOwner) { FreeToRest(it); return; }
                // まだ届いていない（または認められなかった）申請。後から届いても認めない
                _cancelled.Add(Key(deviceId, m.item, m.reqId));
                return;
            }

            if (m.reason == (int)ReleaseReason.Dropped)
            {
                // 今の持ち主の申請ID・世代が一致するものだけ。世代は変えない
                if (!isOwner || m.gen != it.Gen) return;
                it.State = ItemState.Falling;
                it.OwnerDevice = null;
                it.OwnerClientNo = -1;
            }
        }

        void FreeToRest(HostItem it)
        {
            it.State = ItemState.Resting;
            it.OwnerDevice = null;
            it.OwnerClientNo = -1;
            it.Gen++;
            _emit(Rpc.ThFreed, new ThFreedMsg { item = it.Index, gen = it.Gen, pos = it.RestPos, rot = it.RestRot });
        }

        /// <summary>判定役の「止まった」。世代が一致すれば 1 回だけ受理（受理すると世代が進むので、送り直しは世代違いで捨てられる）</summary>
        public void OnRest(string deviceId, ThRestMsg m)
        {
            var it = Get(m.item);
            if (it == null || it.Gone || it.Practice) return;
            if (it.ControllerDevice != deviceId || m.gen != it.Gen) return;
            if (it.State != ItemState.Held && it.State != ItemState.Falling) return;
            it.RestPos = m.pos;
            it.RestRot = m.rot;
            // 荷台の回収範囲の中に置かれた（落下が止まった）なら、回収として扱う
            if (InCollectZone(m.pos)) { Collect(it); return; }
            FreeToRest(it);
        }

        // ---- 回収 ----

        public void OnCollect(string deviceId, ThCollectMsg m)
        {
            var it = Get(m.item);
            if (it == null || it.Gone || it.Practice) return;
            if (it.ControllerDevice != deviceId || m.gen != it.Gen) return;
            if (it.State != ItemState.Held && it.State != ItemState.Falling && it.State != ItemState.Resting) return;
            Collect(it);
        }

        void Collect(HostItem it)
        {
            it.Gone = true;
            it.State = ItemState.Collected;
            it.OwnerDevice = null;
            it.OwnerClientNo = -1;
            it.Gen++;
            Total += it.Value;
            _emit(Rpc.ThCollected, new ThCollectedMsg { item = it.Index, gen = it.Gen, total = Total, value = it.Value, damaged = it.Value < it.BaseValue });
        }

        // ---- 状態表 ----

        public ThItemsMsg BuildItems()
        {
            var arr = new ThItemEntry[_items.Count];
            for (int i = 0; i < arr.Length; i++)
            {
                var it = _items[i];
                it.OwnerClientNo = CurrentNo(it.OwnerDevice, it.OwnerClientNo);
                it.ControllerClientNo = CurrentNo(it.ControllerDevice, it.ControllerClientNo);
                arr[i] = new ThItemEntry
                {
                    item = it.Index,
                    state = (int)it.State,
                    owner = it.OwnerClientNo,
                    ctl = it.ControllerClientNo,
                    reqId = it.AcceptedReqId,
                    gen = it.Gen,
                    value = it.Value,
                    hits = it.Hits,
                    revealed = it.Revealed,
                    gone = it.Gone,
                    pos = it.RestPos,
                    rot = it.RestRot,
                };
            }
            return new ThItemsMsg { items = arr };
        }
    }
}
