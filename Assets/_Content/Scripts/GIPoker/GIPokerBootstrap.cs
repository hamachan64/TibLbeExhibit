using System;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Hands;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

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
    /// - 左右の手: HandPinchTracker / CardGrabInteractor / FingertipTouchCollider / BinderSummonGestureDetector
    /// - ゲイン判定: GainGestureDetector（両手引き裂きジェスチャー）
    /// - バインダー呼び出し: どちらかの手のLサイン（フィンガーガン）ポーズで BinderView.Toggle() を実行
    ///   （白煙＋衝撃波VFX付きで出し入れ。連打・両手同時成立によるチャタリングは ThrottleFirst で抑止）
    ///   ＋ エディタ実行時限定のデバッグ用: Spaceキーでも BinderView.Toggle() を実行可能
    /// - バインダー: CardBinder ＋ BinderView（腰元に追従する見開きブック。既定は非表示）
    /// - タイトル: TitleView（開始待ち中に表示するタイトル画面）
    /// - VR空間: TutorialSpaceDirector（チュートリアル中だけ包む「ゲーム開始の間」）
    /// - MR⇔VR遷移: TransitionDirector（白フェード＋VR空間の表示制御。ScreenFaderは内部生成）
    /// - リザルト: ResultView（タイムアップで表示）
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class GIPokerBootstrap : MonoBehaviour
    {
        /// <summary>Lサインジェスチャーによるバインダー出し入れの連打・チャタリング防止クールダウン</summary>
        private static readonly TimeSpan BinderSummonCooldown = TimeSpan.FromSeconds(0.6);

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
        [SerializeField, Tooltip("バインダーの3Dモデル（ページめくり可能な本、Blender製FBX）")]
        private GameObject cardBinderModelPrefab;
        [SerializeField, Tooltip("バインダー出し入れ時の白煙＋衝撃波VFX（VFX_BinderSummonPoof）")]
        private GameObject binderSmokeVfx;

        private void Awake()
        {
            GIPokerAssets.JapaneseFont = japaneseFont;
            GIPokerAssets.CardSparkleVfx = cardSparkleVfx;
            GIPokerAssets.CardifyVfx = cardifyVfx;
            GIPokerAssets.GainVfx = gainVfx;
            GIPokerAssets.ExpireVfx = expireVfx;
            GIPokerAssets.DiceBlessVfx = diceBlessVfx;
            GIPokerAssets.DiceCurseVfx = diceCurseVfx;
            GIPokerAssets.CardBinderModelPrefab = cardBinderModelPrefab;
            GIPokerAssets.BinderSmokeVfx = binderSmokeVfx;
        }

        private void Start()
        {
            // セッション管理（未配置なら自動追加）
            var session = GameSession.Instance != null ? GameSession.Instance : gameObject.AddComponent<GameSession>();

            // 左右の手
            var (leftTracker, leftGrab, leftSummon) = CreateHand(Handedness.Left);
            var (rightTracker, rightGrab, rightSummon) = CreateHand(Handedness.Right);

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

            // バインダー呼び出し（Lサイン＝フィンガーガンポーズ）。
            // 両手同時成立や連打でも1回のトグルに収まるよう ThrottleFirst でクールダウンを設ける。
            Observable.Merge(leftSummon.PoseEntered, rightSummon.PoseEntered)
                .ThrottleFirst(BinderSummonCooldown)
                .Subscribe(_ => binderView.Toggle())
                .AddTo(this);

#if UNITY_EDITOR
            // エディタ実行時のデバッグ用: Spaceキーでバインダーを出し入れする（Playモード上での動作確認専用）。
            // 実機（PICO / Meta Quest）はハンドトラッキング専用でキーボードが無いためビルドには含めない。
            // Lサインジェスチャー側のクールダウン購読とは独立させ、互いの挙動に影響しないようにする。
            Observable.EveryUpdate()
                .Where(_ => Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
                .Subscribe(_ => binderView.Toggle())
                .AddTo(this);
#endif

            // タイトル
            var titleGo = new GameObject("GIPoker_TitleView");
            var titleView = titleGo.AddComponent<TitleView>();
            titleView.Init(session);

            // チュートリアル（本編開始前の操作説明。Tutorial 状態の間だけ動作する）
            var tutorialGo = new GameObject("GIPoker_Tutorial");
            var tutorialDirector = tutorialGo.AddComponent<TutorialDirector>();
            tutorialDirector.Init(session, binder);

            // VR空間（チュートリアル中だけプレイヤーを包む「ゲーム開始の間」。Issue #13）
            var tutorialSpace = TutorialSpaceDirector.Create();

            // MR⇔VR遷移（白フェード＋VR空間の表示/非表示をオーケストレーション。Issue #14）
            var transitionGo = new GameObject("GIPoker_Transition");
            var transitionDirector = transitionGo.AddComponent<TransitionDirector>();
            transitionDirector.Init(session, tutorialSpace);

            // リザルト
            var resultGo = new GameObject("GIPoker_ResultView");
            var resultView = resultGo.AddComponent<ResultView>();
            resultView.Init(session, binder);

            // カード状態のマルチユーザー同期
            var networkSync = gameObject.AddComponent<CardNetworkSync>();
            networkSync.Initialize(effectService, binder);
        }

        private (HandPinchTracker tracker, CardGrabInteractor grab, BinderSummonGestureDetector summonGesture) CreateHand(Handedness handedness)
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

            var summonGesture = go.AddComponent<BinderSummonGestureDetector>();
            summonGesture.Initialize(handedness);

            go.SetActive(true);
            return (tracker, grab, summonGesture);
        }
    }
}
