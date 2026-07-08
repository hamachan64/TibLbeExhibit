using R3;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// 「素手で触れるとカード化する」G.I.のアイテムが実装すべきインターフェース。
    /// 状態はすべて R3 の ReadOnlyReactiveProperty として公開し、
    /// UI・演出・ネットワーク同期などの購読側はこれを Subscribe して反応する。
    /// </summary>
    public interface ICardizable
    {
        /// <summary>現在の状態（Item → FloatingCard → Stored / Expired / Gained）</summary>
        ReadOnlyReactiveProperty<CardizableState> State { get; }

        /// <summary>1分ルールの残り時間（秒）。カード化されるまでは 0</summary>
        ReadOnlyReactiveProperty<float> RemainingLifetime { get; }

        /// <summary>カウントダウンが一時停止中か（「彷徨うよろい」の効果など）</summary>
        ReadOnlyReactiveProperty<bool> IsCountdownPaused { get; }

        /// <summary>カードの静的定義</summary>
        CardDefinition Definition { get; }

        /// <summary>いまカード化できるか（未カード化 かつ 一度もゲインされていない）</summary>
        bool CanCardify { get; }

        /// <summary>
        /// カード化を試みる。ゲイン済みのオブジェクトは二度とカード化できない（ゲイン制限）。
        /// </summary>
        /// <returns>カード化に成功したら true</returns>
        bool TryCardify();

        /// <summary>1分ルールのカウントダウンを一時停止する（「彷徨うよろい」効果用）</summary>
        void PauseCountdown();

        /// <summary>一時停止したカウントダウンを再開する</summary>
        void ResumeCountdown();
    }
}
