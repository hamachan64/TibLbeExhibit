using UnityEngine;
using Styly.NetSync.Utility;

namespace STYLY.TIB_Template
{
    [DefaultExecutionOrder(-100)]
    public class VariableManager : VariableManagerBase<GlobalVariable, UserVariable>
    {
        public static VariableManager Instance { get; private set; }
    
        void Awake()
        {
            Instance = this;
        }
    }
}
