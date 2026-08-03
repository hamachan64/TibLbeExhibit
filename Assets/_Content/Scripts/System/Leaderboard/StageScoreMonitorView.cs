using System.Collections.Generic;
using System.Text;
using R3;
using TMPro;
using UnityEngine;

namespace STYLY.TIB_Template.Leaderboard
{
    /// <summary>
    /// Environment.unity 内の固定Stage位置（既存 UIDocument プレースホルダーの跡地）に配置する
    /// MR空間向けライブスコアボード。「残り時間」と「今セッションのトップ3」を
    /// プロシージャルTextMeshPro(3D)（BinderView.BuildBook() と同様の生成パターン）で表示する。
    ///
    /// - 追従・ビルボードは行わない（Stage上の固定モニターのため、Camera.main 依存の LateUpdate は不要）。
    /// - 「本日通算のトップ3」はここでは表示しない（永続化データの端末間不整合の懸念のため、
    ///   引き続き OperatorClient 専用の ScoreMonitorView のみが扱う）。
    /// - GIPokerAssets.JapaneseFont には依存せず、自前でフォントアセットを持つ
    ///   （GIPokerBootstrap は UserClient.unity 専用で OperatorClient 側では未初期化のため、
    ///   Leaderboard 名前空間の「OperatorClient専用ロジックを持たない」設計方針に合わせて自己完結させる）。
    /// - LeaderboardTracker は UserClient.unity に配置される。Environment.unity は
    ///   UserClient/OperatorClient どちらからも追加ロードされるため、本Viewは常に同一プロセス内の
    ///   LeaderboardTracker.Instance を見つけて動作する。
    /// </summary>
    public class StageScoreMonitorView : MonoBehaviour
    {
        private const float FontSize = 8f;

        [SerializeField, Tooltip("日本語対応TMPフォント（Noto Sans JP SDF）")]
        private TMP_FontAsset japaneseFont;

        private TextMeshPro remainingTimeText;
        private TextMeshPro liveTop3Text;

        private void Awake()
        {
            BuildViews();
        }

        private void Start()
        {
            var tracker = LeaderboardTracker.Instance;
            if (tracker == null)
            {
                Debug.LogWarning("[StageScoreMonitorView] LeaderboardTracker が見つかりません");
                return;
            }

            tracker.RemainingTime
                .Subscribe(UpdateRemainingTime)
                .AddTo(this);

            tracker.LiveTop3
                .Subscribe(UpdateLiveTop3)
                .AddTo(this);
        }

        private void BuildViews()
        {
            var remainingGo = new GameObject("RemainingTimeText");
            remainingGo.transform.SetParent(transform, false);
            remainingGo.transform.localPosition = new Vector3(0f, 0.15f, 0f);
            remainingGo.transform.localScale = Vector3.one * 0.01f;
            remainingTimeText = remainingGo.AddComponent<TextMeshPro>();
            ConfigureText(remainingTimeText, new Vector2(60f, 10f));

            var top3Go = new GameObject("LiveTop3Text");
            top3Go.transform.SetParent(transform, false);
            top3Go.transform.localPosition = new Vector3(0f, -0.05f, 0f);
            top3Go.transform.localScale = Vector3.one * 0.01f;
            liveTop3Text = top3Go.AddComponent<TextMeshPro>();
            ConfigureText(liveTop3Text, new Vector2(60f, 30f));
        }

        private void ConfigureText(TextMeshPro text, Vector2 sizeDelta)
        {
            if (japaneseFont != null)
            {
                text.font = japaneseFont;
            }
            text.fontSize = FontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.rectTransform.sizeDelta = sizeDelta;
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

        private void UpdateLiveTop3(IReadOnlyList<LeaderboardEntry> entries)
        {
            if (liveTop3Text == null)
            {
                return;
            }
            if (entries == null || entries.Count == 0)
            {
                liveTop3Text.text = "現在のセッションTOP3\n-";
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("現在のセッションTOP3");
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                sb.AppendLine($"{i + 1}. {PlayerNickname.Format(entry.ClientNo)}  {entry.Score}pt");
            }
            liveTop3Text.text = sb.ToString();
        }
    }
}
