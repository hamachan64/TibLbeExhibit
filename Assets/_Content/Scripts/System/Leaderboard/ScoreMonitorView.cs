using System.Collections.Generic;
using System.Text;
using R3;
using TMPro;
using UnityEngine;

namespace STYLY.TIB_Template.Leaderboard
{
    /// <summary>
    /// OperatorClient.unity の Canvas 配下に配置する uGUI バインダー。
    /// LeaderboardTracker の ReactiveProperty を購読して3つの TextMeshProUGUI を更新するだけの
    /// 実用重視な薄いView（DOTween等の演出は行わない）。
    /// </summary>
    public class ScoreMonitorView : MonoBehaviour
    {
        [SerializeField, Tooltip("残り時間の表示先")]
        private TextMeshProUGUI remainingTimeText;

        [SerializeField, Tooltip("今セッションのトップ3（ニックネーム＋スコア）の表示先")]
        private TextMeshProUGUI liveTop3Text;

        [SerializeField, Tooltip("本日通算のトップ3（ニックネーム＋スコア）の表示先")]
        private TextMeshProUGUI dailyTop3Text;

        private void Start()
        {
            var tracker = LeaderboardTracker.Instance;
            if (tracker == null)
            {
                Debug.LogWarning("[ScoreMonitorView] LeaderboardTracker が見つかりません");
                return;
            }

            tracker.RemainingTime
                .Subscribe(UpdateRemainingTime)
                .AddTo(this);

            tracker.LiveTop3
                .Subscribe(entries => UpdateTop3(liveTop3Text, entries))
                .AddTo(this);

            tracker.DailyTop3
                .Subscribe(entries => UpdateTop3(dailyTop3Text, entries))
                .AddTo(this);
        }

        private void UpdateRemainingTime(float seconds)
        {
            if (remainingTimeText == null)
            {
                return;
            }
            var clamped = Mathf.Max(0f, seconds);
            var minutes = Mathf.FloorToInt(clamped / 60f);
            var secs = Mathf.FloorToInt(clamped % 60f);
            remainingTimeText.text = $"残り時間: {minutes:00}:{secs:00}";
        }

        private static void UpdateTop3(TextMeshProUGUI text, IReadOnlyList<LeaderboardEntry> entries)
        {
            if (text == null)
            {
                return;
            }
            if (entries == null || entries.Count == 0)
            {
                text.text = "-";
                return;
            }

            var sb = new StringBuilder();
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                sb.AppendLine($"{i + 1}. {PlayerNickname.Format(entry.ClientNo)}  {entry.Score}pt");
            }
            text.text = sb.ToString();
        }
    }
}
