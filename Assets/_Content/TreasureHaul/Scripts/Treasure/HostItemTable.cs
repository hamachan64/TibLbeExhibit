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
        /// <summary>判定役から報告された減額の免除（ThPoses の exempt。設計書 5章）。持ち主が替わる・つかめるに戻る・回収・消滅で false に戻す</summary>
        public bool ExemptReported;

        /// <summary>ホストが見る免除中: 判定役の報告、または状態が Flying / Placing / Hovering（4.2）</summary>
        public bool IsDamageExempt =>
            ExemptReported || State == ItemState.Flying || State == ItemState.Placing || State == ItemState.Hovering;
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
        readonly ThTreasureParams _tp;
        // 通信上の重複を捨てるための、組み合わせごとの最後に受けた時刻（捨てた報告も含む）
        readonly Dictionary<long, double> _lastDamageAt = new Dictionary<long, double>();

        public IReadOnlyList<HostItem> Items => _items;
        /// <summary>回収した合計額（本編のお宝だけ）</summary>
        public int Total { get; private set; }
        /// <summary>適用した減額の合計（回ごと。#42 の「今回の損害」、#44 の記録の材料。表示は作らない）</summary>
        public int DamageTotal { get; private set; }
        /// <summary>減額の受け付けの記録の出口（実機調整 #45・#49 用。1 件ごと。null なら出さない）</summary>
        public System.Action<string> Log { get; set; }
        void LogLine(string s) => Log?.Invoke("[HostDamage] " + s);

        /// <param name="clientNoOf">DeviceId から今の ClientNo を引く（再接続で ClientNo が付け替わっても ThGrabbed・ThItems に今の番号を載せるため）。引けなければ -1 を返す</param>
        public HostItemTable(IReadOnlyList<SpawnedTreasure> spawned, Bounds collectBounds, ThEmit emit, System.Func<string, int> clientNoOf = null, ThTreasureParams treasureParams = null)
        {
            _tp = treasureParams ?? new ThTreasureParams();
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
            it.ExemptReported = false;
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
                it.ExemptReported = false;
            }
        }

        void FreeToRest(HostItem it)
        {
            it.State = ItemState.Resting;
            it.OwnerDevice = null;
            it.OwnerClientNo = -1;
            it.ExemptReported = false;
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
            it.ExemptReported = false;
            it.Gen++;
            Total += it.Value;
            _emit(Rpc.ThCollected, new ThCollectedMsg { item = it.Index, gen = it.Gen, total = Total, value = it.Value, damaged = it.Value < it.BaseValue });
        }

        // ---- 免除の報告（ThPoses） ----

        /// <summary>
        /// 判定役の位置の送信から、減額の免除だけを読む。送り手がそのお宝の判定役で、お宝が消えていないときだけ覚える。
        /// 位置そのものは端末どうしで補間するので、ホストは使わない。
        /// </summary>
        public void OnPoses(string deviceId, ThPosesMsg m)
        {
            if (m?.poses == null || deviceId == null) return;
            foreach (var e in m.poses)
            {
                var it = e == null ? null : Get(e.item);
                if (it == null || it.Gone) continue;
                // 動いている状態のときだけ。Resting になった後に遅れて届いた exempt=true で免除が立ちっぱなしにならないように
                if (it.State != ItemState.Held && it.State != ItemState.Falling && it.State != ItemState.Flying
                    && it.State != ItemState.Placing && it.State != ItemState.Hovering) continue;
                // 送り手は持ち主（Held 系）か、持ち主がいなければ判定役（Falling 等）
                bool sender = it.OwnerDevice != null ? it.OwnerDevice == deviceId : it.ControllerDevice == deviceId;
                if (!sender) continue;
                it.ExemptReported = e.exempt;
            }
        }

        // ---- 減額 ----

        /// <summary>組み合わせのキー。お宝同士は小さい番号・大きい番号の組（向きは区別しない）、障害物は (a, -1)</summary>
        static long PairKey(int a, int b)
        {
            int lo = System.Math.Min(a, b), hi = System.Math.Max(a, b);
            return ((long)(lo + 1) << 32) | (uint)(hi + 1);
        }

        /// <summary>
        /// 判定役の「ぶつけた」（設計書 5章・6.1）。Playing 中であることは呼び出し側が確かめる。
        /// 送り手が a か b の判定役のときだけ受け付け、同じ組み合わせの重複を捨て、お宝ごとに別々に免除などを判断して適用する。
        /// </summary>
        public void OnDamage(string deviceId, ThDamageMsg m, double now)
        {
            if (m == null || deviceId == null) return;
            var a = Get(m.a);
            var b = m.b >= 0 ? Get(m.b) : null;
            if (a == null && b == null) return;
            if (m.a == m.b) return;
            bool fromController = (a != null && a.ControllerDevice == deviceId) || (b != null && b.ControllerDevice == deviceId);
            if (!fromController)   // 判定役でない送り手の報告は捨てる（重複の記録もしない）
            {
                LogLine($"判定役でないので捨てた device={deviceId} a={m.a} b={m.b} speed={m.speed:F2}");
                return;
            }

            long key = PairKey(m.a, m.b);
            bool duplicate = _lastDamageAt.TryGetValue(key, out var last) && now - last <= _tp.dedupSeconds;
            _lastDamageAt[key] = now;   // 捨てた報告でも時刻を延ばす
            if (duplicate)
            {
                LogLine($"重複で捨てた a={m.a} b={m.b} 直前の報告から {now - last:F3}秒 speed={m.speed:F2}");
                return;
            }

            ApplyDamage(m.a, a, m.pos, m.speed);
            if (m.b >= 0) ApplyDamage(m.b, b, m.pos, m.speed);
        }

        void ApplyDamage(int index, HostItem it, Vector3 pos, float speed)
        {
            if (it == null) { LogLine($"適用しなかった item={index} お宝が無い"); return; }
            if (it.Gone) { LogLine($"適用しなかった item={index} 回収・消滅済み"); return; }
            if (it.Practice) { LogLine($"適用しなかった item={index} 練習用"); return; }
            if (it.IsDamageExempt) { LogLine($"適用しなかった item={index} 免除中 state={it.State} 報告={it.ExemptReported}"); return; }
            var r = DamageCalculator.Apply(it.BaseValue, it.Value, it.Hits, _tp);
            if (!r.Applied) return;

            // 記録用（設計書 8章）: そのときの持ち主、いなければ判定役（最後に持った人）。誰も持ったことがなければ -1（当てた側の人には付けない）
            int by = it.OwnerDevice != null ? CurrentNo(it.OwnerDevice, it.OwnerClientNo)
                : it.ControllerDevice != null ? CurrentNo(it.ControllerDevice, it.ControllerClientNo)
                : -1;

            it.Hits = r.Hits;
            it.Value = r.Value;
            DamageTotal += r.Amount;
            if (r.Destroyed)
            {
                it.Gone = true;
                it.State = ItemState.Destroyed;
                it.OwnerDevice = null;
                it.OwnerClientNo = -1;
                it.ExemptReported = false;
                it.Gen++;   // 設計書 4.2: 回収・消滅のとき
            }
            LogLine($"適用した item={it.Index} 回数={it.Hits} 額={r.Amount} 価値={it.Value} 消滅={r.Destroyed} speed={speed:F2}");
            _emit(Rpc.ThDamaged, new ThDamagedMsg
            {
                item = it.Index, gen = it.Gen, hits = it.Hits, amount = r.Amount, value = it.Value,
                pos = pos, destroyed = r.Destroyed, by = by, revealed = it.Revealed,
            });
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
