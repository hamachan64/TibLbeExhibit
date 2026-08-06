using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// セッション中のアイテム出現を管理するスポーナー（Environment シーンに配置）。
    ///
    /// マルチユーザー同期のため出現は**決定論的**に行う:
    /// - GameSession.SessionSeed（ContentStart RPC で全クライアント共有）から
    ///   セッション開始時に全出現スケジュール（ID・種類・位置・時刻）を事前計算する
    /// - 実行時はセッション経過時間に従ってスケジュール通りに出現させるだけなので、
    ///   全クライアントで同じアイテムが同じ場所に同じIDで現れる（スポーン通信は不要）
    /// - 位置の間隔チェックは「計算済みのスポーン位置」に対してのみ行い、
    ///   実行時の状態（プレイヤーがカードを動かした等）には依存しない
    ///
    /// initialCount 分は開始直後、残りは trickleInterval ごとに1個ずつ
    /// maxCount（出現上限）まで追加出現する。
    ///
    /// 出現位置は、シーン内の StageSpawnAnchor（階層順で列挙）をシード選択のみで
    /// 決定論的に割り当てる（docs/MR_STAGE_PLAN.md §6.2）。アンカーが枯渇した場合、
    /// またはシーンにアンカーが1つも無い場合は従来の箱型ランダム配置にフォールバックする
    /// （＝アンカー0件のシーンでは旧挙動を完全維持する後方互換）。
    /// スケジュール構築ロジック自体は SpawnScheduleBuilder（MonoBehaviour 非依存）に
    /// 分離してあり、本クラスはシーンからの入力収集と実行時の出現タイミング制御のみを担う。
    /// </summary>
    public class ItemSpawnManager : MonoBehaviour
    {
        /// <summary>シーン上に1つだけ存在する想定のシングルトン（宝籤の出現上限チェック等から参照する）</summary>
        public static ItemSpawnManager Instance { get; private set; }

        [SerializeField, Tooltip("出現アイテムのカタログ")]
        private ItemCatalog catalog;

        [SerializeField, Tooltip("出現範囲（自身を中心とした箱のサイズ）")]
        private Vector3 spawnAreaSize = new(4f, 0f, 4f);

        [SerializeField, Tooltip("出現高さの下限（床からの相対）")]
        private float minHeight = 0.6f;

        [SerializeField, Tooltip("出現高さの上限（床からの相対）")]
        private float maxHeight = 1.5f;

        [SerializeField, Tooltip("他アイテムとの最小間隔（m）")]
        private float minSpacing = 0.5f;

        [SerializeField, Tooltip("追加出現の間隔（秒）")]
        private float trickleInterval = 12f;

        private readonly List<ScheduledSpawn> schedule = new();
        private int nextSpawnIndex;

        /// <summary>cardId ごとの出現上限（ItemCatalog.Entry.maxCount）。カタログを基に一度だけ構築する</summary>
        private readonly Dictionary<string, int> maxCountByCardId = new();

        /// <summary>cardId ごとの累計出現数（1分ルールでの消滅やゲイン後も減らない、生涯出現数）</summary>
        private readonly Dictionary<string, int> spawnedCountByCardId = new();

        private void Awake()
        {
            Instance = this;
            BuildCapacityMaps();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Start()
        {
            WaitAndRunAsync(destroyCancellationToken).Forget();
        }

        /// <summary>
        /// カタログから cardId ごとの出現上限を構築する。
        /// セッション開始（BuildSchedule）を待たずに HasRemainingCapacity が使えるよう、
        /// Awake の時点で構築しておく。
        /// </summary>
        private void BuildCapacityMaps()
        {
            maxCountByCardId.Clear();
            spawnedCountByCardId.Clear();
            if (catalog == null)
            {
                return;
            }
            foreach (var entry in catalog.Entries)
            {
                var cardId = ResolveCardId(entry.prefab);
                if (string.IsNullOrEmpty(cardId))
                {
                    continue;
                }
                maxCountByCardId[cardId] = entry.maxCount;
                spawnedCountByCardId[cardId] = 0;
            }
        }

        private static string ResolveCardId(GameObject prefab)
        {
            if (prefab == null)
            {
                return null;
            }
            var item = prefab.GetComponent<CardizableItemBase>();
            return item != null && item.Definition != null ? item.Definition.CardId : null;
        }

        /// <summary>
        /// この定義がカタログの出現上限（maxCount）にまだ達していないかどうか。
        /// カタログに存在しない定義は対象外（false）とする。
        /// </summary>
        public bool HasRemainingCapacity(CardDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.CardId))
            {
                return false;
            }
            if (!maxCountByCardId.TryGetValue(definition.CardId, out var max))
            {
                return false;
            }
            spawnedCountByCardId.TryGetValue(definition.CardId, out var current);
            return current < max;
        }

        /// <summary>
        /// この定義のアイテムが1つ出現したことをカウントに加算する。
        /// スケジュールされた通常出現（Spawn）だけでなく、宝籤等による動的な出現
        /// （CardEffectService.SpawnLotteryCardAt）からも呼ばれる。
        /// </summary>
        public void NotifySpawned(CardDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.CardId))
            {
                return;
            }
            spawnedCountByCardId.TryGetValue(definition.CardId, out var current);
            spawnedCountByCardId[definition.CardId] = current + 1;
        }

        private async UniTask WaitAndRunAsync(CancellationToken ct)
        {
            // GameSession は UserClient シーン側にあるため、マルチシーンのロードを待つ
            await UniTask.WaitUntil(() => GameSession.Instance != null, cancellationToken: ct);

            GameSession.Instance.State
                .Where(s => s == SessionState.Playing)
                .Subscribe(_ => OnSessionStarted(ct))
                .AddTo(this);
        }

        private void OnSessionStarted(CancellationToken ct)
        {
            if (catalog == null)
            {
                Debug.LogWarning("[ItemSpawnManager] ItemCatalog が未設定です");
                return;
            }

            BuildSchedule(GameSession.Instance.SessionSeed);
            Debug.Log($"[ItemSpawnManager] seed={GameSession.Instance.SessionSeed} で {schedule.Count} 個のスポーンを計画");
            RunScheduleAsync(ct).Forget();
        }

        /// <summary>
        /// シードから全出現スケジュールを決定論的に構築する（実処理は SpawnScheduleBuilder に委譲）。
        /// カタログ・アンカーの収集はここで行い、rng を用いる割り当てロジック自体には関与しない。
        /// </summary>
        private void BuildSchedule(int seed)
        {
            var entries = BuildEntrySpecs();
            var anchors = CollectAnchorSpecs();
            var builder = new SpawnScheduleBuilder(transform.position, spawnAreaSize, minHeight, maxHeight, minSpacing, trickleInterval);

            schedule.Clear();
            schedule.AddRange(builder.Build(seed, entries, anchors));
            nextSpawnIndex = 0;

            if (anchors.Count > 0)
            {
                var anchorUsedCount = 0;
                foreach (var s in schedule)
                {
                    if (s.UsedAnchor)
                    {
                        anchorUsedCount++;
                    }
                }
                Debug.Log($"[ItemSpawnManager] StageSpawnAnchor {anchorUsedCount}/{anchors.Count} 件を使用（残りは箱型フォールバック）");
            }
        }

        /// <summary>ItemCatalog のエントリを SpawnScheduleBuilder 用の最小データへ変換する（カタログ順を維持）。</summary>
        private List<SpawnEntrySpec> BuildEntrySpecs()
        {
            var entries = catalog.Entries;
            var specs = new List<SpawnEntrySpec>(entries.Count);
            for (var i = 0; i < entries.Count; i++)
            {
                specs.Add(new SpawnEntrySpec(entries[i].initialCount, entries[i].maxCount));
            }
            return specs;
        }

        /// <summary>
        /// このマネージャーが属するシーン内の StageSpawnAnchor を階層順
        /// （Transform ルート順→siblingIndex 深さ優先）で列挙し、位置情報へ変換する。
        /// 列挙順はクライアント間で一致する必要があるため、FindObjectsByType 等の
        /// 不定順APIではなく、シーンルートからの手動深さ優先探索（StageSpawnAnchorEnumerator）を用いる。
        /// </summary>
        private List<SpawnAnchorSpec> CollectAnchorSpecs()
        {
            var roots = gameObject.scene.GetRootGameObjects();
            var anchors = StageSpawnAnchorEnumerator.EnumerateInHierarchyOrder(roots);
            var cardIdToEntryIndex = BuildCardIdToEntryIndexMap();
            var specs = new List<SpawnAnchorSpec>(anchors.Count);
            var unknownCardIds = new List<string>();
            for (var i = 0; i < anchors.Count; i++)
            {
                var anchor = anchors[i];
                unknownCardIds.Clear();
                var allowed = SpawnScheduleBuilder.ResolveAllowedEntryIndices(
                    anchor.AllowedCardIds, cardIdToEntryIndex, unknownCardIds);
                foreach (var unknown in unknownCardIds)
                {
                    Debug.LogWarning($"[ItemSpawnManager] アンカー '{anchor.name}' の allowedCardIds '{unknown}' はカタログに存在しません（無視されます）", anchor);
                }
                specs.Add(new SpawnAnchorSpec(
                    anchor.transform.position,
                    anchor.IsElevated,
                    allowed,
                    anchor.transform.rotation,
                    anchor.AlignToAnchorRotation));
            }
            return specs;
        }

        /// <summary>
        /// カタログの prefab から cardId → エントリインデックスの対応表を作る
        /// （StageSpawnAnchor.AllowedCardIds の解決用）。
        /// </summary>
        private Dictionary<string, int> BuildCardIdToEntryIndexMap()
        {
            var map = new Dictionary<string, int>();
            var entries = catalog.Entries;
            for (var i = 0; i < entries.Count; i++)
            {
                var prefab = entries[i].prefab;
                if (prefab == null)
                {
                    continue;
                }
                var item = prefab.GetComponent<CardizableItemBase>();
                var cardId = item != null && item.Definition != null ? item.Definition.CardId : null;
                if (!string.IsNullOrEmpty(cardId) && !map.ContainsKey(cardId))
                {
                    map.Add(cardId, i);
                }
            }
            return map;
        }

        private async UniTask RunScheduleAsync(CancellationToken ct)
        {
            var session = GameSession.Instance;
            while (!ct.IsCancellationRequested
                   && session != null
                   && session.State.CurrentValue == SessionState.Playing
                   && nextSpawnIndex < schedule.Count)
            {
                var elapsed = session.SessionDurationSeconds - session.RemainingTime.CurrentValue;
                while (nextSpawnIndex < schedule.Count && schedule[nextSpawnIndex].Time <= elapsed)
                {
                    Spawn(schedule[nextSpawnIndex]);
                    nextSpawnIndex++;
                }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }

        private void Spawn(ScheduledSpawn spawn)
        {
            var entry = catalog.Entries[spawn.EntryIndex];
            if (entry.prefab == null)
            {
                return;
            }
            // 宝籤等の動的出現（NotifySpawned）で、このスケジュール分が生成される前に
            // 既に出現上限（maxCount）へ達している場合はスキップする。
            // spawnedCountByCardId を「通常出現・宝籤出現の両方を合算した唯一の真実のソース」として扱うことで、
            // 出現上限（カード化限度枚数）が二重にカウントされて超過するのを防ぐ
            // （例: Sランクは maxCount=1 のため、宝籤が先に引いた後にスケジュール分も出現すると2枚になってしまう）
            var definition = ResolveCardId(entry.prefab) != null ? entry.prefab.GetComponent<CardizableItemBase>().Definition : null;
            if (definition != null && !HasRemainingCapacity(definition))
            {
                return;
            }
            // 整列アンカー（木の幹・壁龕など）ではアンカーの回転、それ以外はランダム yaw が入っている
            var go = Instantiate(entry.prefab, spawn.Position, spawn.Rotation);
            var item = go.GetComponent<CardizableItemBase>();
            if (item != null && CardNetworkSync.Instance != null)
            {
                // 全クライアントで同一のIDが振られる（スケジュールが決定論的なため）
                CardNetworkSync.Instance.Register(spawn.NetworkId, item);
            }
            NotifySpawned(item != null ? item.Definition : null);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.9f, 0.5f, 0.35f);
            var center = transform.position + Vector3.up * ((minHeight + maxHeight) * 0.5f);
            var size = new Vector3(spawnAreaSize.x, maxHeight - minHeight, spawnAreaSize.z);
            Gizmos.DrawCube(center, size);
        }
    }
}
