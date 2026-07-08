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
    /// </summary>
    public class ItemSpawnManager : MonoBehaviour
    {
        private readonly struct ScheduledSpawn
        {
            public readonly int NetworkId;
            public readonly int EntryIndex;
            public readonly Vector3 Position;
            public readonly float YawDegrees;
            public readonly float Time;

            public ScheduledSpawn(int networkId, int entryIndex, Vector3 position, float yawDegrees, float time)
            {
                NetworkId = networkId;
                EntryIndex = entryIndex;
                Position = position;
                YawDegrees = yawDegrees;
                Time = time;
            }
        }

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
        /// シードから全出現スケジュールを決定論的に構築する。
        /// UnityEngine.Random は他所の消費で狂うため System.Random を使う。
        /// </summary>
        private void BuildSchedule(int seed)
        {
            schedule.Clear();
            nextSpawnIndex = 0;
            var rng = new System.Random(seed);
            var placedPositions = new List<Vector3>();
            var networkId = 0;

            // 1. 初期出現分（時刻0）
            for (var i = 0; i < catalog.Entries.Count; i++)
            {
                var entry = catalog.Entries[i];
                var count = Mathf.Min(entry.initialCount, entry.maxCount);
                for (var n = 0; n < count; n++)
                {
                    AddSpawn(ref networkId, i, 0f, rng, placedPositions);
                }
            }

            // 2. 追加出現分: 残数をエントリ順に列挙し、シャッフルして間隔ごとに配置
            var remaining = new List<int>();
            for (var i = 0; i < catalog.Entries.Count; i++)
            {
                var entry = catalog.Entries[i];
                var rest = entry.maxCount - Mathf.Min(entry.initialCount, entry.maxCount);
                for (var n = 0; n < rest; n++)
                {
                    remaining.Add(i);
                }
            }
            // Fisher-Yates シャッフル（rng の消費順もクライアント間で一致する）
            for (var i = remaining.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (remaining[i], remaining[j]) = (remaining[j], remaining[i]);
            }
            for (var k = 0; k < remaining.Count; k++)
            {
                AddSpawn(ref networkId, remaining[k], trickleInterval * (k + 1), rng, placedPositions);
            }
        }

        private void AddSpawn(ref int networkId, int entryIndex, float time, System.Random rng, List<Vector3> placedPositions)
        {
            var position = FindSpawnPosition(rng, placedPositions);
            placedPositions.Add(position);
            var yaw = (float)(rng.NextDouble() * 360.0);
            schedule.Add(new ScheduledSpawn(networkId, entryIndex, position, yaw, time));
            networkId++;
        }

        private Vector3 FindSpawnPosition(System.Random rng, List<Vector3> placedPositions)
        {
            var basePos = transform.position;
            Vector3 candidate = basePos + Vector3.up * ((minHeight + maxHeight) * 0.5f);
            for (var attempt = 0; attempt < 12; attempt++)
            {
                candidate = basePos + new Vector3(
                    ((float)rng.NextDouble() - 0.5f) * spawnAreaSize.x,
                    minHeight + (float)rng.NextDouble() * (maxHeight - minHeight),
                    ((float)rng.NextDouble() - 0.5f) * spawnAreaSize.z);

                if (HasEnoughSpacing(candidate, placedPositions))
                {
                    return candidate;
                }
            }
            return candidate;
        }

        private bool HasEnoughSpacing(Vector3 position, List<Vector3> placedPositions)
        {
            var minSqr = minSpacing * minSpacing;
            foreach (var placed in placedPositions)
            {
                if ((placed - position).sqrMagnitude < minSqr)
                {
                    return false;
                }
            }
            return true;
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
