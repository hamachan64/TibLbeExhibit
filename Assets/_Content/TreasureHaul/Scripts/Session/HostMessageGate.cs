namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// ホストの受け付け条件の土台（設計書 6.1）。
    /// 「RoundId が今の回」かつ「送り手の ClientNo が今の参加端末のもので、その端末が今も参加できる」ときだけ true。
    /// #38 以降のメッセージごとの条件は、これを満たした上で足す。
    /// </summary>
    public sealed class HostMessageGate
    {
        readonly HostRoundMachine _machine;
        readonly DeviceRegistry _registry;

        public HostMessageGate(HostRoundMachine machine, DeviceRegistry registry)
        {
            _machine = machine;
            _registry = registry;
        }

        public bool Accept(long roundId, int senderClientNo)
        {
            if (_machine.State == RoundState.Waiting || _machine.RoundId == 0) return false;
            if (roundId != _machine.RoundId) return false;
            var deviceId = _registry.GetDeviceId(senderClientNo);
            if (deviceId == null) return false;
            if (!ContainsId(_machine.Participants, deviceId)) return false;
            return _registry.IsEligible(deviceId);
        }

        static bool ContainsId(System.Collections.Generic.IReadOnlyList<string> list, string id)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == id) return true;
            return false;
        }
    }
}
