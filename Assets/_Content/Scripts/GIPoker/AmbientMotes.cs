using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// 木漏れ日・胞子風の環境パーティクル（AmbientMotes）の制御コンポーネント。
    /// Eroded フェーズでフェードイン、Receding でフェードアウトする。
    ///
    /// ポップイン/アウト対策（MR_STAGE_PLAN.md §4-5）はパーティクル自身の
    /// Color over Lifetime（各粒が生まれてから消えるまで滑らかに明滅する設定を
    /// Editor セットアップスクリプトが焼き込む）で実現する:
    /// - 出現: Play() 後は粒が1つずつ湧いてそれぞれフェードインする（全体が瞬時に現れない）
    /// - 消滅: Stop(StopEmitting) で新規放出だけを止め、既存の粒は寿命どおり漂って消える
    /// このため DOTween によるマテリアル操作は不要で、負荷も追加されない。
    ///
    /// パフォーマンス設計（PICO 72fps前提）は <see cref="GIPokerStageDecorationSetup"/> の
    /// パーティクル設定値と、その根拠コメントを参照。
    ///
    /// StageDirector には直接依存せず、<see cref="StageDecorationDirector"/> から
    /// <see cref="EnterPhase"/> の呼び出しを受ける。
    /// </summary>
    [DisallowMultipleComponent]
    public class AmbientMotes : MonoBehaviour
    {
        [SerializeField, Tooltip("制御対象のパーティクルシステム")]
        private ParticleSystem motes;

        /// <summary>
        /// StageDecorationDirector からフェーズ変化の通知を受けて放出を切り替える。
        /// </summary>
        /// <param name="phase">遷移先のステージフェーズ</param>
        /// <param name="immediate">true の場合は途中経過を飛ばして最終状態へ反映する（初回購読時のみ使用）</param>
        public void EnterPhase(StagePhase phase, bool immediate = false)
        {
            if (motes == null)
            {
                return;
            }

            switch (phase)
            {
                case StagePhase.Eroded:
                case StagePhase.Unstable:
                    if (!motes.isEmitting)
                    {
                        motes.Play();
                        if (immediate)
                        {
                            // シーン途中参加などで即時反映する場合は事前シミュレートして
                            // 「粒が湧き始めるまでの空白」を飛ばす
                            motes.Simulate(5f, withChildren: true, restart: true);
                            motes.Play();
                        }
                    }
                    break;
                case StagePhase.Reality:
                case StagePhase.Awakening:
                case StagePhase.Receding:
                    if (immediate)
                    {
                        motes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                    else if (motes.isEmitting)
                    {
                        // 既存の粒は寿命どおりフェードアウトして消える（自然消滅）
                        motes.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    }
                    break;
            }
        }
    }
}
