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
        /// <summary>練習用か（#43。練習用の表の行は true。練習用の表以外では、つかむ・回収・減額の対象にしない）</summary>
        public bool Practice;
        /// <summary>練習用お宝を割り当てられた端末の DeviceId（つかめるのはこの端末だけ）</summary>
        public string PracticeDevice;
        /// <summary>判定役から報告された減額の免除（ThPoses の exempt。設計書 5章）。持ち主が替わる・つかめるに戻る・回収・消滅で false に戻す</summary>
        public bool ExemptReported;
        /// <summary>判定役から最後に受けた位置・向き（ThPoses。VenueRoot ローカル）。持ち主の切断で降ろす先を決める元（#41）。HasLastPos=false なら置き位置を使う</summary>
        public bool HasLastPos;
        public Vector3 LastPos;
        public Quaternion LastRot = Quaternion.identity;
        /// <summary>Placing になった時刻（〔置く時間〕の後に「つかめる」に戻す・回収する）</summary>
        public double PlaceSince;

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
        // 番号 → 行。番号は連続しなくてよい（練習用は 100〜）
        readonly Dictionary<int, HostItem> _byIndex = new Dictionary<int, HostItem>();
        readonly bool _practiceTable;
        readonly Bounds _collectBounds;
        readonly ThEmit _emit;
        readonly System.Func<string, int> _clientNoOf;
        // 取りやめ済みの申請（端末, お宝, 申請ID）。後から届いても認めない
        readonly HashSet<string> _cancelled = new HashSet<string>();
        readonly ThTreasureParams _tp;
        readonly float _placeSeconds;
        // 通信上の重複を捨てるための、組み合わせごとの最後に受けた時刻（捨てた報告も含む）
        readonly Dictionary<long, double> _lastDamageAt = new Dictionary<long, double>();

        public IReadOnlyList<HostItem> Items => _items;
        /// <summary>練習用の表か（Tutorial の間だけホストが持つ。行はすべて練習用で、回収しても合計額（Total）は変わらない）</summary>
        public bool IsPracticeTable => _practiceTable;
        /// <summary>練習用お宝を回収した（DeviceId = 割り当てられた端末、now）。1 つの練習用お宝につき 1 回だけ</summary>
        public System.Action<string, double> PracticeCollected { get; set; }
        /// <summary>回収した合計額（本編のお宝だけ）</summary>
        public int Total { get; private set; }
        /// <summary>適用した減額の合計（回ごと。#42 の「今回の損害」、#44 の記録の材料。表示は作らない）</summary>
        public int DamageTotal { get; private set; }
        /// <summary>減額の受け付けの記録の出口（実機調整 #45・#49 用。1 件ごと。null なら出さない）</summary>
        public System.Action<string> Log { get; set; }
        /// <summary>
        /// 降ろす先を決める関数（VenueRoot ローカルの位置 → 降ろす先。本番は ThVenue の Raycast。外から渡して純粋クラスのまま保つ）。
        /// 既定は y を 0 にするだけ（床）。
        /// </summary>
        public System.Func<Vector3, Vector3> PlaceSolver { get; set; } = p => new Vector3(p.x, 0f, p.z);
        void LogLine(string s) => Log?.Invoke("[HostDamage] " + s);

        /// <param name="clientNoOf">DeviceId から今の ClientNo を引く（再接続で ClientNo が付け替わっても ThGrabbed・ThItems に今の番号を載せるため）。引けなければ -1 を返す</param>
        public HostItemTable(IReadOnlyList<SpawnedTreasure> spawned, Bounds collectBounds, ThEmit emit, System.Func<string, int> clientNoOf = null, ThTreasureParams treasureParams = null, float placeSeconds = 0.3f, bool practiceTable = false)
        {
            _practiceTable = practiceTable;
            _tp = treasureParams ?? new ThTreasureParams();
            _placeSeconds = placeSeconds;
            _clientNoOf = clientNoOf;
            _collectBounds = collectBounds;
            _emit = emit;
            foreach (var s in spawned)
            {
                var row = new HostItem
                {
                    Practice = practiceTable,
                    Index = s.Index,
                    Kind = s.Kind,
                    BaseValue = s.Value,
                    Value = s.Value,
                    RestPos = s.LocalPosition,
                    RestRot = Quaternion.Euler(0f, s.YawDegrees, 0f),
                };
                _items.Add(row);
                _byIndex[row.Index] = row;
            }
        }

        public HostItem Get(int index) => _byIndex.TryGetValue(index, out var it) ? it : null;

        /// <summary>練習用でない表では練習用の行を扱わない（練習用の表の行は扱う）</summary>
        bool Blocked(HostItem it) => it.Practice && !_practiceTable;

        public bool InCollectZone(Vector3 localPos) => _collectBounds.Contains(localPos);

        static string Key(string device, int item, int reqId) => device + "|" + item + "|" + reqId;

        // ---- つかむ ----

        public void OnGrab(string deviceId, int clientNo, ThGrabMsg m)
        {
            var it = Get(m.item);
            if (it == null || m.reqId <= 0) return;
            if (_cancelled.Contains(Key(deviceId, m.item, m.reqId))) return;   // 取りやめ済み。認めない（返事も要らない）

            // 同じ申請の再送: すでに認めていれば同じ承認を送り直す（S は Held、M は予約中の Locked）
            if (it.OwnerDevice == deviceId && it.AcceptedReqId == m.reqId
                && ((it.Kind == TreasureKind.S && it.State == ItemState.Held) || (it.Kind == TreasureKind.M && it.State == ItemState.Locked)))
            {
                EmitGrabbed(it);
                return;
            }
            // 持ち上げ後の M への古い予約の再送は、何も返さない（持ち上げの再送は ThLift で受ける）
            if (it.Kind == TreasureKind.M && it.State == ItemState.Held && it.OwnerDevice == deviceId && it.AcceptedReqId == m.reqId) return;

            // お宝の種類と申請の種類が合うときだけ（S は kind=S、M は kind=M。L・秘宝は #40 の範囲外なので否認）
            bool kindOk = (it.Kind == TreasureKind.S && m.kind == (int)TreasureKind.S) || (it.Kind == TreasureKind.M && m.kind == (int)TreasureKind.M);
            bool ok = !it.Gone
                && kindOk
                && !Blocked(it)
                && (!it.Practice || it.PracticeDevice == deviceId)   // 練習用は割り当てられた端末だけ
                && it.State == ItemState.Resting
                && !InCollectZone(it.RestPos);
            if (!ok)
            {
                _emit(Rpc.ThGrabDenied, new ThGrabDeniedMsg { item = it.Index, clientNo = clientNo, reqId = m.reqId });
                return;
            }

            it.Gen++;
            // M は片手予約（Locked）。値段は公開せず、「持った」にも数えない。S は持ち主を認めた時点で Held・値段公開
            it.State = it.Kind == TreasureKind.M ? ItemState.Locked : ItemState.Held;
            it.OwnerDevice = deviceId;
            it.OwnerClientNo = clientNo;
            it.AcceptedReqId = m.reqId;
            it.ControllerDevice = deviceId;
            it.ControllerClientNo = clientNo;
            if (it.Kind == TreasureKind.S) it.Revealed = true;   // 以後ずっと公開
            it.ExemptReported = false;
            it.HasLastPos = false;   // 前の持ち主の位置を使わない
            EmitGrabbed(it);
        }

        // ---- M の両手成立 ----

        /// <summary>
        /// M の両手成立（設計書 4.4 の M 手順 3）。送り手が予約の持ち主で、申請ID・世代が一致し、Locked なら Held にして値段を公開する。
        /// 世代は変えない（4.2: +1 は持ち主を認めたとき・つかめるに戻したとき・回収・消滅だけ）。
        /// </summary>
        public void OnLift(string deviceId, int clientNo, ThLiftMsg m)
        {
            var it = Get(m.item);
            if (it == null || m.reqId <= 0) return;
            bool match = it.Kind == TreasureKind.M && !it.Gone && it.OwnerDevice != null && it.OwnerDevice == deviceId
                && it.AcceptedReqId == m.reqId && it.Gen == m.gen;
            if (match && it.State == ItemState.Locked)
            {
                it.State = ItemState.Held;
                it.Revealed = true;   // 以後ずっと公開
                it.ExemptReported = false;
                EmitLifted(it);
                return;
            }
            if (match && it.State == ItemState.Held) { EmitLifted(it); return; }   // 同じ申請の再送
            _emit(Rpc.ThGrabDenied, new ThGrabDeniedMsg { item = it.Index, clientNo = clientNo, reqId = m.reqId });
        }

        void EmitLifted(HostItem it)
        {
            it.OwnerClientNo = CurrentNo(it.OwnerDevice, it.OwnerClientNo);
            _emit(Rpc.ThLifted, new ThLiftedMsg { item = it.Index, clientNo = it.OwnerClientNo, reqId = it.AcceptedReqId, gen = it.Gen, value = it.Value });
        }

        /// <summary>
        /// その端末が片手予約中（Locked）のお宝をすべて「つかめる」に戻す（切断・見学への切り替え。設計書 3.3）。
        /// 持っている（Held 等の）お宝は <see cref="ForceRestItemsOf"/>。
        /// </summary>
        public void ReleaseReservationsOf(string deviceId)
        {
            if (deviceId == null) return;
            foreach (var it in _items)
                if (!it.Gone && it.State == ItemState.Locked && it.OwnerDevice == deviceId) FreeToRest(it);
        }

        /// <summary>
        /// その端末が持っている・判定役のお宝を、最後に受けた位置から降ろす先へそっと置かせる（切断・見学への切り替え。設計書 5章・3.3、F-GRAB-8）。
        /// 対象: 持ち主がその端末で Held（S、両手で持った後の M）か Flying / 持ち主なしで判定役がその端末の Falling（落下中に切れると誰も ThRest を送らないため）。
        /// 予約中（Locked）は <see cref="ReleaseReservationsOf"/> が受け持つ。
        /// 世代を +1 して Placing・持ち主なし・判定役なしにし、ThForceRest（降ろす先）を全員へ送る。〔置く時間〕の後は <see cref="Tick"/> が「つかめる」に戻す／回収する。
        /// </summary>
        public void ForceRestItemsOf(string deviceId, double now)
        {
            if (deviceId == null) return;
            foreach (var it in _items)
            {
                if (it.Gone) continue;
                bool held = it.OwnerDevice == deviceId && (it.State == ItemState.Held || it.State == ItemState.Flying);
                bool falling = it.OwnerDevice == null && it.ControllerDevice == deviceId && it.State == ItemState.Falling;
                if (!held && !falling) continue;
                var from = it.HasLastPos ? it.LastPos : it.RestPos;
                var rot = Quaternion.Euler(0f, (it.HasLastPos ? it.LastRot : it.RestRot).eulerAngles.y, 0f);   // 今の向きの Y 回転だけを残して立てる
                var dest = PlaceSolver != null ? PlaceSolver(from) : new Vector3(from.x, 0f, from.z);
                it.Gen++;
                it.State = ItemState.Placing;
                it.OwnerDevice = null;
                it.OwnerClientNo = -1;
                it.ControllerDevice = null;   // 判定役は運営PC（なし）。切れた端末の遅れた ThRest・ThCollect・ThPoses は捨てられる
                it.ControllerClientNo = -1;
                it.ExemptReported = false;    // Placing は状態で免除になる
                it.RestPos = dest;
                it.RestRot = rot;
                it.PlaceSince = now;
                LogLine($"切断などでそっと置く item={it.Index} device={deviceId} 降ろす先={dest}");
                _emit(Rpc.ThForceRest, new ThForceRestMsg { item = it.Index, gen = it.Gen, pos = dest, rot = rot });
            }
        }

        /// <summary>
        /// 毎フレーム。Placing（ForceRestItemsOf）になってから〔置く時間〕たったお宝を、Playing 中で降ろす先が荷台の範囲内なら回収、
        /// それ以外は「つかめる」に戻す（世代 +1、ThFreed）。
        /// </summary>
        public void Tick(double now, bool playing)
        {
            foreach (var it in _items)
            {
                if (it.Gone || it.State != ItemState.Placing || now - it.PlaceSince < _placeSeconds) continue;
                if (playing && !Blocked(it) && InCollectZone(it.RestPos)) Collect(it, now);   // 練習用の行は、練習用の表の中だけ回収する（合計に入れない）
                else FreeToRest(it);
            }
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
            bool reserve = it.State == ItemState.Locked;   // M の片手予約は値段を載せない
            _emit(Rpc.ThGrabbed, new ThGrabbedMsg { item = it.Index, clientNo = it.OwnerClientNo, reqId = it.AcceptedReqId, gen = it.Gen, value = reserve ? 0 : it.Value, reserve = reserve });
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

            if (m.reason == (int)ReleaseReason.Unreserve)
            {
                // M の予約解除: 持ち主・申請ID・世代が一致し、Locked のときだけ「つかめる」に戻す。それ以外は捨てる
                if (!isOwner || m.gen != it.Gen || it.State != ItemState.Locked) return;
                FreeToRest(it);
                return;
            }

            if (m.reason == (int)ReleaseReason.Dropped)
            {
                // 今の持ち主の申請ID・世代が一致するものだけ。世代は変えない（予約中は落とせない）
                if (!isOwner || m.gen != it.Gen || it.State == ItemState.Locked) return;
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
            it.HasLastPos = false;
            it.Gen++;
            _emit(Rpc.ThFreed, new ThFreedMsg { item = it.Index, gen = it.Gen, pos = it.RestPos, rot = it.RestRot });
        }

        /// <summary>判定役の「止まった」。世代が一致すれば 1 回だけ受理（受理すると世代が進むので、送り直しは世代違いで捨てられる）</summary>
        public void OnRest(string deviceId, ThRestMsg m, double now = 0)
        {
            var it = Get(m.item);
            if (it == null || it.Gone || Blocked(it)) return;
            if (it.ControllerDevice != deviceId || m.gen != it.Gen) return;
            if (it.State != ItemState.Held && it.State != ItemState.Falling) return;
            it.RestPos = m.pos;
            it.RestRot = m.rot;
            // 荷台の回収範囲の中に置かれた（落下が止まった）なら、回収として扱う
            if (InCollectZone(m.pos)) { Collect(it, now); return; }
            FreeToRest(it);
        }

        // ---- 回収 ----

        public void OnCollect(string deviceId, ThCollectMsg m, double now = 0)
        {
            var it = Get(m.item);
            if (it == null || it.Gone || Blocked(it)) return;
            if (it.ControllerDevice != deviceId || m.gen != it.Gen) return;
            if (it.State != ItemState.Held && it.State != ItemState.Falling && it.State != ItemState.Resting) return;
            Collect(it, now);
        }

        void Collect(HostItem it, double now)
        {
            it.Gone = true;
            it.State = ItemState.Collected;
            it.OwnerDevice = null;
            it.OwnerClientNo = -1;
            it.ExemptReported = false;
            it.Gen++;
            if (!it.Practice) Total += it.Value;   // 練習用は合計額に入れない（ThCollected の total は本編の合計のまま）
            _emit(Rpc.ThCollected, new ThCollectedMsg { item = it.Index, gen = it.Gen, total = Total, value = it.Value, damaged = it.Value < it.BaseValue });
            if (it.Practice) PracticeCollected?.Invoke(it.PracticeDevice, now);
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
                it.HasLastPos = true;
                it.LastPos = e.pos;
                it.LastRot = e.rot;
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

            // a の免除は送り手が知らせる（位置の送信より先に衝突の報告が届いても、減額しないため）。b の免除はホストが判断する
            // 送り手が a の判定役のときだけ（判定役でない送り手の exemptA で、他人のお宝の減額を止めさせない）
            if (m.exemptA && a != null && a.ControllerDevice == deviceId) LogLine($"適用しなかった item={m.a} 送り手の報告で免除中");
            else ApplyDamage(m.a, a, m.pos, m.speed);
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
