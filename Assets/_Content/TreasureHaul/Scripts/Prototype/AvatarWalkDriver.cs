#if UNITY_EDITOR
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul.Prototype
{
    /// <summary>
    /// 試作(#43 の結合確認): 2 台目の端末役（クローン）の XR Rig を動かし、メインのエディタから見たアバターの頭・体・手が動くかを確かめる補助。
    /// 本番のシーンには置かない（ProtoBatchRunner が実行時に追加する）。何も保存しない。
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class AvatarWalkDriver : MonoBehaviour
    {
        public Vector3 Center = new Vector3(0.6f, 0f, 1.6f);
        public float Radius = 0.5f;
        public float Period = 8f;
        Transform _rig;

        void Awake()
        {
            // サーバーの自動探索がこの Mac の localhost を見つけられないことがあるので、直接指定する（シーンは変えない）
            var nm = FindFirstObjectByType<Styly.NetSync.NetSyncManager>();
            if (nm != null)
                typeof(Styly.NetSync.NetSyncManager).GetField("_serverAddress", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(nm, "localhost");
        }

        void Update()
        {
            if (_rig == null)
            {
                var go = GameObject.Find("STYLY XR Rig");
                if (go == null) return;
                _rig = go.transform;
            }
            float a = Time.time / Period * Mathf.PI * 2f;
            _rig.position = Center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Radius;
            _rig.rotation = Quaternion.Euler(0f, a * Mathf.Rad2Deg * 0.5f, 0f);   // 頭の向きも変える
        }
    }
}
#endif
