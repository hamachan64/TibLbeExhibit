using Cysharp.Threading.Tasks;
using Styly.NetSync;
using TMPro;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// リモートアバター（STYLY Default Avatar A.prefab）の頭上にニックネームを表示する。
    ///
    /// NetSyncAvatar.ClientNo はリモートアバターでは生成直後の InitializeRemote で即確定するが、
    /// ローカルアバターでは NetSyncManager からの割り当てを Update で待つ非同期の値になっている。
    /// そのため確定を UniTask でポーリング待機してから生成する。
    /// 自分自身（IsLocalAvatar）には表示しない。
    /// </summary>
    [RequireComponent(typeof(NetSyncAvatar))]
    public class PlayerNameTagView : MonoBehaviour
    {
        private const string HeadTrackerName = "Head Tracker";
        private const float HeightAboveHead = 0.3f;
        private const float FontSize = 24f;

        private NetSyncAvatar avatar;
        private Transform tagTransform;

        private void Awake()
        {
            avatar = GetComponent<NetSyncAvatar>();
        }

        private void Start()
        {
            WaitForClientNoAndCreateAsync(destroyCancellationToken).Forget();
        }

        private async UniTaskVoid WaitForClientNoAndCreateAsync(System.Threading.CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && avatar != null && avatar.ClientNo <= 0)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct).SuppressCancellationThrow();
                if (ct.IsCancellationRequested)
                {
                    return;
                }
            }

            if (ct.IsCancellationRequested || avatar == null || avatar.IsLocalAvatar)
            {
                // 自分自身のアバターには表示しない
                return;
            }

            CreateNameTag(avatar.ClientNo);
        }

        private void CreateNameTag(int clientNo)
        {
            var headTracker = transform.Find(HeadTrackerName);
            var parent = headTracker != null ? headTracker : transform;

            var go = new GameObject("PlayerNameTag");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, HeightAboveHead, 0f);
            go.transform.localScale = Vector3.one * 0.01f;

            var label = go.AddComponent<TextMeshPro>();
            if (GIPokerAssets.JapaneseFont != null)
            {
                label.font = GIPokerAssets.JapaneseFont;
            }
            label.text = PlayerNickname.Format(clientNo);
            label.fontSize = FontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.rectTransform.sizeDelta = new Vector2(4f, 1f);

            tagTransform = go.transform;
        }

        private void LateUpdate()
        {
            if (tagTransform == null)
            {
                return;
            }
            var cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            // BinderView.TryComputeFollowTarget の flatForward パターンを踏襲し、
            // ヨー軸のみでカメラ方向へビルボードする（ピッチをつけて傾かせない）。
            var flatForward = Vector3.ProjectOnPlane(tagTransform.position - cam.transform.position, Vector3.up).normalized;
            if (flatForward.sqrMagnitude < 0.001f)
            {
                flatForward = tagTransform.forward;
            }
            tagTransform.rotation = Quaternion.LookRotation(flatForward);
        }
    }
}
