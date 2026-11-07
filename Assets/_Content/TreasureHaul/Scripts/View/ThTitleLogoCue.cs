using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// タイトルロゴの出し消しを、同期済みの状態（core.Tracker.State）につなぐ配線（体験者の端末だけ。運営PCには置かない）。
    /// Waiting の間は空間のロゴ（ThVenue.Title）、Waiting → Opening の瞬間に 1 回だけ正面のロゴを出す。判断は <see cref="ThTitleLogoCueLogic"/>。
    /// </summary>
    public sealed class ThTitleLogoCue : MonoBehaviour
    {
        [SerializeField] SessionClient _client;
        [SerializeField, Tooltip("正面のロゴ（この端末のカメラの前に置く。UserClient シーンに置く）")] ThTitleLogo _front;

        readonly ThTitleLogoCueLogic _logic = new ThTitleLogoCueLogic();
        SessionClientCore _core;

        void Start() => RunAsync().Forget();

        async UniTaskVoid RunAsync()
        {
            if (_client == null) return;
            await UniTask.WaitUntil(() => _client.Core != null, cancellationToken: destroyCancellationToken);
            _core = _client.Core;
            _core.Tracker.State.Subscribe(OnState).AddTo(this);
        }

        void OnState(RoundState state)
        {
            var t = _core.Tracker;
            var a = _logic.OnState(state, t.RemainingSeconds.CurrentValue, ThOpeningTime.TotalSeconds(_core.Params.progress, t.Settings.phantom));   // 亡霊を入れる回は延長分を含む（ホストと同じ式）
            var venue = ThVenue.Instance;
            var title = venue != null ? venue.Title : null;
            if (title != null)
            {
                if (a.ShowVenue) title.ShowVenue();
                else if (a.HideVenue) title.HideVenue();
            }
            if (_front != null)
            {
                if (a.PlayCue) _front.PlayCue(Camera.main != null ? Camera.main.transform : null);
                else if (a.HideFront) _front.HideNow();
            }
        }
    }
}
