using TMPro;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// 具現化ポーカーのエントリーポイント（UserClient シーンに1つ配置する）。
    ///
    /// STYLY XR Rig はランタイムでリグを生成するため、ハンドトラッキング関連の
    /// コンポーネント群もすべてここからランタイムで組み立てる。
    /// シーンに事前配置が必要なのはこの Bootstrap（＋アセット参照）だけ。
    ///
    /// 生成するもの:
    /// - 左右の手: HandPinchTracker / CardGrabInteractor / FingertipTouchCollider
    /// - ゲイン判定: GainGestureDetector（両手引き裂きジェスチャー）
    /// - バインダー: CardBinder ＋ BinderView（腰元に追従する見開きブック）
    /// - リザルト: ResultView（タイムアップで表示）
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class GIPokerBootstrap : MonoBehaviour
    {
        [Header("共有アセット（GIPokerAssets へ流し込む）")]
        [SerializeField, Tooltip("日本語対応TMPフォント（Noto Sans JP SDF）")]
        private TMP_FontAsset japaneseFont;
        [SerializeField, Tooltip("浮遊カードのキラキラVFX")]
        private GameObject cardSparkleVfx;
        [SerializeField, Tooltip("カード化した瞬間のVFX")]
        private GameObject cardifyVfx;
        [SerializeField, Tooltip("ゲイン（具現化）した瞬間のVFX")]
        private GameObject gainVfx;
        [SerializeField, Tooltip("1分ルール消滅時のVFX")]
        private GameObject expireVfx;
        [SerializeField, Tooltip("リスキーダイス大吉VFX")]
        private GameObject diceBlessVfx;
        [SerializeField, Tooltip("リスキーダイス大凶VFX")]
        private GameObject diceCurseVfx;

        private void Awake()
        {
            GIPokerAssets.JapaneseFont = japaneseFont;
            GIPokerAssets.CardSparkleVfx = cardSparkleVfx;
            GIPokerAssets.CardifyVfx = cardifyVfx;
            GIPokerAssets.GainVfx = gainVfx;
            GIPokerAssets.ExpireVfx = expireVfx;
            GIPokerAssets.DiceBlessVfx = diceBlessVfx;
            GIPokerAssets.DiceCurseVfx = diceCurseVfx;
        }

        private void Start()
        {
            // セッション管理（未配置なら自動追加）
            var session = GameSession.Instance != null ? GameSession.Instance : gameObject.AddComponent<GameSession>();

            // 左右の手
            var (leftTracker, leftGrab) = CreateHand(Handedness.Left);
            var (rightTracker, rightGrab) = CreateHand(Handedness.Right);

            // ゲイン（引き裂き）ジェスチャー
            var gainDetector = gameObject.AddComponent<GainGestureDetector>();
            gainDetector.Initialize(leftTracker, leftGrab, rightTracker, rightGrab);

            // カード効果（シーン上の設定済みコンポーネントを優先）
            var effectService = GetComponent<CardEffectService>();
            if (effectService == null)
            {
                effectService = gameObject.AddComponent<CardEffectService>();
                Debug.LogWarning("[GIPokerBootstrap] CardEffectService が未配置のため既定設定で生成しました（錬金プール・SSプレハブは未設定）");
            }
            effectService.BindTo(gainDetector);

            // バインダー
            var binderGo = new GameObject("GIPoker_Binder");
            var binder = binderGo.AddComponent<CardBinder>();
            var binderView = binderGo.AddComponent<BinderView>();
            binderView.Init(binder);

            // リザルト
            var resultGo = new GameObject("GIPoker_ResultView");
            var resultView = resultGo.AddComponent<ResultView>();
            resultView.Init(session, binder);

            // カード状態のマルチユーザー同期
            var networkSync = gameObject.AddComponent<CardNetworkSync>();
            networkSync.Initialize(effectService, binder);
        }

        private (HandPinchTracker tracker, CardGrabInteractor grab) CreateHand(Handedness handedness)
        {
            // SetActive(false) の状態でコンポーネントを揃えて Initialize してから
            // 有効化することで、Awake 前に handedness を確定させる
            var go = new GameObject($"GIPoker_{handedness}Hand");
            go.transform.SetParent(transform, false);
            go.SetActive(false);

            var tracker = go.AddComponent<HandPinchTracker>();
            tracker.Initialize(handedness);

            var grab = go.AddComponent<CardGrabInteractor>();

            var fingertip = go.AddComponent<FingertipTouchCollider>();
            fingertip.Initialize(handedness);

            go.SetActive(true);
            return (tracker, grab);
        }
    }
}
