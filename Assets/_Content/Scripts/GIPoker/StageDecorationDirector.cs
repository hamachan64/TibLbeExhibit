using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// ステージ装飾要素（台座・境界魔法陣・環境パーティクル）のフェーズ演出を統括する
    /// コンポーネント（Environment シーンの装飾ルートに配置）。
    ///
    /// StageDirector（Issue #7 実装）への依存は MR_STAGE_PLAN.md §6.1 のIF契約
    /// （<c>StageDirector.Instance</c> / <c>Phase</c> / <c>ErosionProgress</c>）のみに限定し、
    /// 各装飾コンポーネントは StageDirector を一切知らない。
    /// 依存の集約により、#7 の実装詳細が変わっても影響がこの1クラスに閉じる。
    ///
    /// - Phase の変化 → 各要素の EnterPhase() を呼び分ける（生える/崩れる/フェードの演出付き遷移）
    /// - ErosionProgress → BorderCircle の増光へ転送（連続値の演出）
    /// - 初回購読時（シーンロード直後に流れる現在値）のみ immediate=true で
    ///   アニメーションなしの状態合わせを行う。以降の遷移は必ず演出を通す
    ///   （ポップイン/アウト禁止、MR_STAGE_PLAN.md §4-5）
    /// - 台座の迫り上がり/沈み込みは個体ごとに発生をずらし（pedestalStagger）、
    ///   「島が順に目覚めていく」印象を作る
    /// </summary>
    public class StageDecorationDirector : MonoBehaviour
    {
        [SerializeField, Tooltip("制御対象の台座（空の場合は子階層から自動収集）")]
        private StagePedestal[] pedestals;

        [SerializeField, Tooltip("制御対象の境界魔法陣（未設定の場合は子階層から自動収集）")]
        private BorderCircle borderCircle;

        [SerializeField, Tooltip("制御対象の環境パーティクル（未設定の場合は子階層から自動収集）")]
        private AmbientMotes ambientMotes;

        [SerializeField, Tooltip("台座の演出開始を個体ごとにずらす間隔（秒）")]
        private float pedestalStagger = 0.15f;

        /// <summary>初回購読（現在値の流入）を即時反映として扱うためのフラグ</summary>
        private bool firstPhaseApplied;

        private void Awake()
        {
            if (pedestals == null || pedestals.Length == 0)
            {
                pedestals = GetComponentsInChildren<StagePedestal>(includeInactive: true);
            }
            if (borderCircle == null)
            {
                borderCircle = GetComponentInChildren<BorderCircle>(includeInactive: true);
            }
            if (ambientMotes == null)
            {
                ambientMotes = GetComponentInChildren<AmbientMotes>(includeInactive: true);
            }
        }

        private void Start()
        {
            WaitAndBindAsync(destroyCancellationToken).Forget();
        }

        private async UniTask WaitAndBindAsync(CancellationToken ct)
        {
            // StageDirector は Issue #7 が Environment シーンへ配置する。
            // 生成順・シーンロード順に依存しないよう ItemSpawnManager と同じ待ち方をする
            await UniTask.WaitUntil(() => StageDirector.Instance != null, cancellationToken: ct);

            StageDirector.Instance.Phase
                .Subscribe(ApplyPhase)
                .AddTo(this);

            StageDirector.Instance.ErosionProgress
                .Subscribe(erosion => borderCircle?.SetErosion(erosion))
                .AddTo(this);
        }

        /// <summary>フェーズを各装飾要素へ配信する</summary>
        private void ApplyPhase(StagePhase phase)
        {
            // ReactiveProperty の購読直後は「現在値」が即座に流れてくる。
            // これはフェーズ遷移ではなく初期状態合わせなので、演出なしで反映する
            var immediate = !firstPhaseApplied;
            firstPhaseApplied = true;

            for (var i = 0; i < pedestals.Length; i++)
            {
                if (pedestals[i] != null)
                {
                    pedestals[i].EnterPhase(phase, immediate ? 0f : pedestalStagger * i, immediate);
                }
            }

            if (borderCircle != null)
            {
                borderCircle.EnterPhase(phase, immediate);
            }

            if (ambientMotes != null)
            {
                ambientMotes.EnterPhase(phase, immediate);
            }
        }
    }
}
