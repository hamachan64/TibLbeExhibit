using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using R3;
using TMPro;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// ボーナスタイム開始（残り時間が ItemSpawnManager.BonusTimeSeconds を下回った瞬間）を
    /// プレイヤーへ告知する、ワールド空間TMPのポップアップ表示。
    ///
    /// ネットワーク同期は不要。GameSession.RemainingTime は各クライアントが同じ ContentStart RPC
    /// タイミングを起点にローカルで Time.deltaTime を減算しているだけなので、StageDirector の
    /// unstableThresholdSeconds（残り時間しきい値越えで演出を切り替える）と全く同じ考え方で、
    /// 各クライアントが独立にしきい値越えを判定するだけで実用上ほぼ同時に表示できる。
    ///
    /// 本体（この MonoBehaviour が乗る GameObject）は GIPokerBootstrap から一度だけ生成され、
    /// LBE運用（1日に何組もプレイする展示）でアプリ終了まで生き続け、ラウンドごとに
    /// RemainingTime のリセット→再度のしきい値越えを検知して毎回告知する。そのため、
    /// 表示のたびに毎回 BonusTimeAnnouncementPopup という使い捨ての子オブジェクトを
    /// 生成・自己破棄する（DiceResultLabel と同じ「使い捨てポップアップ」パターン）。
    /// 本体の GameObject 自体を破棄すると、購読ごと消えて次のラウンド以降は
    /// 二度と告知されなくなるため、絶対に自分自身を破棄しないこと。
    /// </summary>
    public class BonusTimeAnnouncementView : MonoBehaviour
    {
        /// <summary>プレイヤー正面からの距離（m）</summary>
        private const float FollowDistance = 0.8f;

        /// <summary>頭からどれだけ下に置くか（m）</summary>
        private const float HeightBelowHead = 0.05f;

        /// <summary>セッションと結線する。ItemSpawnManager（Environment シーン側）の準備を待ってから購読を開始する。</summary>
        public void Init(GameSession session)
        {
            WaitAndBindAsync(session, destroyCancellationToken).Forget();
        }

        /// <summary>
        /// ItemSpawnManager は Environment シーン、本 View は UserClient シーンにあるため、
        /// ItemSpawnManager 自身が GameSession.Instance を待つのと対称的に、
        /// ItemSpawnManager.Instance の出現をマルチシーンのロード完了まで待つ。
        /// </summary>
        private async UniTask WaitAndBindAsync(GameSession session, CancellationToken ct)
        {
            await UniTask.WaitUntil(() => ItemSpawnManager.Instance != null, cancellationToken: ct);
            if (ct.IsCancellationRequested)
            {
                return;
            }

            // 出現率アップと告知のタイミングが二重管理でズレないよう、必ず同じしきい値を参照する。
            var bonusTimeSeconds = ItemSpawnManager.Instance.BonusTimeSeconds;

            // DistinctUntilChanged は「直前の購読値」を覚えているだけなので、LBE運用で同じ
            // GameObject が複数セッション（複数組のプレイヤー）にまたがって生き続けても、
            // ラウンドごとに RemainingTime が sessionDurationSeconds へリセットされるたびに
            // false→true の再遷移を検知し、毎ラウンド1回ずつ正しく告知される。
            session.RemainingTime
                .Where(_ => session.State.CurrentValue == SessionState.Playing)
                .Select(remaining => remaining <= bonusTimeSeconds)
                .DistinctUntilChanged()
                .Where(isBonusTime => isBonusTime)
                .Subscribe(_ => ShowAnnouncement())
                .AddTo(this);
        }

        private void ShowAnnouncement()
        {
            var cam = Camera.main;
            var position = cam != null
                ? cam.transform.position + cam.transform.forward * FollowDistance + Vector3.down * HeightBelowHead
                : transform.position;
            BonusTimeAnnouncementPopup.ShowAt(position);
        }

        /// <summary>
        /// 実際の見た目（ビルボードTMP・DOTweenでポップイン→表示→フェードアウトして自己破棄）を担う
        /// 使い捨てポップアップ。DiceResultLabel と同じパターン。
        /// </summary>
        private class BonusTimeAnnouncementPopup : MonoBehaviour
        {
            private const string AnnouncementMessage = "ボーナスタイム！\nレアアイテムの出現率アップ！";

            private const float PopDuration = 0.3f;
            private const float DisplayDuration = 3f;
            private const float FadeDuration = 0.8f;
            private const float RiseDistance = 0.2f;
            private const float TargetScale = 0.025f;

            private TextMeshPro text;

            public static void ShowAt(Vector3 position)
            {
                var go = new GameObject("BonusTimeAnnouncementPopup");
                go.transform.position = position;
                go.transform.localScale = Vector3.zero;

                var popup = go.AddComponent<BonusTimeAnnouncementPopup>();
                popup.text = go.AddComponent<TextMeshPro>();
                if (GIPokerAssets.JapaneseFont != null)
                {
                    popup.text.font = GIPokerAssets.JapaneseFont;
                }
                popup.text.text = AnnouncementMessage;
                popup.text.fontSize = 16f;
                popup.text.color = new Color(0.95f, 0.78f, 0.25f);
                popup.text.fontStyle = FontStyles.Bold;
                popup.text.alignment = TextAlignmentOptions.Center;
                popup.text.overflowMode = TextOverflowModes.Overflow;
                popup.text.rectTransform.sizeDelta = new Vector2(30f, 6f);

                popup.FaceCamera();
                popup.PlayAndDestroy();
            }

            private void PlayAndDestroy()
            {
                var sequence = DOTween.Sequence().SetLink(gameObject);
                sequence.Append(transform.DOScale(TargetScale, PopDuration).SetEase(Ease.OutBack));
                sequence.AppendInterval(DisplayDuration);

                var riseTargetY = transform.position.y + RiseDistance;
                sequence.Append(transform.DOMoveY(riseTargetY, FadeDuration).SetEase(Ease.OutSine));
                sequence.Join(DOTween.To(() => text.alpha, a => text.alpha = a, 0f, FadeDuration));
                sequence.OnComplete(() => Destroy(gameObject));
            }

            private void FaceCamera()
            {
                var cam = Camera.main;
                if (cam == null)
                {
                    return;
                }
                transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
            }

            private void LateUpdate()
            {
                // 常にプレイヤー（メインカメラ）の方を向ける
                FaceCamera();
            }
        }
    }
}
