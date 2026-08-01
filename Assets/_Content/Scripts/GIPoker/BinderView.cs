using System.Collections.Generic;
using DG.Tweening;
using R3;
using TMPro;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// G.I.バインダー（ブック）の見た目と表示を担うビュー。
    ///
    /// - 見開きの本（カバー＋左右ページ）をプロシージャルに生成する
    /// - プレイヤーの腰の高さ・正面に遅延追従する（常時携帯・展示で迷子にならない）
    /// - CardBinder.Score を購読し、ヘッダー（残り時間・スコア）と
    ///   ページ上のミニカード一覧を自動更新する
    /// - Show/Hide/Toggle で表示・非表示を切り替えられる（Lサインジェスチャーでの呼び出し用）。
    ///   見た目（VisualRoot）だけを DOTween でポップイン／アウトさせ、CardBinder のロジックは
    ///   非表示中も生き続ける（GameObject 自体は無効化しない）
    /// </summary>
    public class BinderView : MonoBehaviour
    {
        private const int ColumnsPerPage = 4;
        private const int RowsPerPage = 5;
        private const float CellWidth = 0.048f;
        private const float CellHeight = 0.062f;
        private const float MiniCardScale = 0.62f;

        // 出し入れ演出（DOTween）の所要時間
        private const float ShowDuration = 0.35f;
        private const float HideDuration = 0.25f;

        // 煙VFX（汎用アセットを想定）を実機バインダーサイズ（幅約0.45m）に合わせて控えめに縮小する倍率
        private const float SmokeVfxScale = 0.4f;

        // CardBinder.fbx の実測値（表紙/ページ中心X ≈ ±0.14m）に合わせた配置定数。
        // モデルはBlender上で「開いた本を上から見た」姿勢（X=見開き幅, Y=ページ積層厚み, Z=ページ縦）
        // で作られているため、X-90°回転で「プレイヤーに正対する縦向きの本」に変換する。
        private const float ModelPageCenterX = 0.14f;
        private const float HeaderHeightY = 0.19f;
        private const float HeaderZOffset = -0.01f;
        private const float GridZOffset = -0.02f;

        [SerializeField, Tooltip("プレイヤー正面からの距離（m）")]
        private float followDistance = 0.3f;
        [SerializeField, Tooltip("頭からどれだけ下に置くか（m）")]
        private float heightBelowHead = 0.52f;
        [SerializeField, Tooltip("追従の速さ")]
        private float followSpeed = 3.5f;

        private CardBinder binder;
        private Transform visualRoot;
        private Transform gridRoot;
        private TextMeshPro headerText;
        private Tween visualTween;
        private readonly List<GameObject> miniCards = new();
        private readonly ReactiveProperty<bool> isVisible = new(false);

        /// <summary>バインダーが表示されているか（既定は非表示。Lサインジェスチャーで呼び出す仕様のため）</summary>
        public ReadOnlyReactiveProperty<bool> IsVisible => isVisible;

        /// <summary>バインダーロジックと結線し、見た目を構築する</summary>
        public void Init(CardBinder targetBinder)
        {
            binder = targetBinder;
            BuildBook();

            binder.Score
                .Subscribe(_ => Refresh())
                .AddTo(this);

            if (GameSession.Instance != null)
            {
                GameSession.Instance.RemainingTime
                    .Subscribe(_ => UpdateHeader())
                    .AddTo(this);
            }
        }

        /// <summary>非表示⇔表示をトグルする（Lサインジェスチャーからの呼び出し想定）</summary>
        public void Toggle()
        {
            if (isVisible.Value)
            {
                Hide();
            }
            else
            {
                Show();
            }
        }

        /// <summary>
        /// バインダーを出す。VisualRoot を 0→1 スケールで OutBack ポップインさせ、煙VFXを再生する。
        /// 追従位置は前回の非表示位置から補間せず、目標位置へ即座にスナップしてから演出する
        /// （非表示中は LateUpdate の追従を止めているため、飛んでくるのを防ぐ）。
        /// </summary>
        public void Show()
        {
            if (isVisible.Value)
            {
                return;
            }
            isVisible.Value = true;
            SnapToFollowTarget();
            visualRoot.gameObject.SetActive(true);

            visualTween?.Kill();
            visualTween = visualRoot
                .DOScale(Vector3.one, ShowDuration)
                .SetEase(Ease.OutBack)
                .SetLink(gameObject);

            PlaySmokeVfx(transform.position);
        }

        /// <summary>
        /// バインダーを収める。VisualRoot を 1→0 スケールで InBack ポップアウトさせ、
        /// 完了後にモデルの GameObject を非アクティブ化する。CardBinder のロジックは生かしたままにする。
        /// </summary>
        public void Hide()
        {
            if (!isVisible.Value)
            {
                return;
            }
            isVisible.Value = false;
            PlaySmokeVfx(transform.position);

            visualTween?.Kill();
            var root = visualRoot;
            visualTween = root
                .DOScale(Vector3.zero, HideDuration)
                .SetEase(Ease.InBack)
                .SetLink(gameObject)
                .OnComplete(() => root.gameObject.SetActive(false));
        }

        /// <summary>煙VFXを再生する。未設定なら警告のみで演出をスキップする（機能自体は動く）</summary>
        private void PlaySmokeVfx(Vector3 position)
        {
            if (GIPokerAssets.BinderSmokeVfx == null)
            {
                Debug.LogWarning("[BinderView] BinderSmokeVfx が未設定のため、出し入れの煙演出をスキップします");
                return;
            }
            GIPokerAssets.SpawnVfx(GIPokerAssets.BinderSmokeVfx, position, scale: SmokeVfxScale);
        }

        private void BuildBook()
        {
            // 見た目一式（モデル・ヘッダー・カードグリッド）をまとめる VisualRoot。
            // Show/Hide のスケール演出・非表示中の非アクティブ化はこの Transform に対してのみ行い、
            // BinderView 本体（＝ CardBinder と同じ GameObject）は常に有効なままにする。
            var visualRootGo = new GameObject("VisualRoot");
            visualRootGo.transform.SetParent(transform, false);
            visualRoot = visualRootGo.transform;
            visualRoot.localScale = Vector3.zero;
            visualRootGo.SetActive(false);

            var modelPrefab = GIPokerAssets.CardBinderModelPrefab;
            if (modelPrefab != null)
            {
                var model = Instantiate(modelPrefab, visualRoot);
                model.name = "Model";
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                model.transform.localScale = Vector3.one;
            }
            else
            {
                Debug.LogWarning("[BinderView] CardBinderModelPrefab が未設定のため、プレースホルダーの箱で代用します");
                BuildPlaceholderBook();
            }

            // ヘッダーテキスト（本の上）
            var headerGo = new GameObject("Header");
            headerGo.transform.SetParent(visualRoot, false);
            headerGo.transform.localPosition = new Vector3(0f, HeaderHeightY, HeaderZOffset);
            headerGo.transform.localScale = Vector3.one * 0.01f;
            headerText = headerGo.AddComponent<TextMeshPro>();
            if (GIPokerAssets.JapaneseFont != null)
            {
                headerText.font = GIPokerAssets.JapaneseFont;
            }
            headerText.fontSize = 9f;
            headerText.alignment = TextAlignmentOptions.Center;
            headerText.rectTransform.sizeDelta = new Vector2(48f, 8f);

            // ミニカードのグリッド親（ページのわずかに手前）
            var grid = new GameObject("CardGrid");
            grid.transform.SetParent(visualRoot, false);
            grid.transform.localPosition = new Vector3(0f, 0f, GridZOffset);
            gridRoot = grid.transform;

            UpdateHeader();
        }

        /// <summary>CardBinderModelPrefab 未設定時のフォールバック（旧プリミティブ構築）</summary>
        private void BuildPlaceholderBook()
        {
            CreateBox("Cover", new Vector3(0f, 0f, 0.006f), new Vector3(0.45f, 0.32f, 0.008f),
                new Color(0.10f, 0.28f, 0.16f), new Color(0.02f, 0.08f, 0.04f));
            CreateBox("PageL", new Vector3(-0.11f, 0f, 0f), new Vector3(0.21f, 0.29f, 0.004f),
                new Color(0.92f, 0.89f, 0.80f), Color.black);
            CreateBox("PageR", new Vector3(0.11f, 0f, 0f), new Vector3(0.21f, 0.29f, 0.004f),
                new Color(0.92f, 0.89f, 0.80f), Color.black);
            CreateBox("Spine", new Vector3(0f, 0f, 0.001f), new Vector3(0.012f, 0.30f, 0.006f),
                new Color(0.95f, 0.78f, 0.25f), new Color(0.6f, 0.45f, 0.1f));
        }

        private GameObject CreateBox(string name, Vector3 localPos, Vector3 size, Color color, Color emission)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            Destroy(box.GetComponent<Collider>());
            box.transform.SetParent(visualRoot, false);
            box.transform.localPosition = localPos;
            box.transform.localScale = size;
            box.GetComponent<Renderer>().sharedMaterial = CardVisualFactory.CreateLitMaterial(color, emission);
            return box;
        }

        private void Refresh()
        {
            UpdateHeader();
            RebuildMiniCards();
        }

        private void UpdateHeader()
        {
            if (headerText == null)
            {
                return;
            }
            var session = GameSession.Instance;
            var time = session != null ? session.RemainingTime.CurrentValue : 0f;
            var minutes = Mathf.FloorToInt(time / 60f);
            var seconds = Mathf.FloorToInt(time % 60f);
            var scoreResult = binder != null ? binder.Score.CurrentValue : ScoreResult.Empty;
            var count = binder != null ? binder.Cards.Count : 0;
            headerText.text =
                $"<color=#FFD34D>残り {minutes:0}:{seconds:00}</color>   " +
                $"<color=#FFFFFF>SCORE {scoreResult.TotalScore}</color>   " +
                $"<color=#9AD1FF>{count}枚</color>";
        }

        private void RebuildMiniCards()
        {
            foreach (var mini in miniCards)
            {
                if (mini != null)
                {
                    Destroy(mini);
                }
            }
            miniCards.Clear();

            if (binder == null)
            {
                return;
            }

            var index = 0;
            var capacityShown = ColumnsPerPage * RowsPerPage * 2;
            foreach (var instance in binder.Cards)
            {
                if (index >= capacityShown)
                {
                    break;
                }
                var mini = CardVisualFactory.Create(gridRoot, instance.Definition, MiniCardScale, withSparkle: false);
                mini.transform.localPosition = GridPosition(index);
                mini.SetActive(true);
                miniCards.Add(mini);
                index++;
            }
        }

        private Vector3 GridPosition(int index)
        {
            var perPage = ColumnsPerPage * RowsPerPage;
            var page = index / perPage;           // 0=左ページ 1=右ページ
            var i = index % perPage;
            var col = i % ColumnsPerPage;
            var row = i / ColumnsPerPage;

            var pageCenterX = page == 0 ? -ModelPageCenterX : ModelPageCenterX;
            var x = pageCenterX + (col - (ColumnsPerPage - 1) * 0.5f) * CellWidth;
            var y = ((RowsPerPage - 1) * 0.5f - row) * CellHeight;
            return new Vector3(x, y, 0f);
        }

        private void LateUpdate()
        {
            // 非表示中は追従を止める（Show() 時に SnapToFollowTarget() で目標位置へスナップする）
            if (!isVisible.Value)
            {
                return;
            }
            if (!TryComputeFollowTarget(out var targetPos, out var targetRot))
            {
                return;
            }

            var t = Mathf.Clamp01(Time.deltaTime * followSpeed);
            transform.SetPositionAndRotation(
                Vector3.Lerp(transform.position, targetPos, t),
                Quaternion.Slerp(transform.rotation, targetRot, t));
        }

        /// <summary>
        /// 追従目標位置へ補間せず即座にスナップする。
        /// 非表示中は LateUpdate を止めているため、再表示時にそのまま追従を再開すると
        /// 古い位置から目標位置へ「飛んでくる」動きになってしまう。それを防ぐために使う。
        /// </summary>
        private void SnapToFollowTarget()
        {
            if (TryComputeFollowTarget(out var targetPos, out var targetRot))
            {
                transform.SetPositionAndRotation(targetPos, targetRot);
            }
        }

        private bool TryComputeFollowTarget(out Vector3 targetPos, out Quaternion targetRot)
        {
            var cam = Camera.main;
            if (cam == null)
            {
                targetPos = transform.position;
                targetRot = transform.rotation;
                return false;
            }

            var flatForward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            if (flatForward.sqrMagnitude < 0.001f)
            {
                flatForward = transform.forward;
            }

            targetPos = cam.transform.position + flatForward * followDistance + Vector3.down * heightBelowHead;
            targetRot = Quaternion.LookRotation(flatForward) * Quaternion.Euler(30f, 0f, 0f);
            return true;
        }

        private void OnDestroy()
        {
            visualTween?.Kill();
            isVisible.Dispose();
        }
    }
}
