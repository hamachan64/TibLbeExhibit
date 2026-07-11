using System.Collections.Generic;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// スポーンスケジュール構築に必要なアイテムカタログ1エントリ分の情報。
    /// ItemCatalog（ScriptableObject）に依存させないための最小データ構造（テスト容易性のため）。
    /// </summary>
    public readonly struct SpawnEntrySpec
    {
        /// <summary>セッション開始時に出現する数</summary>
        public readonly int InitialCount;

        /// <summary>セッション全体での出現上限（カード化限度枚数）</summary>
        public readonly int MaxCount;

        public SpawnEntrySpec(int initialCount, int maxCount)
        {
            InitialCount = initialCount;
            MaxCount = maxCount;
        }
    }

    /// <summary>
    /// スポーンスケジュール構築に必要なアンカー1点分の情報。
    /// StageSpawnAnchor（MonoBehaviour）に依存させないための最小データ構造（テスト容易性のため）。
    /// </summary>
    public readonly struct SpawnAnchorSpec
    {
        /// <summary>アンカーのワールド座標</summary>
        public readonly Vector3 Position;

        /// <summary>台座上（true）か床パッチ上（false）か</summary>
        public readonly bool IsElevated;

        public SpawnAnchorSpec(Vector3 position, bool isElevated)
        {
            Position = position;
            IsElevated = isElevated;
        }
    }

    /// <summary>1件分の決定済みスポーン情報。</summary>
    public readonly struct ScheduledSpawn
    {
        public readonly int NetworkId;
        public readonly int EntryIndex;
        public readonly Vector3 Position;
        public readonly float YawDegrees;
        public readonly float Time;

        /// <summary>StageSpawnAnchor の位置を使用したか（false は箱型ランダム配置へのフォールバック）</summary>
        public readonly bool UsedAnchor;

        public ScheduledSpawn(int networkId, int entryIndex, Vector3 position, float yawDegrees, float time, bool usedAnchor)
        {
            NetworkId = networkId;
            EntryIndex = entryIndex;
            Position = position;
            YawDegrees = yawDegrees;
            Time = time;
            UsedAnchor = usedAnchor;
        }
    }

    /// <summary>
    /// ItemSpawnManager のスポーンスケジュールを決定論的に構築する純ロジッククラス
    /// （MonoBehaviour 非依存。EditMode テストから直接インスタンス化して検証できる）。
    ///
    /// 割り当て優先順位:
    /// 1. 呼び出し側が階層順（Transform ルート順→siblingIndex 深さ優先）で渡した
    ///    StageSpawnAnchor 候補から、共有 rng の消費のみで未使用の1点を選び割り当てる
    ///    （選ばれたアンカーはプールから除去され、同一スケジュール内で二重割り当てされない）
    /// 2. アンカーが枯渇したら、従来の箱型ランダム配置（spawnAreaSize 内・minSpacing 確保）に
    ///    フォールバックする（枯渇判定はプールの残数のみで決まるため決定論的）
    ///
    /// rng の消費順は「初期出現分をカタログ順→追加出現分をシャッフルして時刻順」という
    /// 従来の ItemSpawnManager.BuildSchedule の消費順をそのまま踏襲する。
    /// アンカーが0件の場合、アンカー選択は rng を一切消費しないため、
    /// 本クラス導入前の挙動とビット単位で同一の結果になる（後方互換）。
    /// </summary>
    public sealed class SpawnScheduleBuilder
    {
        private readonly Vector3 basePosition;
        private readonly Vector3 spawnAreaSize;
        private readonly float minHeight;
        private readonly float maxHeight;
        private readonly float minSpacing;
        private readonly float trickleInterval;

        public SpawnScheduleBuilder(
            Vector3 basePosition,
            Vector3 spawnAreaSize,
            float minHeight,
            float maxHeight,
            float minSpacing,
            float trickleInterval)
        {
            this.basePosition = basePosition;
            this.spawnAreaSize = spawnAreaSize;
            this.minHeight = minHeight;
            this.maxHeight = maxHeight;
            this.minSpacing = minSpacing;
            this.trickleInterval = trickleInterval;
        }

        /// <summary>
        /// シードから全出現スケジュールを決定論的に構築する。
        /// UnityEngine.Random は他所の消費で狂うため System.Random を使う。
        /// </summary>
        /// <param name="seed">GameSession.SessionSeed（全クライアント共有）</param>
        /// <param name="entries">カタログの出現エントリ一覧（カタログ順）</param>
        /// <param name="anchors">階層順に列挙済みの StageSpawnAnchor 候補（呼び出し側が順序を保証する）</param>
        public List<ScheduledSpawn> Build(int seed, IReadOnlyList<SpawnEntrySpec> entries, IReadOnlyList<SpawnAnchorSpec> anchors)
        {
            var schedule = new List<ScheduledSpawn>();
            if (entries == null || entries.Count == 0)
            {
                return schedule;
            }

            var rng = new System.Random(seed);
            var placedPositions = new List<Vector3>();
            var availableAnchors = anchors != null ? new List<SpawnAnchorSpec>(anchors) : new List<SpawnAnchorSpec>();
            var networkId = 0;

            // 1. 初期出現分（時刻0）
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var count = Mathf.Min(entry.InitialCount, entry.MaxCount);
                for (var n = 0; n < count; n++)
                {
                    AddSpawn(schedule, ref networkId, i, 0f, rng, placedPositions, availableAnchors);
                }
            }

            // 2. 追加出現分: 残数をエントリ順に列挙し、シャッフルして間隔ごとに配置
            var remaining = new List<int>();
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var rest = entry.MaxCount - Mathf.Min(entry.InitialCount, entry.MaxCount);
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
                AddSpawn(schedule, ref networkId, remaining[k], trickleInterval * (k + 1), rng, placedPositions, availableAnchors);
            }

            return schedule;
        }

        private void AddSpawn(
            List<ScheduledSpawn> schedule,
            ref int networkId,
            int entryIndex,
            float time,
            System.Random rng,
            List<Vector3> placedPositions,
            List<SpawnAnchorSpec> availableAnchors)
        {
            var usedAnchor = TryTakeAnchorPosition(availableAnchors, rng, out var position);
            if (!usedAnchor)
            {
                position = FindBoxPosition(rng, placedPositions);
            }
            placedPositions.Add(position);
            var yaw = (float)(rng.NextDouble() * 360.0);
            schedule.Add(new ScheduledSpawn(networkId, entryIndex, position, yaw, time, usedAnchor));
            networkId++;
        }

        /// <summary>
        /// 未使用アンカーの中から rng で1点選び、プールから除去して返す。
        /// アンカーが枯渇している（プールが空の）場合は rng を消費せず false を返す
        /// （＝アンカー0件のシーンでは本メソッドが一切 rng を消費しないため旧挙動と完全一致する）。
        /// </summary>
        private static bool TryTakeAnchorPosition(List<SpawnAnchorSpec> availableAnchors, System.Random rng, out Vector3 position)
        {
            if (availableAnchors.Count == 0)
            {
                position = default;
                return false;
            }
            var index = rng.Next(availableAnchors.Count);
            position = availableAnchors[index].Position;
            // Swap-remove（O(1)）。以後の選択は毎回 rng.Next で行うため、プール内の残り順序は結果に影響しない
            var last = availableAnchors.Count - 1;
            availableAnchors[index] = availableAnchors[last];
            availableAnchors.RemoveAt(last);
            return true;
        }

        /// <summary>従来の箱型ランダム配置（spawnAreaSize 内で minSpacing を確保する）。</summary>
        private Vector3 FindBoxPosition(System.Random rng, List<Vector3> placedPositions)
        {
            Vector3 candidate = basePosition + Vector3.up * ((minHeight + maxHeight) * 0.5f);
            for (var attempt = 0; attempt < 12; attempt++)
            {
                candidate = basePosition + new Vector3(
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
    }
}
