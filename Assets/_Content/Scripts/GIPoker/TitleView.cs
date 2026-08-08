using System.Collections.Generic;
using DG.Tweening;
using R3;
using TMPro;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// 開始待ち（SessionState.WaitingForStart）中に表示するタイトル画面。
    ///
    /// - プレイヤー正面・目線の高さに緩やかに追従する（BinderView.LateUpdate 参照）
    /// - WaitingForStart の間だけロゴ・装飾カード・案内文を表示する
    /// - 他状態へ遷移したら退場演出（DOScale→0）後に表示物を破棄し、
    ///   WaitingForStart に戻ったら再構築する（次の体験者向けのループ運用）
    /// - MRパススルーの視界を塞がないよう、大きな背景板は置かずフローティング表示のみで構成する
    /// </summary>
    public class TitleView : MonoBehaviour
    {
        [SerializeField, Tooltip("プレイヤー正面からの距離（m）")]
        private float followDistance = 1.6f;
        [SerializeField, Tooltip("追従の速さ（ゆっくりめ）")]
        private float followSpeed = 1.2f;

        // 弧状に並べる装飾カードのランク（D〜Sの5枚）
        private static readonly CardRank[] DecorRanks =
        {
            CardRank.D, CardRank.C, CardRank.B, CardRank.A, CardRank.S,
        };

        // ランクごとの装飾カード用マテリアルのキャッシュ（Show/Hideを何度も繰り返す
        // 展示ループ運用でマテリアルが際限なく増えないようにする）
        private static readonly Dictionary<CardRank, Material> decorMaterialCache = new();

        private GameObject content;
        private bool hasSnapped;

        /// <summary>ゲームセッションと結線する</summary>
        public void Init(GameSession session)
        {
            session.State
                .Subscribe(OnStateChanged)
                .AddTo(this);
        }

        private void OnStateChanged(SessionState state)
        {
            // 将来 SessionState に値が増えても壊れないよう、
            // 「開始待ちかどうか」だけで表示・非表示を判定する
            if (state == SessionState.WaitingForStart)
            {
                Show();
            }
            else
            {
                Hide();
            }
        }

        private void Show()
        {
            if (content != null)
            {
                return;
            }

            // 追従の始点が原点のままだと初回表示時に大きくワープして見えるため、
            // 生成前に一度だけ現在のカメラ位置へスナップしておく
            SnapToTarget();

            content = new GameObject("TitleContent");
            content.transform.SetParent(transform, false);

            BuildLogo(content.transform);
            BuildDecorCards(content.transform);
            BuildCaption(content.transform);

            content.transform.localScale = Vector3.zero;
            content.transform.DOScale(1f, 0.6f)
                .SetEase(Ease.OutBack)
                .SetLink(content);
        }

        private void Hide()
        {
            if (content == null)
            {
                return;
            }

            var toDestroy = content;
            content = null;

            // 入場Tweenが残っていても退場演出が確実に勝つように先に破棄する
            toDestroy.transform.DOKill();
            toDestroy.transform
                .DOScale(0f, 0.4f)
                .SetEase(Ease.InBack)
                .SetLink(toDestroy)
                .OnComplete(() => Destroy(toDestroy));
        }

        private void BuildLogo(Transform parent)
        {
            // 金色（#F2C740系）の大きなメインロゴ
            CreateText(parent, "MainLogo", "MR グリード・アイランド",
                new Vector3(0f, 0.36f, 0f), 64f, new Color(0.95f, 0.78f, 0.25f), FontStyles.Bold);

            CreateText(parent, "SubLogo", "- MR G.I. -",
                new Vector3(0f, 0.22f, 0f), 24f, new Color(0.85f, 0.72f, 0.48f), FontStyles.Normal);
        }

        private void BuildDecorCards(Transform parent)
        {
            const float arcHalfAngle = 55f;
            const float arcRadius = 0.55f;
            const float baseY = -0.02f;

            var count = DecorRanks.Length;
            for (var i = 0; i < count; i++)
            {
                var t = count > 1 ? i / (float)(count - 1) : 0.5f; // 0..1
                var angleDeg = Mathf.Lerp(-arcHalfAngle, arcHalfAngle, t);
                var angleRad = angleDeg * Mathf.Deg2Rad;

                var x = Mathf.Sin(angleRad) * arcRadius;
                var y = baseY - (1f - Mathf.Cos(angleRad)) * arcRadius * 0.3f;

                var card = CreateDecorCard(parent, DecorRanks[i]);
                card.transform.localPosition = new Vector3(x, y, 0f);
                card.transform.localRotation = Quaternion.Euler(0f, 0f, -angleDeg * 0.35f);

                PlayFloatAnimation(card.transform, i);
            }
        }

        /// <summary>
        /// ランク別の装飾ミニカードを生成する。
        /// CardVisualFactory.Create は CardDefinition（ScriptableObject）を要求するが、
        /// そのフィールドはすべて private SerializeField でランタイムからは設定できないため、
        /// ここでは CardVisualFactory.CreateLitMaterial を使ったカード型 Cube＋
        /// ランク色文字（CardVisualFactory.GetRankColor）で代替する。
        /// </summary>
        private GameObject CreateDecorCard(Transform parent, CardRank rank)
        {
            var card = new GameObject($"DecorCard_{rank}");
            card.transform.SetParent(parent, false);

            // カード本体（アイボリー基調＋ランク色の淡い発光）
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(card.transform, false);
            body.transform.localScale = new Vector3(0.05f, 0.07f, 0.004f);
            body.GetComponent<Renderer>().sharedMaterial = GetDecorMaterial(rank);

            // ランク色の文字（Bodyの非一様スケールに巻き込まれないよう card 直下に置く）
            // 緩回転で裏面も見えるため、CardVisualFactory と同様に表裏の両面へ配置する
            // （TMPは -Z 側から見て正しく読める）
            var rankColor = CardVisualFactory.GetRankColor(rank);
            CreateText(card.transform, "RankF", rank.ToString(),
                new Vector3(0f, 0f, -0.0028f), 32f, rankColor, FontStyles.Bold);
            var back = CreateText(card.transform, "RankB", rank.ToString(),
                new Vector3(0f, 0f, 0.0028f), 32f, rankColor, FontStyles.Bold);
            back.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            return card;
        }

        private static Material GetDecorMaterial(CardRank rank)
        {
            if (!decorMaterialCache.TryGetValue(rank, out var mat) || mat == null)
            {
                var rankColor = CardVisualFactory.GetRankColor(rank);
                var baseColor = new Color(0.93f, 0.91f, 0.86f); // BinderViewのページ色に合わせたアイボリー
                mat = CardVisualFactory.CreateLitMaterial(baseColor, rankColor * 0.7f);
                decorMaterialCache[rank] = mat;
            }
            return mat;
        }

        /// <summary>浮遊＋緩回転のループ演出（CardizableItemBase.PlayFloatingAnimation のローカル座標版）</summary>
        private void PlayFloatAnimation(Transform t, int index)
        {
            const float floatAmplitude = 0.03f;
            var floatDuration = 1.6f + index * 0.15f;   // カードごとに周期をずらして揃わないようにする
            var rotateDuration = 6f + index * 0.8f;

            t.DOLocalMoveY(t.localPosition.y + floatAmplitude, floatDuration)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetLink(t.gameObject);

            t.DOLocalRotate(new Vector3(0f, 360f, 0f), rotateDuration, RotateMode.FastBeyond360)
                .SetEase(Ease.Linear)
                .SetLoops(-1, LoopType.Incremental)
                .SetLink(t.gameObject);
        }

        private void BuildCaption(Transform parent)
        {
            var caption = CreateText(parent, "Caption",
                "スタッフがゲームを開始するまで\nそのままお待ちください",
                new Vector3(0f, -0.34f, 0f), 20f, Color.white, FontStyles.Normal);

            // TextMeshPro（3D版）には DOFade 拡張がこのプロジェクトのDOTweenモジュールに
            // 含まれていない（UGUIのGraphic/Image向けのみ）ため、DOVirtual.Float で
            // アルファ値を手動アニメーションする（ResultViewのカウントアップ演出と同じ手法）
            DOVirtual.Float(1f, 0.35f, 1.2f, v =>
                {
                    if (caption == null)
                    {
                        return;
                    }
                    var c = caption.color;
                    c.a = v;
                    caption.color = c;
                })
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetLink(caption.gameObject);
        }

        private TextMeshPro CreateText(Transform parent, string name, string text,
            Vector3 localPos, float fontSize, Color color, FontStyles style)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * 0.01f;

            var tmp = go.AddComponent<TextMeshPro>();
            if (GIPokerAssets.JapaneseFont != null)
            {
                tmp.font = GIPokerAssets.JapaneseFont;
            }
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = new Vector2(80f, 40f);
            return tmp;
        }

        private void LateUpdate()
        {
            if (content == null)
            {
                return;
            }
            // STYLY XR Rig はランタイム生成のため、Show 時点ではカメラが未生成のことがある。
            // カメラを初めて検出したフレームは補間せず即座に正面へスナップする
            ApplyFollow(hasSnapped ? Mathf.Clamp01(Time.deltaTime * followSpeed) : 1f);
        }

        private void SnapToTarget()
        {
            ApplyFollow(1f);
        }

        /// <summary>プレイヤー正面・目線の高さへゆるやかに追従する（BinderView.LateUpdate 参照）</summary>
        private void ApplyFollow(float t)
        {
            var cam = Camera.main;
            if (cam == null)
            {
                return;
            }
            hasSnapped = true;

            var flatForward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            if (flatForward.sqrMagnitude < 0.001f)
            {
                flatForward = transform.forward;
            }

            var targetPos = cam.transform.position + flatForward * followDistance;
            targetPos.y = cam.transform.position.y; // 目線の高さ
            var targetRot = Quaternion.LookRotation(flatForward);

            transform.SetPositionAndRotation(
                Vector3.Lerp(transform.position, targetPos, t),
                Quaternion.Slerp(transform.rotation, targetRot, t));
        }
    }
}
