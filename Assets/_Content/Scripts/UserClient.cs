using UnityEngine;
using Unity.XR.PICO.TOBSupport;

namespace STYLY.TIB_Template
{
    public class UserClient : MonoBehaviour
    {
        void Start()
        {
            PXR_Enterprise.InitEnterpriseService();
            PXR_Enterprise.BindEnterpriseService(bound =>
            {
                if (bound)
                {
                    int result = PXR_Enterprise.Recenter();
                    Debug.Log($"[UserClient] Recenter result: {result} (0=success, 1=failure)");

                    PXR_Enterprise.SwitchSystemFunction(
                        SystemFunctionSwitchEnum.SFS_GESTURE_RECOGNITION_HOME_ENABLE,
                        SwitchEnum.S_OFF);
                    PXR_Enterprise.SwitchSystemFunction(
                        SystemFunctionSwitchEnum.SFS_GESTURE_RECOGNITION_RESET_ENABLE,
                        SwitchEnum.S_OFF);
                }
                else
                {
                    Debug.LogWarning("[UserClient] Failed to bind enterprise service");
                }
            });
        }
    }
}