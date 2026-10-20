namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 送信の差し替え口。本番は RpcManager（全員宛て）、テストはメモリ上の偽の通信。
    /// 設計書 6章: RPC はすべて全員宛てで、特定の端末向けの内容は引数の ClientNo で表す。
    /// </summary>
    public interface IThTransport
    {
        void Send(Rpc rpc, string[] args);
    }
}
