using UnityEngine;
using UnityEngine.Android;
using Unity.XR.PICO.TOBSupport;

namespace STYLY.TIB_Template
{
    public class UserClient : MonoBehaviour
    {
#if UNITY_ANDROID
        private const string HandTrackingPermission = "com.picovr.permission.HAND_TRACKING";
#endif

        void Awake()
        {
            RequestHandTrackingPermission();
        }

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

        private void RequestHandTrackingPermission()
        {
#if UNITY_ANDROID
            if (Permission.HasUserAuthorizedPermission(HandTrackingPermission))
            {
                return;
            }

            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += permission =>
                Debug.Log($"[UserClient] Permission granted: {permission}");
            callbacks.PermissionDenied += permission =>
                Debug.LogWarning($"[UserClient] Permission denied: {permission}（ハンドトラッキングが動作しません。設定アプリから手動で許可してください）");

            Permission.RequestUserPermission(HandTrackingPermission, callbacks);
#endif
        }
    }
}