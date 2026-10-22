using System.Collections.Generic;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>端末 1 台ぶんの記録。DeviceId が主キー。</summary>
    public sealed class DeviceInfo
    {
        public string DeviceId;
        /// <summary>今の ClientNo（切断中は -1）。再接続で変わる</summary>
        public int ClientNo = -1;
        public bool Connected;
        /// <summary>位置合わせ確認済み</summary>
        public bool AlignmentConfirmed;
        /// <summary>見学</summary>
        public bool Spectator;
        /// <summary>端末から報告されたパラメータ版（未報告 = null）</summary>
        public string ParamsVersion;
        /// <summary>端末から報告されたお宝カタログのハッシュ（未報告 = ""）</summary>
        public string CatalogHash = "";
        /// <summary>回の途中から入ったので、この回は参加しない（Waiting に戻ったら解除）</summary>
        public bool WaitNextRound;
        /// <summary>手の認識の台数（0 / 1 / 2）。未報告は -1（ThStatus。#44）</summary>
        public int HandCount = -1;
        /// <summary>電池残量（%）。未報告・取れない端末は -1（ThStatus。#44）</summary>
        public int BatteryPercent = -1;
        /// <summary>最後に ThStatus を受けた時刻（ホストの now）。未報告は負の無限大</summary>
        public double StatusAt = double.NegativeInfinity;
    }

    /// <summary>
    /// 端末一覧（設計書 3.3）。DeviceId を主キーに、今の ClientNo・接続中か・位置合わせ確認済みか・見学か・パラメータ版を持つ。
    /// </summary>
    public sealed class DeviceRegistry
    {
        readonly Dictionary<string, DeviceInfo> _byDevice = new Dictionary<string, DeviceInfo>();
        readonly Dictionary<int, string> _byClientNo = new Dictionary<int, string>();

        /// <summary>ホストが使っているパラメータ版。参加できる条件の一つ。</summary>
        public string HostParamsVersion { get; set; } = "";
        /// <summary>ホストのお宝カタログのハッシュ。参加できる条件の一つ（パラメータ版と同じ扱い）</summary>
        public string HostCatalogHash { get; set; } = "";

        /// <summary>
        /// 新しく登録された端末・再接続した端末を「位置合わせ確認済み」で始めるか（パラメータ operation.autoConfirmAlignment。
        /// エディタの ParrelSync 検証用）。false なら「未確認」で始まる。
        /// </summary>
        public bool AutoConfirm { get; set; }
        /// <summary>DeviceId → 端末番号の対応表（運営UIの並びと練習の色の順に使う。treasure_haul_devices.json）</summary>
        public ThDeviceNumbers Numbers { get; set; } = ThDeviceNumbers.Empty;

        public IEnumerable<DeviceInfo> Devices => _byDevice.Values;

        /// <summary>端末番号の順（未登録は後ろに DeviceId の文字列順）に並べた DeviceId</summary>
        public List<string> SortedDeviceIds()
        {
            var list = new List<string>(_byDevice.Keys);
            list.Sort(Numbers.Compare);
            return list;
        }

        /// <summary>接続中の端末数</summary>
        public int ConnectedCount
        {
            get { int n = 0; foreach (var d in _byDevice.Values) if (d.Connected) n++; return n; }
        }

        public DeviceInfo Get(string deviceId) =>
            deviceId != null && _byDevice.TryGetValue(deviceId, out var d) ? d : null;

        public string GetDeviceId(int clientNo) => _byClientNo.TryGetValue(clientNo, out var id) ? id : null;

        /// <summary>接続（または再接続・ClientNo の付け替え）。同じ DeviceId は ClientNo が変わっても同じ端末。何度呼んでもよい。</summary>
        public DeviceInfo Connect(string deviceId, int clientNo)
        {
            if (string.IsNullOrEmpty(deviceId) || clientNo <= 0) return null;

            // この ClientNo を別の端末が持っていたら、その端末は切断扱い（ClientNo は使い回される）
            if (_byClientNo.TryGetValue(clientNo, out var holder) && holder != deviceId) MarkDisconnected(holder);

            if (!_byDevice.TryGetValue(deviceId, out var d))
            {
                // 新しい端末は「未確認」で始まる（運営UIで確認するまで参加できない）。autoConfirm のときだけ確認済み
                d = new DeviceInfo { DeviceId = deviceId, AlignmentConfirmed = AutoConfirm, Spectator = false };
                _byDevice[deviceId] = d;
            }
            else
            {
                if (d.Connected && d.ClientNo != clientNo) _byClientNo.Remove(d.ClientNo);
                // 再接続した端末は「未確認のまま」（切断で未確認に戻している）。autoConfirm のときだけ確認済みで戻る
                if (!d.Connected) d.AlignmentConfirmed = AutoConfirm;
            }
            d.ClientNo = clientNo;
            d.Connected = true;
            _byClientNo[clientNo] = deviceId;
            return d;
        }

        /// <summary>切断。位置合わせ確認済みは「未確認」に戻す。</summary>
        public void Disconnect(int clientNo)
        {
            if (_byClientNo.TryGetValue(clientNo, out var id)) MarkDisconnected(id);
        }

        void MarkDisconnected(string deviceId)
        {
            if (!_byDevice.TryGetValue(deviceId, out var d)) return;
            if (d.ClientNo > 0 && _byClientNo.TryGetValue(d.ClientNo, out var cur) && cur == deviceId) _byClientNo.Remove(d.ClientNo);
            d.Connected = false;
            d.ClientNo = -1;
            d.AlignmentConfirmed = false;
            // 切断した端末の手・電池は「不明」に戻す
            d.HandCount = -1;
            d.BatteryPercent = -1;
            d.StatusAt = double.NegativeInfinity;
        }

        public void ReportParamsVersion(string deviceId, string version)
        {
            var d = Get(deviceId);
            if (d != null) d.ParamsVersion = version;
        }

        public void ReportCatalogHash(string deviceId, string hash)
        {
            var d = Get(deviceId);
            if (d != null) d.CatalogHash = hash ?? "";
        }

        /// <summary>ThStatus の受信。handCount は 0〜2（範囲外は不明 -1）、batteryPercent は 0〜100（範囲外は不明 -1）</summary>
        public void ReportStatus(string deviceId, int handCount, int batteryPercent, double now)
        {
            var d = Get(deviceId);
            if (d == null) return;
            d.HandCount = handCount >= 0 && handCount <= 2 ? handCount : -1;
            d.BatteryPercent = batteryPercent >= 0 && batteryPercent <= 100 ? batteryPercent : -1;
            d.StatusAt = now;
        }

        // #44 の運営UI（SessionHostCore.SetSpectator / SetAlignmentConfirmed）から使う。
        public void SetAlignmentConfirmed(string deviceId, bool confirmed) { var d = Get(deviceId); if (d != null) d.AlignmentConfirmed = confirmed; }
        public void SetSpectator(string deviceId, bool spectator) { var d = Get(deviceId); if (d != null) d.Spectator = spectator; }

        public void SetWaitNextRound(string deviceId, bool wait) { var d = Get(deviceId); if (d != null) d.WaitNextRound = wait; }

        /// <summary>Waiting に戻ったとき、全端末の「次の回を待つ」を解除する。</summary>
        public void ClearWaitNextRound() { foreach (var d in _byDevice.Values) d.WaitNextRound = false; }

        /// <summary>開始時に参加端末へ入れてよい端末 = 接続中・確認済み・見学でない・次の回を待つでない（版は問わない）</summary>
        public bool IsJoinable(string deviceId)
        {
            var d = Get(deviceId);
            return d != null && d.Connected && d.AlignmentConfirmed && !d.Spectator && !d.WaitNextRound;
        }

        /// <summary>参加できる端末 = 開始時の条件に加えて、報告されたパラメータ版がホストの版と一致</summary>
        public bool IsEligible(string deviceId)
        {
            var d = Get(deviceId);
            return IsJoinable(deviceId) && d.ParamsVersion != null && d.ParamsVersion == HostParamsVersion
                && d.CatalogHash == HostCatalogHash;
        }

        public List<string> JoinableDeviceIds()
        {
            var list = new List<string>();
            foreach (var d in _byDevice.Values) if (IsJoinable(d.DeviceId)) list.Add(d.DeviceId);
            list.Sort(System.StringComparer.Ordinal);
            return list;
        }

        public List<string> EligibleDeviceIds()
        {
            var list = new List<string>();
            foreach (var d in _byDevice.Values) if (IsEligible(d.DeviceId)) list.Add(d.DeviceId);
            list.Sort(System.StringComparer.Ordinal);
            return list;
        }

        public int EligibleCount
        {
            get { int n = 0; foreach (var d in _byDevice.Values) if (IsEligible(d.DeviceId)) n++; return n; }
        }
    }
}
