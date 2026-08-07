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
    ///   非表示中も生き続ける（GameObject 自体は無効化しない）。出し入れの瞬間には
    ///   白煙＋衝撃波リングのVFX（VFX_BinderSummonPoof）を再生する
    /// </summary>
    public class BinderView : MonoBehaviour
    {
        // 出し入れ演出（DOTween）の所要時間
        private const float ShowDuration = 0.35f;
        private const float HideDuration = 0.25f;

        // 煙＋衝撃波VFX（VFX_BinderSummonPoof）の再生時間（秒）。プレハブ自体が実機バインダーサイズ
        // （幅約0.45m）に合わせて作り込み済みのため、ここでの追加スケーリングは行わない。
        // Smoke(startLifetime 1s) / ShockWave(startLifetime 0.5s) が再生し切るまでの余裕を持たせた値。
        private const float SmokeVfxLifetime = 1.5f;

        private const float HeaderHeightY = 0.19f;
        private const float HeaderZOffset = -0.01f;

        // ページめくりの補完アニメーション（DOTween）の所要時間
        private const float PageTurnCommitDuration = 0.3f;
        private const float PageTurnSpringBackDuration = 0.2f;

        // 収納OKハイライトの発光色（シアン系。パススルーMR環境下でも視認しやすいよう強めに設定）
        private static readonly Color HighlightEmissionColor = new Color(0.25f, 0.85f, 1f) * 3f;
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField, Tooltip("プレイヤー正面からの距離（m）")]
        private float followDistance = 0.25f;
        [SerializeField, Tooltip("頭からどれだけ下に置くか（m）")]
        private float heightBelowHead = 0.52f;
        [SerializeField, Tooltip("追従の速さ")]
        private float followSpeed = 3.5f;

        private CardBinder binder;
        private Transform visualRoot;
        private TextMeshPro headerText;
        private Tween visualTween;
        private Tween pageTurnTween;
        private readonly List<GameObject> miniCards = new();
        private readonly List<CardInstance> miniCardInstances = new();
        private readonly List<Renderer> placeholderRenderers = new();
        private readonly ReactiveProperty<bool> isVisible = new(false);
        private readonly ReactiveProperty<bool> isHighlighted = new(false);
        private readonly HashSet<object> highlightRequesters = new();

        /// <summary>Page_01〜Page_08 の Transform（モデル未設定・見つからない場合は要素が null になりうる）</summary>
        private Transform[] pages;

        /// <summary>現在最前面に開いているページの配列インデックス（0始まり）</summary>
        private int currentPageIndex;

        /// <summary>現在ドラッグでめくっているページの配列インデックス（-1 ならめくり操作なし）</summary>
        private int turningPageIndex = -1;

        /// <summary>true: currentPageIndex のページを 0°→180° 方向にめくっている（次のページへ進む）</summary>
        private bool turningForward;

        /// <summary>ドラッグ中の現在の回転角（度、0〜180にクランプ）</summary>
        private float turningAngleDeg;

        private Renderer[] modelRenderers;

        /// <summary>バインダーが表示されているか（既定は非表示。Lサインジェスチャーで呼び出す仕様のため）</summary>
        public ReadOnlyReactiveProperty<bool> IsVisible => isVisible;

        /// <summary>収納OKハイライト表示中かどうか（発光/アウトライン表示のON/OFF）</summary>
        public ReadOnlyReactiveProperty<bool> IsHighlighted => isHighlighted;

        /// <summary>ページめくりのドラッグ操作中かどうか</summary>
        public bool IsTurningPage => turningPageIndex >= 0;

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
            CancelPageTurnOnHide();
            PlaySmokeVfx(transform.position);

            visualTween?.Kill();
            var root = visualRoot;
            visualTween = root
                .DOScale(Vector3.zero, HideDuration)
                .SetEase(Ease.InBack)
                .SetLink(gameObject)
                .OnComplete(() => root.gameObject.SetActive(false));
        }

        /// <summary>煙＋衝撃波VFXを再生する。未設定なら警告のみで演出をスキップする（機能自体は動く）</summary>
        private void PlaySmokeVfx(Vector3 position)
        {
            if (GIPokerAssets.BinderSmokeVfx == null)
            {
                Debug.LogWarning("[BinderView] BinderSmokeVfx が未設定のため、出し入れの煙演出をスキップします");
                return;
            }
            GIPokerAssets.SpawnVfx(GIPokerAssets.BinderSmokeVfx, position, lifetime: SmokeVfxLifetime);
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
                LoadPages(model.transform);
                modelRenderers = model.GetComponentsInChildren<Renderer>(true);
            }
            else
            {
                Debug.LogWarning("[BinderView] CardBinderModelPrefab が未設定のため、プレースホルダーの箱で代用します");
                pages = null;
                BuildPlaceholderBook();
                modelRenderers = placeholderRenderers.ToArray();
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
            headerText.fontSize = 18f;
            headerText.alignment = TextAlignmentOptions.Center;
            headerText.rectTransform.sizeDelta = new Vector2(48f, 8f);

            UpdateHeader();
        }

        /// <summary>
        /// モデル階層から Page_01〜Page_08 の Transform を取得する。
        /// CardBinderModelPrefab は「Model直下にCover_Front/Cover_Back/Spine/Binder_Rings/
        /// Page_01〜Page_08が並ぶ」標準的なFBXインポート階層を前提とする。
        /// 見つからないページがあっても例外にはせず、そのページのグリッド・ページめくりが
        /// 機能しないだけに留める（プレースホルダー代用時などを想定）。
        /// </summary>
        private void LoadPages(Transform modelRoot)
        {
            pages = new Transform[BinderLayout.PageCount];
            var missingCount = 0;
            for (var i = 0; i < BinderLayout.PageCount; i++)
            {
                var pageName = $"Page_{i + 1:00}";
                pages[i] = modelRoot.Find(pageName);
                if (pages[i] == null)
                {
                    missingCount++;
                }
            }
            if (missingCount > 0)
            {
                Debug.LogWarning(
                    $"[BinderView] モデル内に Page_01〜Page_08 のうち {missingCount} 枚が見つかりません" +
                    "（該当ページのカードグリッド・ページめくりが機能しません）");
            }
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
            var renderer = box.GetComponent<Renderer>();
            renderer.sharedMaterial = CardVisualFactory.CreateLitMaterial(color, emission);
            placeholderRenderers.Add(renderer);
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

        /// <summary>
        /// 現在ページ（currentPageIndex）に属するカードのみを、実測値ベースの3×3グリッドで
        /// そのページの Transform の子として再構築する。ページごとに親を変えることで、
        /// ページめくりのヒンジ回転にミニカードが追従する。
        /// カードの挿入順インデックス（binder.Cards内の並び順）から index/9=ページ、
        /// index%9=スロットを都度算出するため、取り出しで配列が詰まった結果、
        /// 後続カードのページ表示が変わることを許容する（仕様どおり）。
        /// </summary>
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
            miniCardInstances.Clear();

            if (binder == null || pages == null || currentPageIndex < 0 || currentPageIndex >= pages.Length)
            {
                return;
            }

            var pageTransform = pages[currentPageIndex];
            if (pageTransform == null)
            {
                return;
            }

            var index = 0;
            foreach (var instance in binder.Cards)
            {
                var pageOfCard = BinderLayout.PageIndexOf(index);
                if (pageOfCard == currentPageIndex)
                {
                    var slot = BinderLayout.SlotIndexOf(index);
                    var mini = CardVisualFactory.Create(pageTransform, instance.Definition, BinderLayout.MiniCardScale, withSparkle: false);
                    mini.transform.localPosition = BinderLayout.SlotLocalPosition(slot);
                    mini.SetActive(true);
                    miniCards.Add(mini);
                    miniCardInstances.Add(instance);
                }
                else if (pageOfCard > currentPageIndex)
                {
                    // binder.Cards は挿入順であり、pageOfCard は index の増加に対して単調非減少のため、
                    // 現在ページを追い越した時点で以降のカードもすべて後続ページ確定であり探索を打ち切れる
                    break;
                }
                index++;
            }
        }

        /// <summary>
        /// 現在ページに表示中のミニカードのうち、指定位置に最も近いものを検出する
        /// （取り出しのための距離判定。ミニカード自体にColliderは付けない）。
        /// バインダーが非表示のときは常に false を返す。
        /// </summary>
        public bool TryFindNearestMiniCard(Vector3 worldPosition, out CardInstance instance)
        {
            instance = null;
            if (!isVisible.Value || miniCards.Count == 0)
            {
                return false;
            }

            var nearestIndex = -1;
            var nearestSqrDistance = float.MaxValue;
            for (var i = 0; i < miniCards.Count; i++)
            {
                if (miniCards[i] == null)
                {
                    continue;
                }
                var sqrDistance = (miniCards[i].transform.position - worldPosition).sqrMagnitude;
                if (sqrDistance < nearestSqrDistance)
                {
                    nearestSqrDistance = sqrDistance;
                    nearestIndex = i;
                }
            }

            var grabRadius = BinderLayout.MiniCardGrabRadius;
            if (nearestIndex < 0 || nearestSqrDistance > grabRadius * grabRadius)
            {
                return false;
            }

            instance = miniCardInstances[nearestIndex];
            return true;
        }

        /// <summary>
        /// 指定位置に最も近いミニカードの取り出しを試みる（CardBinder.TryTakeOut への橋渡し）。
        /// StoreCopy（Source == null）カードは CardBinder.TryTakeOut 側で拒否される。
        /// </summary>
        public bool TryTakeOutNearestMiniCard(Vector3 worldPosition, out CardizableItemBase card)
        {
            card = null;
            if (binder == null || !TryFindNearestMiniCard(worldPosition, out var instance))
            {
                return false;
            }
            if (!binder.TryTakeOut(instance, worldPosition))
            {
                return false;
            }
            card = instance.Source;
            return true;
        }

        /// <summary>
        /// ピンチ開始位置が、現在操作可能な2枚（最前面ページ／直前にめくって左側にある1枚）の
        /// 角タブ近傍かどうかを判定し、該当すればページめくりのドラッグを開始する。
        /// </summary>
        public bool TryBeginPageTurn(Vector3 pinchWorldPosition)
        {
            if (!isVisible.Value || pages == null || IsTurningPage)
            {
                return false;
            }

            // 最前面ページ（0°→180°方向にめくって次のページへ進む）
            if (TryMatchTab(currentPageIndex, pinchWorldPosition))
            {
                BeginTurn(currentPageIndex, forward: true);
                return true;
            }

            // 直前にめくって左側にある1枚（180°→0°方向に戻して前のページへ戻る）
            var previousIndex = currentPageIndex - 1;
            if (previousIndex >= 0 && TryMatchTab(previousIndex, pinchWorldPosition))
            {
                BeginTurn(previousIndex, forward: false);
                return true;
            }

            return false;
        }

        private bool TryMatchTab(int pageIndex, Vector3 pinchWorldPosition)
        {
            if (pages == null || pageIndex < 0 || pageIndex >= pages.Length || pages[pageIndex] == null)
            {
                return false;
            }
            var local = pages[pageIndex].InverseTransformPoint(pinchWorldPosition);
            return Vector3.Distance(local, BinderLayout.PageTabLocalPosition) <= BinderLayout.PageTabRadius;
        }

        private void BeginTurn(int pageIndex, bool forward)
        {
            pageTurnTween?.Kill();
            turningPageIndex = pageIndex;
            turningForward = forward;
            turningAngleDeg = forward ? 0f : 180f;
        }

        /// <summary>
        /// Hide() 時にページめくりドラッグが進行中だった場合、非表示のバインダーに対して
        /// 後からアニメーション・currentPageIndex更新が走らないよう、即座に元の静止角へ
        /// スナップして中断する（もう一方の手のLサインで Toggle() された場合など、
        /// ページ端を掴んでいる最中に非表示化されるケースへの対策）。
        /// ドラッグ中だった手の CardGrabInteractor は turningPageBinderView 参照を握ったままだが、
        /// 以後の UpdatePageTurn/EndPageTurn は IsTurningPage==false により安全に no-op になる。
        /// </summary>
        private void CancelPageTurnOnHide()
        {
            if (!IsTurningPage)
            {
                return;
            }
            pageTurnTween?.Kill();
            var page = pages[turningPageIndex];
            if (page != null)
            {
                page.localRotation = Quaternion.AngleAxis(turningForward ? 0f : 180f, Vector3.forward);
            }
            turningPageIndex = -1;
        }

        /// <summary>
        /// ページめくりドラッグ中、ピンチ位置の動きにヒンジ回転（背表紙軸＝モデルローカルZ軸）を
        /// 追従させる。回転角は、ページの親（Modelの回転していないローカル座標系）における
        /// ピンチ位置の「背表紙を中心とした角度」（atan2）をそのまま採用する
        /// （ページ静止時の角タブが角度0の位置にあるため、そのまま角度として使える）。
        /// </summary>
        public void UpdatePageTurn(Vector3 pinchWorldPosition)
        {
            if (!IsTurningPage || pages[turningPageIndex] == null)
            {
                return;
            }
            var page = pages[turningPageIndex];
            var modelLocal = page.parent != null ? page.parent.InverseTransformPoint(pinchWorldPosition) : pinchWorldPosition;
            turningAngleDeg = Mathf.Clamp(Mathf.Atan2(modelLocal.y, modelLocal.x) * Mathf.Rad2Deg, 0f, 180f);
            page.localRotation = Quaternion.AngleAxis(turningAngleDeg, Vector3.forward);
        }

        /// <summary>
        /// ページめくりドラッグを終了する。指を離した時点の回転角がしきい値を超えていれば
        /// 180°まで自動補完してめくり切り currentPageIndex を更新し、しきい値未満なら
        /// バネで元の状態に戻す（いずれも DOTween によるアニメーション）。
        /// </summary>
        public void EndPageTurn(Vector3 pinchWorldPosition)
        {
            if (!IsTurningPage)
            {
                return;
            }
            UpdatePageTurn(pinchWorldPosition);

            var pageIndex = turningPageIndex;
            var forward = turningForward;
            var page = pages[pageIndex];

            // 最終角度が180側に寄っていれば「開いた（左へ倒れた）」状態に収束させる。
            // ページが実際に切り替わるかどうかの非対称な判定は BinderLayout.ShouldAdvancePage
            // に切り出してあり（EditModeテストで直接検証済み）、ここではそれを呼ぶだけにする。
            var settleOpen = BinderLayout.ShouldCommitPageTurn(turningAngleDeg);
            var indexChanges = BinderLayout.ShouldAdvancePage(forward, turningAngleDeg);
            turningPageIndex = -1;

            var targetAngle = settleOpen ? 180f : 0f;
            if (page != null)
            {
                var startAngle = turningAngleDeg;
                pageTurnTween?.Kill();
                pageTurnTween = DOTween.To(
                        () => startAngle,
                        a =>
                        {
                            startAngle = a;
                            page.localRotation = Quaternion.AngleAxis(a, Vector3.forward);
                        },
                        targetAngle,
                        indexChanges ? PageTurnCommitDuration : PageTurnSpringBackDuration)
                    .SetEase(Ease.OutCubic)
                    .SetLink(gameObject);
            }

            if (indexChanges)
            {
                currentPageIndex = forward ? pageIndex + 1 : pageIndex;
                RebuildMiniCards();
            }
        }

        /// <summary>
        /// 収納OKハイライトのON/OFFを、呼び出し元（通常は各手のCardGrabInteractor）ごとに
        /// 参照カウントして切り替える。両手がそれぞれ別のカードを持って同じバインダーの
        /// 収納圏内に入っている場合など、片方の手が圏外に出てももう片方が圏内である限り
        /// ハイライトは消灯しない。
        /// </summary>
        public void SetHighlighted(object requester, bool highlighted)
        {
            var changed = highlighted ? highlightRequesters.Add(requester) : highlightRequesters.Remove(requester);
            if (!changed)
            {
                return;
            }
            var newValue = highlightRequesters.Count > 0;
            if (isHighlighted.Value == newValue)
            {
                return;
            }
            isHighlighted.Value = newValue;
            ApplyHighlight();
        }

        /// <summary>単一の呼び出し元しか想定しない簡易版（テスト・単一呼び出し元向け）</summary>
        public void SetHighlighted(bool highlighted) => SetHighlighted(this, highlighted);

        /// <summary>
        /// renderer.material（インスタンス化）でエミッションカラーを切り替える。
        /// sharedMaterial は直接書き換えない（マルチプレイヤーで他プレイヤーのバインダーに
        /// 影響しないため）。
        /// </summary>
        private void ApplyHighlight()
        {
            if (modelRenderers == null)
            {
                return;
            }
            var color = isHighlighted.Value ? HighlightEmissionColor : Color.black;
            foreach (var renderer in modelRenderers)
            {
                if (renderer == null)
                {
                    continue;
                }
                var mat = renderer.material;
                if (isHighlighted.Value)
                {
                    mat.EnableKeyword("_EMISSION");
                }
                else
                {
                    mat.DisableKeyword("_EMISSION");
                }
                mat.SetColor(EmissionColorId, color);
            }
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
            targetRot = Quaternion.LookRotation(flatForward) * Quaternion.Euler(30f, 0f, 180f);
            return true;
        }

        private void OnDestroy()
        {
            visualTween?.Kill();
            pageTurnTween?.Kill();
            isVisible.Dispose();
            isHighlighted.Dispose();
        }
    }
}
