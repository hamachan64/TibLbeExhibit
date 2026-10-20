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

        public IEnumerable<DeviceInfo> Devices => _byDevice.Values;

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
                // TODO(#44): 位置合わせ確認と見学を切り替える UI ができたら、新規登録は「未確認」始まりに変える。
                //            #44 までは UI が無いので「確認済み・見学でない」で始める。
                d = new DeviceInfo { DeviceId = deviceId, AlignmentConfirmed = true, Spectator = false };
                _byDevice[deviceId] = d;
            }
            else
            {
                if (d.Connected && d.ClientNo != clientNo) _byClientNo.Remove(d.ClientNo);
                if (!d.Connected)
                {
                    // TODO(#44): 位置合わせ確認 UI ができたら、再接続した端末は「未確認のまま」にする（切断で未確認に戻す処理は今のまま）。
                    //            #44 までは UI が無く復帰できなくなるので、再接続したら「確認済み」に戻す。
                    d.AlignmentConfirmed = true;
                }
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

        // #44 の UI から使う。
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
