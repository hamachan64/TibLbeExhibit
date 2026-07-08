using UnityEngine;

namespace STYLY.TIB_Template
{
    public class OperatorClient : MonoBehaviour
    {
        public void SendContentStart()
        {
            // 引数[0]: 具現化ポーカーのスポーン用乱数シード。
            // 全クライアントが同じシードを受け取り、アイテム配置を決定論的に一致させる。
            var seed = Random.Range(int.MinValue, int.MaxValue);
            RpcManager.Instance.Send(Rpc.ContentStart, new[] { seed.ToString() });
        }
    }
}
