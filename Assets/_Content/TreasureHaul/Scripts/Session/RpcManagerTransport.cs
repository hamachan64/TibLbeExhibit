namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>本番の送信先。RpcManager（Core）で全員宛てに送る。</summary>
    public sealed class RpcManagerTransport : IThTransport
    {
        public void Send(Rpc rpc, string[] args)
        {
            if (RpcManager.Instance != null) RpcManager.Instance.Send(rpc, args);
        }
    }
}
