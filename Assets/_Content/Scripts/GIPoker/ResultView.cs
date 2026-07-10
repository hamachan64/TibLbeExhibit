using System.Collections.Generic;
using DG.Tweening;
using R3;
using TMPro;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// タイムアップ時のリザルト表示。
    /// GameSession が Finished になったらプレイヤーの正面にパネルを生成し、
    /// 「TIME UP! → カードリキャップ → 役の開票 → スコアカウントアップ →
    /// グレードスタンプ → 案内フッター」の順で DOTween Sequence によるステージング演出を行う。
    ///
    /// - パネルは頭の移動に遅れてついてくる緩い視線追従（回転は水平のみ）
    /// - Finished 以外の状態に遷移したらパネルと演出を破棄し、
    ///   次の Finished で再表示できるようにリセットする（展示ループ対応）
    /// </summary>
    public class ResultView : MonoBehaviour
    {
        // --- 追従パラメータ（BinderView より緩め） ---
        private const float FollowDistance = 1.2f;
        private const float HeightBelowHead = 0.05f;
        private const float FollowSpeed = 2.0f;

        // --- レイアウト定数（パネルはローカル 0.85 x 0.65。MRの視界を覆わない現状程度の大きさ） ---
        private const float TextBaseScale = 0.01f;
        private const float MiniCardScale = 0.5f;
        private const int MaxComboLines = 5;

        private static readonly Color GoldColor = new(0.95f, 0.78f, 0.25f);

        private GameSession session;
        private CardBinder binder;

        private GameObject panelRoot;
        private Sequence sequence;
        private bool shown;

        public void Init(GameSession gameSession, CardBinder cardBinder)
        {
            session = gameSession;
            binder = cardBinder;

            // Finished で表示、それ以外へ遷移（次セッション開始など）でリセットする
            session.State
                .Subscribe(s =>
                {
                    if (s == SessionState.Finished)
                    {
                        Show();
                    }
                    else
                    {
                        Hide();
                    }
                })
                .AddTo(this);
        }

        private void Show()
        {
            if (shown)
            {
                return;
            }
            shown = true;

            // スコアは Finished 遷移時点のバインダー内容で確定させる
            var result = binder != null ? binder.Score.CurrentValue : ScoreResult.Empty;
            var grade = ResultGrade.Judge(result.TotalScore);

            var cam = Camera.main;
            var forward = cam != null
                ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized
                : Vector3.forward;
            var origin = cam != null ? cam.transform.position : Vector3.up * 1.4f;

            panelRoot = new GameObject("GIPoker_Result");
            panelRoot.transform.SetPositionAndRotation(
                origin + forward * FollowDistance + Vector3.down * HeightBelowHead,
                Quaternion.LookRotation(forward));

            BuildAndPlay(result, grade);
        }

        /// <summary>パネルと実行中の演出を破棄し、次の Finished で再表示できる状態に戻す</summary>
        private void Hide()
        {
            shown = false;
            sequence?.Kill();
            sequence = null;
            if (panelRoot != null)
            {
                // SetLink(panelRoot) 済みの残存 Tween も Destroy 時にまとめて破棄される
                Destroy(panelRoot);
                panelRoot = null;
            }
        }

        private void BuildAndPlay(ScoreResult result, ResultGradeRank grade)
        {
            var root = panelRoot.transform;

            // 背景パネル（現状程度の大きさ。MRパススルーの視界を覆わない）
            var bg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bg.name = "BG";
            Destroy(bg.GetComponent<Collider>());
            bg.transform.SetParent(root, false);
            bg.transform.localPosition = new Vector3(0f, 0f, 0.01f);
            bg.transform.localScale = new Vector3(0.85f, 0.65f, 0.01f);
            bg.GetComponent<Renderer>().sharedMaterial =
                CardVisualFactory.CreateLitMaterial(new Color(0.06f, 0.09f, 0.07f), new Color(0.01f, 0.02f, 0.01f));

            sequence = DOTween.Sequence().SetLink(panelRoot);

            AppendTitleStage();
            AppendCardRecapStage();
            AppendComboStage(result);
            AppendScoreCountUpStage(result);
            AppendGradeStampStage(grade);
            AppendFooterStage();
        }

        // --- ステージ1: 「TIME UP!」フラッシュイン ---
        private void AppendTitleStage()
        {
            var title = CreateText(panelRoot.transform, "Title", "TIME UP!",
                new Vector3(0f, 0.25f, 0f), 26f, GoldColor, FontStyles.Bold);
            title.transform.localScale = Vector3.zero;

            panelRoot.transform.localScale = Vector3.zero;

            var intro = DOTween.Sequence();
            intro.Append(panelRoot.transform.DOScale(1f, 0.4f).SetEase(Ease.OutBack));
            intro.Insert(0.15f, title.transform.DOScale(TextBaseScale, 0.3f).SetEase(Ease.OutBack));
            intro.Append(title.transform.DOPunchScale(
                Vector3.one * (TextBaseScale * 0.35f), 0.3f, 8, 0.9f));
            sequence.Append(intro);
        }

        // --- ステージ2: カードリキャップ（バインダー内カードを弧状に順次フライイン） ---
        private void AppendCardRecapStage()
        {
            if (binder == null || binder.Cards.Count == 0)
            {
                return;
            }

            var arcRoot = new GameObject("CardArc");
            arcRoot.transform.SetParent(panelRoot.transform, false);
            arcRoot.transform.localPosition = new Vector3(0f, 0.165f, -0.01f);

            var count = binder.Cards.Count;
            // 枚数が多いときは間隔を詰めて全体尺（8〜10秒）を守る
            var flyInterval = count > 15 ? 0.05f : 0.08f;
            var spacing = Mathf.Min(0.05f, 0.62f / Mathf.Max(count - 1, 1));
            // 端のカードが中央よりわずかに下がる、ゆるい弧（扇）を作る
            var halfSpan = (count - 1) * 0.5f;
            var edgeDrop = count > 1 ? 0.025f / (halfSpan * halfSpan) : 0f;

            var recap = DOTween.Sequence();
            var index = 0;
            foreach (var instance in binder.Cards)
            {
                var u = index - halfSpan;
                var target = new Vector3(u * spacing, -u * u * edgeDrop, -0.0008f * index);
                var tilt = Mathf.Clamp(-u * 3.5f, -28f, 28f);

                var mini = CardVisualFactory.Create(arcRoot.transform, instance.Definition,
                    MiniCardScale, withSparkle: false);
                mini.transform.localPosition = new Vector3(0f, -0.06f, target.z);
                mini.transform.localRotation = Quaternion.Euler(0f, 0f, tilt);
                mini.transform.localScale = Vector3.zero;
                mini.SetActive(true);

                var t0 = index * flyInterval;
                recap.Insert(t0, mini.transform.DOScale(MiniCardScale, 0.25f).SetEase(Ease.OutBack));
                recap.Insert(t0, mini.transform.DOLocalMove(target, 0.3f).SetEase(Ease.OutCubic));
                index++;
            }

            sequence.AppendInterval(0.2f);
            sequence.Append(recap);
        }

        // --- ステージ3: 役の開票（1件ずつ表示、ボーナス点がポップ） ---
        private void AppendComboStage(ScoreResult result)
        {
            sequence.AppendInterval(0.25f);

            const float firstLineY = 0.075f;
            const float lineStep = 0.038f;

            if (result.Combos.Count == 0)
            {
                var noneText = CreateText(panelRoot.transform, "ComboNone", "役なし",
                    new Vector3(0f, firstLineY, 0f), 10f, new Color(0.55f, 0.55f, 0.55f), FontStyles.Normal);
                noneText.alpha = 0f;
                sequence.Append(FadeText(noneText, 1f, 0.3f));
                return;
            }

            // 行数が多いときは末尾をまとめて表示する（パネルからあふれないように）
            var lines = BuildComboLines(result.Combos);
            for (var i = 0; i < lines.Count; i++)
            {
                var y = firstLineY - i * lineStep;
                var (comboName, bonus) = lines[i];

                var nameText = CreateText(panelRoot.transform, $"ComboName{i}", comboName,
                    new Vector3(-0.05f, y, 0f), 10f, Color.white, FontStyles.Normal,
                    TextAlignmentOptions.Left, new Vector2(60f, 6f));
                nameText.alpha = 0f;

                var bonusText = CreateText(panelRoot.transform, $"ComboBonus{i}", $"+{bonus}",
                    new Vector3(-0.10f, y, 0f), 10f, GoldColor, FontStyles.Bold,
                    TextAlignmentOptions.Right, new Vector2(44f, 6f));
                bonusText.alpha = 0f;
                bonusText.transform.localScale = Vector3.zero;

                sequence.Append(FadeText(nameText, 1f, 0.18f));

                var pop = DOTween.Sequence();
                pop.Append(bonusText.transform.DOScale(TextBaseScale, 0.22f).SetEase(Ease.OutBack));
                pop.Join(FadeText(bonusText, 1f, 0.15f));
                sequence.Append(pop);
                sequence.AppendInterval(0.1f);
            }
        }

        /// <summary>役一覧を表示行に変換する。MaxComboLines を超えた分は1行に集約する</summary>
        private static List<(string name, int bonus)> BuildComboLines(IReadOnlyList<AchievedCombo> combos)
        {
            var lines = new List<(string, int)>();
            if (combos.Count <= MaxComboLines)
            {
                foreach (var combo in combos)
                {
                    lines.Add((combo.Name, combo.Bonus));
                }
                return lines;
            }

            for (var i = 0; i < MaxComboLines - 1; i++)
            {
                lines.Add((combos[i].Name, combos[i].Bonus));
            }
            var restCount = 0;
            var restBonus = 0;
            for (var i = MaxComboLines - 1; i < combos.Count; i++)
            {
                restCount++;
                restBonus += combos[i].Bonus;
            }
            lines.Add(($"ほか {restCount} 件", restBonus));
            return lines;
        }

        // --- ステージ4: スコアカウントアップ（基本得点 → ボーナス → TOTAL） ---
        private void AppendScoreCountUpStage(ScoreResult result)
        {
            var baseText = CreateText(panelRoot.transform, "BaseScore", "基本得点  0",
                new Vector3(-0.19f, -0.16f, 0f), 10f, Color.white, FontStyles.Normal);
            baseText.alpha = 0f;

            var bonusText = CreateText(panelRoot.transform, "BonusScore", "ボーナス  0",
                new Vector3(0.19f, -0.16f, 0f), 10f, Color.white, FontStyles.Normal);
            bonusText.alpha = 0f;

            var totalText = CreateText(panelRoot.transform, "Total", "TOTAL  0",
                new Vector3(0f, -0.235f, 0f), 30f, GoldColor, FontStyles.Bold);
            totalText.alpha = 0f;

            sequence.AppendInterval(0.2f);

            sequence.Append(FadeText(baseText, 1f, 0.15f));
            sequence.Append(DOVirtual.Float(0f, result.BaseScore, 0.6f,
                    v => baseText.text = $"基本得点  {Mathf.FloorToInt(v)}")
                .SetEase(Ease.OutCubic));

            sequence.Append(FadeText(bonusText, 1f, 0.15f));
            sequence.Append(DOVirtual.Float(0f, result.BonusScore, 0.5f,
                    v => bonusText.text = $"ボーナス  {Mathf.FloorToInt(v)}")
                .SetEase(Ease.OutCubic));

            sequence.Append(FadeText(totalText, 1f, 0.15f));
            sequence.Append(DOVirtual.Float(0f, result.TotalScore, 1.1f,
                    v => totalText.text = $"TOTAL  {Mathf.FloorToInt(v)}")
                .SetEase(Ease.OutCubic));
        }

        // --- ステージ5: グレードスタンプ（大きくドンと押してから軽く揺れる） ---
        private void AppendGradeStampStage(ResultGradeRank grade)
        {
            var gradeColor = ResultGrade.GetColor(grade);

            // スタンプらしく少し傾けて右側に重ねる
            var stampRoot = new GameObject("GradeStamp");
            stampRoot.transform.SetParent(panelRoot.transform, false);
            stampRoot.transform.localPosition = new Vector3(0.28f, -0.03f, -0.02f);
            stampRoot.transform.localRotation = Quaternion.Euler(0f, 0f, -10f);
            stampRoot.transform.localScale = Vector3.one * 3f;

            var letterText = CreateText(stampRoot.transform, "GradeLetter", grade.ToString(),
                new Vector3(0f, 0.01f, 0f), 130f, gradeColor, FontStyles.Bold,
                TextAlignmentOptions.Center, new Vector2(30f, 20f));
            letterText.alpha = 0f;

            var labelText = CreateText(stampRoot.transform, "GradeLabel", ResultGrade.GetLabel(grade),
                new Vector3(0f, -0.085f, 0f), 10f, gradeColor, FontStyles.Bold,
                TextAlignmentOptions.Center, new Vector2(30f, 6f));
            labelText.alpha = 0f;

            sequence.AppendInterval(0.15f);

            var stamp = DOTween.Sequence();
            stamp.Append(stampRoot.transform.DOScale(1f, 0.28f).SetEase(Ease.InCubic));
            stamp.Join(FadeText(letterText, 1f, 0.28f));
            stamp.Join(FadeText(labelText, 1f, 0.28f));
            stamp.AppendCallback(() =>
                GIPokerAssets.SpawnVfx(GIPokerAssets.DiceBlessVfx, stampRoot.transform.position, 4f));
            stamp.Append(stampRoot.transform.DOShakeRotation(0.4f, new Vector3(0f, 0f, 6f), 12, 90f));
            sequence.Append(stamp);
        }

        // --- ステージ6: 案内フッター（体験終了のアナウンスを点滅表示） ---
        private void AppendFooterStage()
        {
            var footerText = CreateText(panelRoot.transform, "Footer",
                "体験は以上です。ヘッドセットを外して スタッフにお声がけください",
                new Vector3(0f, -0.295f, 0f), 8f, new Color(0.85f, 0.88f, 0.85f), FontStyles.Normal);
            footerText.alpha = 0f;

            sequence.AppendInterval(0.2f);
            sequence.Append(FadeText(footerText, 1f, 0.4f));
            sequence.AppendCallback(() =>
            {
                // ゆっくり点滅させ続ける（パネル破棄時に SetLink で自動停止）
                DOTween.To(() => footerText.alpha, a => footerText.alpha = a, 0.35f, 0.8f)
                    .SetEase(Ease.InOutSine)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetLink(panelRoot);
            });
        }

        /// <summary>テキストのアルファをフェードする（TMPモジュール非依存の汎用 Tween）</summary>
        private static Tween FadeText(TMP_Text text, float alpha, float duration)
        {
            return DOTween.To(() => text.alpha, a => text.alpha = a, alpha, duration);
        }

        private TextMeshPro CreateText(Transform parent, string name, string content,
            Vector3 localPos, float fontSize, Color color, FontStyles style,
            TextAlignmentOptions alignment = TextAlignmentOptions.Center,
            Vector2? sizeDelta = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * TextBaseScale;

            var tmp = go.AddComponent<TextMeshPro>();
            if (GIPokerAssets.JapaneseFont != null)
            {
                tmp.font = GIPokerAssets.JapaneseFont;
            }
            tmp.text = content;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.fontStyle = style;
            tmp.alignment = alignment;
            tmp.rectTransform.sizeDelta = sizeDelta ?? new Vector2(80f, 40f);
            return tmp;
        }

        /// <summary>
        /// 緩い視線追従。頭の移動に遅れてついてくる（回転は水平のみ）。
        /// BinderView の追従と同じ Lerp/Slerp 方式で、速度はより緩やか。
        /// </summary>
        private void LateUpdate()
        {
            if (panelRoot == null)
            {
                return;
            }
            var cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            var flatForward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            if (flatForward.sqrMagnitude < 0.001f)
            {
                flatForward = panelRoot.transform.forward;
            }

            var targetPos = cam.transform.position + flatForward * FollowDistance + Vector3.down * HeightBelowHead;
            var targetRot = Quaternion.LookRotation(flatForward);

            var t = Mathf.Clamp01(Time.deltaTime * FollowSpeed);
            panelRoot.transform.SetPositionAndRotation(
                Vector3.Lerp(panelRoot.transform.position, targetPos, t),
                Quaternion.Slerp(panelRoot.transform.rotation, targetRot, t));
        }
    }
}
