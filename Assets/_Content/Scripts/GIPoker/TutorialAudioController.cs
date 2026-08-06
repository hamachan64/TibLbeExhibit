using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using R3;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// GameSession.State の遷移（previous→next）から、発火すべき音響アクションを決定する
    /// 純ロジック（MonoBehaviour・DOTween・AudioSource 非依存）。
    /// TutorialAudioController から呼ばれ、EditModeテストで単独検証できる
    /// （TransitionPlanner / TransitionDirector と同じ設計）。
    /// </summary>
    public enum TutorialAudioAction
    {
        /// <summary>何もしない</summary>
        None,
        /// <summary>他状態 → Tutorial: TutorialBgmClip をループ再生開始する</summary>
        EnterTutorial,
        /// <summary>Tutorial → Playing: BGMフェードアウト→開始の掛け声→本編BGM開始のシーケンスを行う</summary>
        ExitTutorialToPlaying,
    }

    /// <summary>
    /// GameSession.State を購読し、チュートリアル/本編の音響（BGM切り替え・開始の掛け声）を
    /// オーケストレーションする（GIPokerBootstrap から結線する）。
    ///
    /// 状態遷移と演出（<see cref="PlanAction"/> が決定。TransitionDirector と同じ
    /// 「previousState を見て実際の遷移だけ処理する」パターンに倣う）:
    /// - 他状態 → Tutorial: TutorialBgmClip をループ再生開始
    /// - Tutorial → Playing: TutorialBgmClip をフェードアウトして停止 →
    ///   SessionStartVoiceClip（掛け声）を1回再生 → 再生完了後 MainBgmClip をループ再生開始
    /// - それ以外の遷移（skipTutorial による WaitingForStart→Playing 直行、Finished への
    ///   遷移等）では何も行わない
    ///
    /// 安全性:
    /// - 状態変化のたびに走行中のシーケンスを CancellationTokenSource の張り替えでキャンセルしてから
    ///   開始するため、短時間での連続状態変化（多重発火）でも音が二重に鳴らない
    /// - 破棄時は破棄時点で鳴っているBGM/SEを停止する（連続稼働のLBEでセッションをまたいで
    ///   音が残らないようにする）
    /// - BGM再生用の AudioSource は Tutorial/本編で共用する（次のセッションの EnterTutorial で
    ///   自動的に前セッションの本編BGMを上書き停止するため、セッションをまたいだ多重再生も起きない）
    /// </summary>
    public class TutorialAudioController : MonoBehaviour
    {
        [SerializeField, Tooltip("チュートリアルBGMの再生音量（各StepのVoiceClipの聞き取りを妨げない小さめの値）")]
        private float tutorialBgmVolume = 0.15f;

        [SerializeField, Tooltip("本編BGMの再生音量")]
        private float mainBgmVolume = 0.2f;

        [SerializeField, Tooltip("Tutorial→Playing遷移時、チュートリアルBGMをフェードアウトする所要時間（秒）")]
        private float bgmFadeOutDuration = 0.5f;

        private AudioSource bgmSource;
        private AudioSource seSource;

        private SessionState? previousState;
        private CancellationTokenSource sequenceCts;
        private Tween bgmFadeTween;

        /// <summary>
        /// 状態遷移（previous→next）から発火すべき音響アクションを判定する。
        /// previous == next（購読直後の現在値流入など）は None を返す。
        /// </summary>
        public static TutorialAudioAction PlanAction(SessionState previous, SessionState next)
        {
            if (previous == next)
            {
                return TutorialAudioAction.None;
            }
            if (next == SessionState.Tutorial)
            {
                return TutorialAudioAction.EnterTutorial;
            }
            if (previous == SessionState.Tutorial && next == SessionState.Playing)
            {
                return TutorialAudioAction.ExitTutorialToPlaying;
            }
            return TutorialAudioAction.None;
        }

        /// <summary>GIPokerBootstrap から結線する</summary>
        public void Init(GameSession session)
        {
            bgmSource = gameObject.AddComponent<AudioSource>();
            bgmSource.playOnAwake = false;
            bgmSource.spatialBlend = 0f; // 2D再生（ノイズの多い展示環境でも聞き取りやすくする）
            bgmSource.loop = true;

            seSource = gameObject.AddComponent<AudioSource>();
            seSource.playOnAwake = false;
            seSource.spatialBlend = 0f;
            seSource.loop = false;

            session.State
                .Subscribe(OnStateChanged)
                .AddTo(this);
        }

        /// <summary>
        /// R3 の ReactiveProperty は購読直後に現在値を1回流すため、初回の通知は
        /// 「前状態」を確定させるだけで演出は行わない（起動直後に誤発火させないため）。
        /// </summary>
        private void OnStateChanged(SessionState next)
        {
            if (previousState == null)
            {
                previousState = next;
                return;
            }

            var previous = previousState.Value;
            previousState = next;

            var action = PlanAction(previous, next);
            if (action == TutorialAudioAction.None)
            {
                return;
            }

            RestartSequenceCts();
            RunActionAsync(action, sequenceCts.Token).Forget();
        }

        private async UniTask RunActionAsync(TutorialAudioAction action, CancellationToken ct)
        {
            try
            {
                switch (action)
                {
                    case TutorialAudioAction.EnterTutorial:
                        PlayBgmLoop(GIPokerAssets.TutorialBgmClip, tutorialBgmVolume);
                        break;
                    case TutorialAudioAction.ExitTutorialToPlaying:
                        await FadeOutAndStopBgmAsync(ct);
                        await PlayOneShotAndWaitAsync(GIPokerAssets.SessionStartVoiceClip, ct);
                        PlayBgmLoop(GIPokerAssets.MainBgmClip, mainBgmVolume);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                // 新しい状態遷移や破棄によるキャンセルは正常系
                // （多重発火時に古いシーケンスを打ち切るための想定挙動）
            }
        }

        /// <summary>BGM用AudioSourceでクリップをループ再生する（前の再生は即座に止めて切り替える）</summary>
        private void PlayBgmLoop(AudioClip clip, float volume)
        {
            bgmFadeTween?.Kill();
            if (bgmSource == null)
            {
                return;
            }
            bgmSource.Stop();
            if (clip == null)
            {
                return;
            }
            bgmSource.clip = clip;
            bgmSource.volume = volume;
            bgmSource.loop = true;
            bgmSource.Play();
        }

        /// <summary>チュートリアルBGMを音量0までフェードしてから停止する</summary>
        private async UniTask FadeOutAndStopBgmAsync(CancellationToken ct)
        {
            bgmFadeTween?.Kill();
            if (bgmSource == null)
            {
                return;
            }
            if (!bgmSource.isPlaying || bgmFadeOutDuration <= 0f)
            {
                bgmSource.Stop();
                return;
            }

            var from = bgmSource.volume;
            bgmFadeTween = DOVirtual.Float(from, 0f, bgmFadeOutDuration, v =>
                {
                    if (bgmSource != null)
                    {
                        bgmSource.volume = v;
                    }
                })
                .SetEase(Ease.InQuad)
                .SetLink(gameObject);

            await WaitTweenAsync(bgmFadeTween, ct);
            if (bgmSource != null)
            {
                bgmSource.Stop();
            }
        }

        /// <summary>SE用AudioSourceでクリップを1回再生し、再生完了まで待つ</summary>
        private async UniTask PlayOneShotAndWaitAsync(AudioClip clip, CancellationToken ct)
        {
            if (seSource == null || clip == null)
            {
                return;
            }
            seSource.Stop();
            seSource.clip = clip;
            seSource.Play();

            await UniTask.WaitUntil(() => seSource == null || !seSource.isPlaying, cancellationToken: ct);
        }

        /// <summary>
        /// Tween の完了を UniTask で待つ。
        /// 本プロジェクトの DOTween は DLL プラグイン（Assets/Plugins/Demigiant）のため
        /// UNITASK_DOTWEEN_SUPPORT シンボルが定義されず、UniTask の
        /// <c>Tween.ToUniTask()</c> 拡張は使えない。代わりに Tween 非依存の
        /// ポーリング待機を行う（完了時は autoKill により IsActive() が false になる）。
        /// キャンセル時は Tween を Kill してから例外を伝播する。
        /// </summary>
        private static async UniTask WaitTweenAsync(Tween tween, CancellationToken ct)
        {
            try
            {
                await UniTask.WaitUntil(
                    () => tween == null || !tween.IsActive(),
                    cancellationToken: ct);
            }
            catch (OperationCanceledException)
            {
                if (tween != null && tween.IsActive())
                {
                    tween.Kill();
                }
                throw;
            }
        }

        private void RestartSequenceCts()
        {
            sequenceCts?.Cancel();
            sequenceCts?.Dispose();
            sequenceCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        }

        private void OnDestroy()
        {
            sequenceCts?.Cancel();
            sequenceCts?.Dispose();
            bgmFadeTween?.Kill();

            // 連続稼働のLBEでセッションを跨いで音が残らないようにする
            if (bgmSource != null)
            {
                bgmSource.Stop();
            }
            if (seSource != null)
            {
                seSource.Stop();
            }
        }
    }
}
