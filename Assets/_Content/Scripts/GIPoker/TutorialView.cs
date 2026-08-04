using System.Collections.Generic;
using DG.Tweening;
using R3;
using TMPro;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// チュートリアルの指示パネル（プロシージャル生成・視線緩追従）。
    ///
    /// - MRパススルーの視界を塞がないよう小さめのパネルを目線のやや下に置き、
    ///   BinderView と同じ方式でプレイヤーの視線に遅延追従させる
    /// - 現在ステップの指示テキスト、進行ドット（達成済み/現在/未到達）、
    ///   本編開始までのカウントダウンを表示する
    /// - Intro ステップでは「具現化ポーカーの世界へ入る」ことが伝わる一回限りの
    ///   大見出しフラッシュ演出を挟み、以降のステップ切り替えでは達成をねぎらう
    ///   軽い称賛演出（パネルの発光フラッシュ＋称賛テキスト＋キラキラVFX）を出す
    /// - 日本語テキストには GIPokerAssets.JapaneseFont を適用する（null 許容）。
    ///   本ファイルで使用する漢字は既存UI（TitleView / BinderView / ResultView /
    ///   ResultGrade / 変更前の TutorialSequence・TutorialView）で既に描画実績のある
    ///   字のみを再利用しており、TMPアトラスの新規グリフ追加は発生しない想定
    ///   （豆腐リスクなし。「秒」「編」は変更前 TutorialView のカウントダウン文言由来）
    /// </summary>
    public class TutorialView : MonoBehaviour
    {
        [SerializeField, Tooltip("プレイヤー正面からの距離（m）")]
        private float followDistance = 0.55f;
        [SerializeField, Tooltip("頭からどれだけ下に置くか（m）")]
        private float heightBelowHead = 0.18f;
        [SerializeField, Tooltip("追従の速さ")]
        private float followSpeed = 3f;

        private const float DotSpacing = 0.036f;
        private const float DotSize = 0.014f;
        private const float PanelWidth = 0.50f;
        private const float PanelHeight = 0.205f;
        private const float TextBaseScale = 0.01f;

        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private static readonly Color GoldColor = new(0.95f, 0.78f, 0.25f);
        private static readonly Color GoldEmission = new(0.6f, 0.45f, 0.1f);
        private static readonly Color PanelBaseEmission = new(0.02f, 0.05f, 0.035f);
        private static readonly Color PanelFlashEmission = new(0.55f, 0.48f, 0.20f);
        private static readonly Color WarnColor = new(0.98f, 0.35f, 0.22f);
        private static readonly Color CalmCountdownColor = new(0.6f, 0.82f, 1f);

        private Transform panelRoot;
        private Material bgMaterial;
        private TextMeshPro titleText;
        private TextMeshPro instructionText;
        private TextMeshPro countdownText;
        private Renderer[] dotRenderers;
        private Material dotDoneMaterial;
        private Material dotCurrentMaterial;
        private Material dotPendingMaterial;

        // 各ステップの指示音声（Step1.mp3〜Step7.mp3）再生用。ノイズの多い展示環境でも
        // 聞き取りやすいよう2D再生（spatialBlend=0）にする
        private AudioSource voiceAudioSource;
        // クリップ未設定なら常に true。設定済みは Play() 呼び出し直後に false へ落とし、
        // 再生完了（!isPlaying）を検知したフレームで true に戻す
        private bool voiceFinished = true;

        private Vector3 instructionBasePos;
        private Tween instructionFadeTween;

        // 本Viewが生成したマテリアル（LBEの連続稼働でセッション毎に再生成されるため、
        // OnDestroy で確実に破棄してリークを防ぐ）
        private readonly List<Material> createdMaterials = new();

        // 直前に表示していたステップ種別（達成演出を出すべきかの判定に使う）
        private TutorialStepKind? lastStepKind;
        // カウントダウンの秒表示が変わった時だけ演出を起こすための直近値
        private int lastCountdownSecond = int.MinValue;

        /// <summary>セッションと結線し、パネルの見た目を構築する</summary>
        public void Init(GameSession session, int stepCount)
        {
            BuildPanel(stepCount);

            // 本編開始までのカウントダウン（チュートリアルは固定時間）
            session.TutorialRemainingTime
                .Subscribe(UpdateCountdown)
                .AddTo(this);
        }

        /// <summary>
        /// 現在ステップの音声読み上げが完了しているか。
        /// 音声クリップが未設定（GIPokerAssets.TutorialStepVoiceClips が null / 該当要素が null）なら
        /// 常に true（展示で詰まらせないため、タイムアウトのみで進行できる）。
        /// </summary>
        public bool IsVoiceFinished => voiceFinished;

        /// <summary>現在ステップの指示テキストと進行ドットを更新する</summary>
        public void ShowStep(int stepIndex, TutorialStep step)
        {
            UpdateInstruction(step);
            UpdateDots(stepIndex);
            PlayVoice(stepIndex);

            if (step.Kind == TutorialStepKind.Intro)
            {
                PlayIntroFlourish(step.Title);
            }
            else
            {
                if (titleText != null)
                {
                    titleText.text = step.Title;
                }

                // 実践ステップ（BinderShow/Cardify/Store/Gain）を1つ終えて次へ進んだ瞬間だけ
                // 称賛演出を出す（Introからの自動送りは対象外）
                if (lastStepKind.HasValue && lastStepKind.Value != TutorialStepKind.Intro && stepIndex > 0)
                {
                    PlayAchievementFeedback(stepIndex - 1);
                }
            }

            // ステップ切り替えのポップ演出
            if (panelRoot != null)
            {
                panelRoot.DOKill();
                panelRoot.localScale = Vector3.one * 0.92f;
                panelRoot.DOScale(1f, 0.3f).SetEase(Ease.OutBack).SetLink(panelRoot.gameObject);
            }

            lastStepKind = step.Kind;
        }

        /// <summary>
        /// ステップに対応する音声（Step1.mp3〜Step7.mp3）を再生する。
        /// 再生中のクリップがあれば即座に止めて切り替える。
        /// クリップが未設定なら「読み上げ完了」を即時 true として扱う。
        /// </summary>
        private void PlayVoice(int stepIndex)
        {
            if (voiceAudioSource == null)
            {
                voiceFinished = true;
                return;
            }

            voiceAudioSource.Stop();

            var clips = GIPokerAssets.TutorialStepVoiceClips;
            var clip = clips != null && stepIndex >= 0 && stepIndex < clips.Length ? clips[stepIndex] : null;
            voiceAudioSource.clip = clip;

            if (clip == null)
            {
                voiceFinished = true;
                return;
            }

            // Play() 直後の1フレーム目で誤って true にならないよう、ここで明示的に false にする
            voiceFinished = false;
            voiceAudioSource.Play();
        }

        private void BuildPanel(int stepCount)
        {
            voiceAudioSource = gameObject.AddComponent<AudioSource>();
            voiceAudioSource.playOnAwake = false;
            voiceAudioSource.spatialBlend = 0f; // 2D再生（ノイズの多い展示環境でも聞き取りやすくする）

            var root = new GameObject("Panel");
            root.transform.SetParent(transform, false);
            panelRoot = root.transform;

            // 外周の発光フレーム（本体よりわずかに大きい板を背後に重ねてベゼル風の質感を出す）
            var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frame.name = "Frame";
            Destroy(frame.GetComponent<Collider>());
            frame.transform.SetParent(panelRoot, false);
            frame.transform.localPosition = new Vector3(0f, 0f, 0.008f);
            frame.transform.localScale = new Vector3(PanelWidth + 0.01f, PanelHeight + 0.01f, 0.004f);
            frame.GetComponent<Renderer>().sharedMaterial =
                CreateTrackedMaterial(GoldColor * 0.55f, GoldEmission);

            // 背景（濃緑〜黒のガラス質パネル。視界を塞がない小さめサイズ）
            var bg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bg.name = "BG";
            Destroy(bg.GetComponent<Collider>());
            bg.transform.SetParent(panelRoot, false);
            bg.transform.localPosition = new Vector3(0f, 0f, 0.005f);
            bg.transform.localScale = new Vector3(PanelWidth, PanelHeight, 0.008f);
            bgMaterial = CreateTrackedMaterial(new Color(0.05f, 0.08f, 0.065f), PanelBaseEmission);
            bg.GetComponent<Renderer>().sharedMaterial = bgMaterial;

            // タイトル下の金の区切りライン（BinderViewの背表紙ラインと同じ意匠で統一感を出す）
            var divider = GameObject.CreatePrimitive(PrimitiveType.Cube);
            divider.name = "Divider";
            Destroy(divider.GetComponent<Collider>());
            divider.transform.SetParent(panelRoot, false);
            divider.transform.localPosition = new Vector3(0f, 0.046f, 0.0005f);
            divider.transform.localScale = new Vector3(PanelWidth - 0.05f, 0.0015f, 0.004f);
            divider.GetComponent<Renderer>().sharedMaterial = CreateTrackedMaterial(GoldColor, GoldEmission);

            // タイトル（初期値は空。ShowStep で各ステップの Title に差し替わる）
            titleText = CreateText("Title", string.Empty, new Vector3(0f, 0.075f, -0.002f), 5.5f,
                GoldColor, FontStyles.Bold);

            // 指示テキスト（中央・少し大きめ＆行間広めで一読しやすく）
            instructionText = CreateText("Instruction", string.Empty, new Vector3(0f, 0.006f, -0.002f), 7f,
                Color.white, FontStyles.Bold);
            instructionText.lineSpacing = 8f;
            instructionBasePos = instructionText.transform.localPosition;

            // 進行ドット
            BuildDots(stepCount);

            // 本編開始カウントダウン（下部・小。最後の5秒は UpdateCountdown 内で強調表示に切り替わる）
            countdownText = CreateText("Countdown", string.Empty, new Vector3(0f, -0.088f, -0.002f), 4f,
                CalmCountdownColor, FontStyles.Normal);
        }

        private void BuildDots(int stepCount)
        {
            dotDoneMaterial = CreateTrackedMaterial(GoldColor, GoldEmission);
            dotCurrentMaterial = CreateTrackedMaterial(Color.white, new Color(0.6f, 0.6f, 0.6f));
            dotPendingMaterial = CreateTrackedMaterial(new Color(0.3f, 0.32f, 0.3f), Color.black);

            const float dotsY = -0.058f;

            // ドットの下地となる細いトラックライン（進行の「道」を可視化する）
            var track = GameObject.CreatePrimitive(PrimitiveType.Cube);
            track.name = "DotTrack";
            Destroy(track.GetComponent<Collider>());
            track.transform.SetParent(panelRoot, false);
            var trackWidth = (stepCount - 1) * DotSpacing + DotSize * 1.5f;
            track.transform.localPosition = new Vector3(0f, dotsY, 0.0005f);
            track.transform.localScale = new Vector3(trackWidth, 0.0015f, 0.003f);
            track.GetComponent<Renderer>().sharedMaterial =
                CreateTrackedMaterial(new Color(0.12f, 0.12f, 0.1f), Color.black);

            dotRenderers = new Renderer[stepCount];
            var startX = -(stepCount - 1) * 0.5f * DotSpacing;
            for (var i = 0; i < stepCount; i++)
            {
                var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                dot.name = $"Dot{i}";
                Destroy(dot.GetComponent<Collider>());
                dot.transform.SetParent(panelRoot, false);
                dot.transform.localPosition = new Vector3(startX + i * DotSpacing, dotsY, -0.002f);
                dot.transform.localScale = Vector3.one * DotSize;
                dotRenderers[i] = dot.GetComponent<Renderer>();
                dotRenderers[i].sharedMaterial = dotPendingMaterial;
            }
        }

        /// <summary>達成済み/現在/未到達のドット表示を更新する（現在地のドットは控えめに脈動させる）</summary>
        private void UpdateDots(int stepIndex)
        {
            if (dotRenderers == null)
            {
                return;
            }

            for (var i = 0; i < dotRenderers.Length; i++)
            {
                var dotRenderer = dotRenderers[i];
                if (dotRenderer == null)
                {
                    continue;
                }

                var dotTransform = dotRenderer.transform;
                dotTransform.DOKill();
                dotTransform.localScale = Vector3.one * DotSize;

                if (i < stepIndex)
                {
                    dotRenderer.sharedMaterial = dotDoneMaterial;
                }
                else if (i == stepIndex)
                {
                    dotRenderer.sharedMaterial = dotCurrentMaterial;
                    dotTransform.DOScale(DotSize * 1.35f, 0.55f)
                        .SetEase(Ease.InOutSine)
                        .SetLoops(-1, LoopType.Yoyo)
                        .SetLink(dotTransform.gameObject);
                }
                else
                {
                    dotRenderer.sharedMaterial = dotPendingMaterial;
                }
            }
        }

        /// <summary>指示テキストを差し替え、軽いフェード＋浮き上がりで切り替わりに気づきやすくする</summary>
        private void UpdateInstruction(TutorialStep step)
        {
            if (instructionText == null)
            {
                return;
            }
            instructionText.text = step.Instruction;

            var t = instructionText.transform;
            t.DOKill();
            instructionFadeTween?.Kill();

            t.localPosition = instructionBasePos + Vector3.down * 0.008f;
            instructionText.alpha = 0.2f;

            t.DOLocalMove(instructionBasePos, 0.22f).SetEase(Ease.OutCubic).SetLink(t.gameObject);
            instructionFadeTween = DOTween.To(() => instructionText.alpha, a => instructionText.alpha = a, 1f, 0.22f)
                .SetLink(t.gameObject);
        }

        /// <summary>
        /// Introステップに入った瞬間だけ再生する、世界観提示の大見出しフラッシュ。
        /// パネル上部に一時的に大きく「具現化ポーカー」を掲げ、しばらくしてから
        /// 通常のタイトル表示（ステップ1の Title＝「ルール」）へ収束する。
        /// </summary>
        /// <param name="firstStepTitle">フラッシュ終了後に表示するステップ1のタイトル</param>
        private void PlayIntroFlourish(string firstStepTitle)
        {
            if (panelRoot == null)
            {
                return;
            }

            if (titleText != null)
            {
                titleText.text = string.Empty; // 大見出しの表示中はタイトル欄と重複させない
            }

            var flourishRoot = new GameObject("IntroFlourish");
            flourishRoot.transform.SetParent(panelRoot, false);
            flourishRoot.transform.localPosition = new Vector3(0f, 0.135f, -0.015f);

            var mainTitle = CreateText(flourishRoot.transform, "IntroTitle", "具現化ポーカー",
                Vector3.zero, 10f, GoldColor, FontStyles.Bold);
            var subTitle = CreateText(flourishRoot.transform, "IntroSub", "- G.I. POKER -",
                new Vector3(0f, -0.026f, 0f), 4.2f, new Color(0.85f, 0.72f, 0.48f), FontStyles.Normal);

            var mainBaseScale = mainTitle.transform.localScale;
            var subBaseScale = subTitle.transform.localScale;
            mainTitle.alpha = 0f;
            subTitle.alpha = 0f;
            mainTitle.transform.localScale = Vector3.zero;
            subTitle.transform.localScale = Vector3.zero;

            // 世界が具現化するようなキラキラを一度だけ添える
            GIPokerAssets.SpawnVfx(GIPokerAssets.CardSparkleVfx, panelRoot.position, 2.5f);

            var seq = DOTween.Sequence().SetLink(flourishRoot);
            seq.Append(mainTitle.transform.DOScale(mainBaseScale, 0.45f).SetEase(Ease.OutBack));
            seq.Join(DOTween.To(() => mainTitle.alpha, a => mainTitle.alpha = a, 1f, 0.35f));
            seq.Append(subTitle.transform.DOScale(subBaseScale, 0.25f).SetEase(Ease.OutBack));
            seq.Join(DOTween.To(() => subTitle.alpha, a => subTitle.alpha = a, 1f, 0.2f));
            seq.AppendInterval(1.3f);
            seq.Append(DOTween.To(() => mainTitle.alpha, a => mainTitle.alpha = a, 0f, 0.4f));
            seq.Join(DOTween.To(() => subTitle.alpha, a => subTitle.alpha = a, 0f, 0.4f));
            seq.Join(flourishRoot.transform.DOScale(0f, 0.4f).SetEase(Ease.InBack));
            seq.OnComplete(() =>
            {
                Destroy(flourishRoot);
                if (titleText != null)
                {
                    titleText.text = firstStepTitle;
                }
            });
        }

        /// <summary>
        /// 実践ステップを1つ終えて次へ進んだ瞬間の達成フィードバック。
        /// 完了したドットをポップさせ、パネルを金色にフラッシュし、
        /// 称賛テキストとキラキラVFXで「できた」を分かりやすく伝える。
        /// </summary>
        private void PlayAchievementFeedback(int completedDotIndex)
        {
            if (dotRenderers != null && completedDotIndex >= 0 && completedDotIndex < dotRenderers.Length)
            {
                var dot = dotRenderers[completedDotIndex];
                if (dot != null)
                {
                    dot.transform.DOPunchScale(Vector3.one * (DotSize * 0.9f), 0.4f, 6, 0.8f)
                        .SetLink(dot.gameObject);
                }
            }

            PlayPanelFlash(PanelFlashEmission, 0.5f);
            PlayPraisePopup();

            if (panelRoot != null)
            {
                GIPokerAssets.SpawnVfx(GIPokerAssets.CardSparkleVfx, panelRoot.position, 1.5f);
            }
        }

        /// <summary>パネル背景の発光色を一瞬明るくしてから元に戻す（達成・カウントダウン強調の共通演出）</summary>
        private void PlayPanelFlash(Color flashEmission, float duration)
        {
            if (bgMaterial == null)
            {
                return;
            }
            bgMaterial.DOKill();
            DOTween.Sequence()
                .Append(bgMaterial.DOColor(flashEmission, EmissionId, duration * 0.3f).SetEase(Ease.OutQuad))
                .Append(bgMaterial.DOColor(PanelBaseEmission, EmissionId, duration * 0.7f).SetEase(Ease.InQuad))
                .SetLink(gameObject);
        }

        /// <summary>パネル上部にポップして消える短い称賛テキストを出す</summary>
        private void PlayPraisePopup()
        {
            if (panelRoot == null)
            {
                return;
            }

            var praise = CreateText(panelRoot, "Praise", "よくできた！",
                new Vector3(0f, 0.128f, -0.01f), 5.5f, GoldColor, FontStyles.Bold);
            var baseScale = praise.transform.localScale;
            praise.alpha = 0f;
            praise.transform.localScale = Vector3.zero;

            DOTween.Sequence()
                .Append(praise.transform.DOScale(baseScale, 0.2f).SetEase(Ease.OutBack))
                .Join(DOTween.To(() => praise.alpha, a => praise.alpha = a, 1f, 0.15f))
                .AppendInterval(0.55f)
                .Append(DOTween.To(() => praise.alpha, a => praise.alpha = a, 0f, 0.35f))
                .Join(praise.transform.DOScale(baseScale * 0.7f, 0.35f).SetEase(Ease.InCubic))
                .OnComplete(() => Destroy(praise.gameObject))
                .SetLink(praise.gameObject);
        }

        /// <summary>
        /// マテリアルを生成しつつ破棄リストへ登録する。
        /// CardVisualFactory.CreateLitMaterial は呼び出しごとに new Material するため、
        /// 本View経由で作ったものはすべてここを通して OnDestroy での一括破棄対象にする。
        /// </summary>
        private Material CreateTrackedMaterial(Color color, Color emission)
        {
            var mat = CardVisualFactory.CreateLitMaterial(color, emission);
            createdMaterials.Add(mat);
            return mat;
        }

        private TextMeshPro CreateText(string name, string content,
            Vector3 localPos, float fontSize, Color color, FontStyles style)
        {
            return CreateText(panelRoot, name, content, localPos, fontSize, color, style);
        }

        private TextMeshPro CreateText(Transform parent, string name, string content,
            Vector3 localPos, float fontSize, Color color, FontStyles style)
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
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = new Vector2(42f, 8f);
            return tmp;
        }

        /// <summary>
        /// 本編開始までのカウントダウン表示。ラスト5秒は色・サイズ・ポップ・
        /// パネルフラッシュで「もうすぐ始まる」ことを強く伝える。
        /// 秒表示が変わったフレームだけ演出を起こす（毎フレーム更新されるため）。
        /// </summary>
        private void UpdateCountdown(float remaining)
        {
            if (countdownText == null)
            {
                return;
            }

            var sec = Mathf.CeilToInt(Mathf.Max(0f, remaining));
            if (sec == lastCountdownSecond)
            {
                return;
            }
            lastCountdownSecond = sec;

            if (sec <= 0)
            {
                countdownText.text = "本編スタート！";
                countdownText.color = WarnColor;
                countdownText.fontSize = 6.5f;
                return;
            }

            if (sec <= 5)
            {
                countdownText.text = $"まもなく本編開始… {sec}";
                countdownText.color = Color.Lerp(GoldColor, WarnColor, (6 - sec) / 5f);
                countdownText.fontSize = 6.5f;

                countdownText.transform.DOKill();
                countdownText.transform.localScale = Vector3.one * TextBaseScale * 0.8f;
                countdownText.transform.DOScale(TextBaseScale, 0.28f)
                    .SetEase(Ease.OutBack)
                    .SetLink(countdownText.gameObject);

                PlayPanelFlash(Color.Lerp(PanelBaseEmission, WarnColor, 0.5f), 0.35f);
            }
            else
            {
                countdownText.text = $"本編開始まで {sec}秒";
                countdownText.color = CalmCountdownColor;
                countdownText.fontSize = 4f;
            }
        }

        /// <summary>
        /// 生成した全マテリアルを破棄する。
        /// LBEの連続稼働ではセッションごとに TutorialView が生成・破棄されるため、
        /// ここで確実に解放しないとセッションまたぎで漸増リークする。
        /// </summary>
        private void OnDestroy()
        {
            voiceAudioSource?.Stop();

            foreach (var mat in createdMaterials)
            {
                if (mat != null)
                {
                    mat.DOKill(); // PlayPanelFlash 等のマテリアルTweenが残っていても先に止める
                    Destroy(mat);
                }
            }
            createdMaterials.Clear();
        }

        /// <summary>
        /// 音声再生完了を毎フレーム監視し、再生が終わったフレームで IsVoiceFinished を true に戻す。
        /// Play() 直後は voiceFinished=false から始まるため、誤って即座に true になることはない。
        /// </summary>
        private void Update()
        {
            if (!voiceFinished && voiceAudioSource != null && !voiceAudioSource.isPlaying)
            {
                voiceFinished = true;
            }
        }

        private void LateUpdate()
        {
            // BinderView と同じ視線緩追従（急な頭の動きでも酔いにくい）
            var cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            var flatForward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            if (flatForward.sqrMagnitude < 0.001f)
            {
                flatForward = transform.forward;
            }

            var targetPos = cam.transform.position + flatForward * followDistance + Vector3.down * heightBelowHead;
            var targetRot = Quaternion.LookRotation(flatForward) * Quaternion.Euler(5f, 0f, 0f);

            var t = Mathf.Clamp01(Time.deltaTime * followSpeed);
            transform.SetPositionAndRotation(
                Vector3.Lerp(transform.position, targetPos, t),
                Quaternion.Slerp(transform.rotation, targetRot, t));
        }
    }
}
