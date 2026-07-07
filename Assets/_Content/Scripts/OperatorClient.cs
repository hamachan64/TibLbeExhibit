using UnityEngine;

namespace STYLY.TIB_Template
{
    public class OperatorClient : MonoBehaviour
    {
        public void SendContentStart()
        {
            RpcManager.Instance.Send(Rpc.ContentStart);
        }
    }
}