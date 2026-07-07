using UnityEngine;
using Styly.NetSync.Utility;

namespace STYLY.TIB_Template
{
    [DefaultExecutionOrder(-100)]
    public class RpcManager : RpcManagerBase<Rpc>
    {
        public static RpcManager Instance { get; private set; }
    
        void Awake()
        {
            Instance = this;
        }
    }
}
