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

        private void Start()
        {
            WaitAndRunAsync(destroyCancellationToken).Forget();
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
            var specs = new List<SpawnAnchorSpec>(anchors.Count);
            for (var i = 0; i < anchors.Count; i++)
            {
                var anchor = anchors[i];
                specs.Add(new SpawnAnchorSpec(anchor.transform.position, anchor.IsElevated));
            }
            return specs;
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
            var go = Instantiate(entry.prefab, spawn.Position, Quaternion.Euler(0f, spawn.YawDegrees, 0f));
            var item = go.GetComponent<CardizableItemBase>();
            if (item != null && CardNetworkSync.Instance != null)
            {
                // 全クライアントで同一のIDが振られる（スケジュールが決定論的なため）
                CardNetworkSync.Instance.Register(spawn.NetworkId, item);
            }
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
