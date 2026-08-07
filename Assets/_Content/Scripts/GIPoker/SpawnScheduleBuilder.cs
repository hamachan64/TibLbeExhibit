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

        /// <summary>このエントリのカードランク（ボーナスタイムの重み付き抽選に使用）</summary>
        public readonly CardRank Rank;

        /// <summary>後方互換コンストラクタ。Rank 未指定時は D 扱い（重み付き抽選を使わない既存呼び出し向け）</summary>
        public SpawnEntrySpec(int initialCount, int maxCount)
            : this(initialCount, maxCount, CardRank.D)
        {
        }

        public SpawnEntrySpec(int initialCount, int maxCount, CardRank rank)
        {
            InitialCount = initialCount;
            MaxCount = maxCount;
            Rank = rank;
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

        /// <summary>
        /// このアンカーに出現してよいカタログエントリのインデックス
        /// （null または空 = 全アイテム可の汎用アンカー。従来の2引数コンストラクタは汎用になる）
        /// </summary>
        public readonly IReadOnlyList<int> AllowedEntryIndices;

        /// <summary>アンカーのワールド回転（AlignRotation が true のときスポーンへ適用される）</summary>
        public readonly Quaternion Rotation;

        /// <summary>スポーン時にアンカーの回転を適用するか（false ならランダム yaw）</summary>
        public readonly bool AlignRotation;

        public SpawnAnchorSpec(Vector3 position, bool isElevated)
            : this(position, isElevated, null, Quaternion.identity, alignRotation: false)
        {
        }

        public SpawnAnchorSpec(
            Vector3 position,
            bool isElevated,
            IReadOnlyList<int> allowedEntryIndices,
            Quaternion rotation,
            bool alignRotation)
        {
            Position = position;
            IsElevated = isElevated;
            AllowedEntryIndices = allowedEntryIndices;
            Rotation = rotation;
            AlignRotation = alignRotation;
        }

        /// <summary>専用アンカー（出現アイテムが cardId 指定で限定されている）かどうか</summary>
        public bool IsDedicated => AllowedEntryIndices != null && AllowedEntryIndices.Count > 0;

        /// <summary>指定エントリのアイテムがこのアンカーに出現できるか</summary>
        public bool Accepts(int entryIndex)
        {
            if (!IsDedicated)
            {
                return true;
            }
            for (var i = 0; i < AllowedEntryIndices.Count; i++)
            {
                if (AllowedEntryIndices[i] == entryIndex)
                {
                    return true;
                }
            }
            return false;
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

        /// <summary>
        /// 適用すべきワールド回転。整列アンカー（AlignRotation）ならアンカーの回転、
        /// それ以外は Quaternion.Euler(0, YawDegrees, 0) と一致する。
        /// </summary>
        public readonly Quaternion Rotation;

        public ScheduledSpawn(int networkId, int entryIndex, Vector3 position, float yawDegrees, float time, bool usedAnchor)
            : this(networkId, entryIndex, position, yawDegrees, time, usedAnchor, Quaternion.Euler(0f, yawDegrees, 0f))
        {
        }

        public ScheduledSpawn(int networkId, int entryIndex, Vector3 position, float yawDegrees, float time, bool usedAnchor, Quaternion rotation)
        {
            NetworkId = networkId;
            EntryIndex = entryIndex;
            Position = position;
            YawDegrees = yawDegrees;
            Time = time;
            UsedAnchor = usedAnchor;
            Rotation = rotation;
        }
    }

    /// <summary>
    /// ItemSpawnManager のスポーンスケジュールを決定論的に構築する純ロジッククラス
    /// （MonoBehaviour 非依存。EditMode テストから直接インスタンス化して検証できる）。
    ///
    /// 割り当て優先順位:
    /// 1. 呼び出し側が階層順（Transform ルート順→siblingIndex 深さ優先）で渡した
    ///    StageSpawnAnchor 候補から、共有 rng の消費のみで未使用の1点を選び割り当てる
    ///    （選ばれたアンカーはプールから除去され、同一スケジュール内で二重割り当てされない）。
    ///    候補は二段プール: まず「そのアイテム専用のアンカー（AllowedEntryIndices 指定）」を優先し、
    ///    無ければ「汎用アンカー（指定なし）」から選ぶ。専用アンカーは指定外のアイテムに消費されない
    /// 2. 使えるアンカーが無ければ、従来の箱型ランダム配置（spawnAreaSize 内・minSpacing 確保）に
    ///    フォールバックする（枯渇判定はプールの残数と指定のみで決まるため決定論的）
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
        /// StageSpawnAnchor.AllowedCardIds（cardId 文字列）をカタログエントリのインデックス列へ解決する。
        /// - allowedCardIds が null/空 → null（＝汎用アンカー）
        /// - 未知の cardId は無視（unknownCardIds に収集。呼び出し側で警告ログを出す）
        /// - 非空だが全件未知 → 空リスト（＝どのアイテムにもマッチしない専用アンカー。
        ///   汎用に落とすと誤配置になるため、あえて「何も出ないアンカー」として扱う）
        /// </summary>
        public static IReadOnlyList<int> ResolveAllowedEntryIndices(
            IReadOnlyList<string> allowedCardIds,
            IReadOnlyDictionary<string, int> cardIdToEntryIndex,
            List<string> unknownCardIds = null)
        {
            if (allowedCardIds == null || allowedCardIds.Count == 0)
            {
                return null;
            }
            var indices = new List<int>(allowedCardIds.Count);
            for (var i = 0; i < allowedCardIds.Count; i++)
            {
                var cardId = allowedCardIds[i];
                if (!string.IsNullOrEmpty(cardId) && cardIdToEntryIndex.TryGetValue(cardId, out var entryIndex))
                {
                    if (!indices.Contains(entryIndex))
                    {
                        indices.Add(entryIndex);
                    }
                }
                else
                {
                    unknownCardIds?.Add(cardId);
                }
            }
            return indices;
        }

        /// <summary>
        /// シードから全出現スケジュールを決定論的に構築する。
        /// UnityEngine.Random は他所の消費で狂うため System.Random を使う。
        /// </summary>
        /// <param name="seed">GameSession.SessionSeed（全クライアント共有）</param>
        /// <param name="entries">カタログの出現エントリ一覧（カタログ順）</param>
        /// <param name="anchors">階層順に列挙済みの StageSpawnAnchor 候補（呼び出し側が順序を保証する）</param>
        /// <remarks>
        /// ボーナスタイム（終盤の出現率・レア度アップ）を使わない従来呼び出し。
        /// 内部では新オーバーロードへ「重み未指定」（＝ボーナス無効）で委譲するだけなので、
        /// 追加出現分は必ず既存の Fisher-Yates シャッフルのコードパスで決定される（後方互換）。
        /// </remarks>
        public List<ScheduledSpawn> Build(int seed, IReadOnlyList<SpawnEntrySpec> entries, IReadOnlyList<SpawnAnchorSpec> anchors)
        {
            return Build(seed, entries, anchors, float.PositiveInfinity, trickleInterval, null, null);
        }

        /// <summary>
        /// ボーナスタイム（終盤の出現率・レア度アップ）に対応したオーバーロード。
        /// </summary>
        /// <param name="seed">GameSession.SessionSeed（全クライアント共有）</param>
        /// <param name="entries">カタログの出現エントリ一覧（カタログ順）</param>
        /// <param name="anchors">階層順に列挙済みの StageSpawnAnchor 候補（呼び出し側が順序を保証する）</param>
        /// <param name="bonusWindowStartSeconds">
        /// ボーナスタイム開始時刻（セッション経過秒）。この時刻以降にスケジュールされる追加出現は
        /// bonusTrickleInterval・bonusWeights に従う。
        /// </param>
        /// <param name="bonusTrickleInterval">ボーナスタイム中の追加出現間隔（秒）</param>
        /// <param name="normalWeights">通常時（ボーナスタイム前）のランク別抽選重み。null ならランク均等（重み1.0）</param>
        /// <param name="bonusWeights">ボーナスタイム中のランク別抽選重み。null ならランク均等（重み1.0）</param>
        /// <remarks>
        /// normalWeights と bonusWeights がどちらも null（＝重み付け機能が実際には呼ばれていない）場合は、
        /// bonusWindowStartSeconds・bonusTrickleInterval の値に関わらず既存の Fisher-Yates コードパスを
        /// 1行も変更せずそのまま使う。重み付き抽選は、どちらか一方でも指定されたときだけ通る別コードパス。
        /// </remarks>
        public List<ScheduledSpawn> Build(
            int seed,
            IReadOnlyList<SpawnEntrySpec> entries,
            IReadOnlyList<SpawnAnchorSpec> anchors,
            float bonusWindowStartSeconds,
            float bonusTrickleInterval,
            IReadOnlyDictionary<CardRank, float> normalWeights,
            IReadOnlyDictionary<CardRank, float> bonusWeights)
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

            // 重み付け機能が実際に有効（normalWeights / bonusWeights のいずれかが指定されている）でなければ、
            // 既存の Fisher-Yates コードパスをそのまま使う（後方互換・ビット単位一致）。
            var weightingEnabled = normalWeights != null || bonusWeights != null;
            if (!weightingEnabled)
            {
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
            }
            else
            {
                // 重み付き抽選（ボーナスタイム対応）。remaining をプールとして、時刻が進むごとに
                // 重み付き乱数で1件ずつ選んで除去する。プールに残る要素数はエントリごとに変わらないため、
                // maxCount 上限は Fisher-Yates 版と同様に構造的に守られる。
                //
                // 間隔(interval)は「加算前」の currentTime で、重み(weights)は「加算後」の currentTime で
                // 判定しているため、ボーナスタイム開始をまたぐ1件だけは「間隔は通常ペースのまま・
                // 抽選重みはボーナス扱い」という非対称な挙動になる（意図的な仕様。境界の1件だけを
                // 特別扱いすると分岐が増え、決定論性の検証も複雑になるため許容している）。
                var currentTime = 0f;
                var pool = remaining;
                while (pool.Count > 0)
                {
                    var interval = currentTime >= bonusWindowStartSeconds ? bonusTrickleInterval : trickleInterval;
                    currentTime += interval;
                    var weights = currentTime >= bonusWindowStartSeconds ? bonusWeights : normalWeights;
                    var selectedEntryIndex = WeightedPick(pool, entries, weights, rng);
                    AddSpawn(schedule, ref networkId, selectedEntryIndex, currentTime, rng, placedPositions, availableAnchors);
                }
            }

            return schedule;
        }

        /// <summary>
        /// pool（エントリインデックスの多重集合）から、ランク別重みに従って1件を重み付き乱択で選び、
        /// pool から除去して返す。weights が null、または該当ランクの重みが未定義の場合は 1.0 として扱う。
        /// 全要素の重み合計が0以下（異常値）の場合は先頭要素を選ぶ（決定論性を維持したフォールバック）。
        /// </summary>
        private static int WeightedPick(
            List<int> pool,
            IReadOnlyList<SpawnEntrySpec> entries,
            IReadOnlyDictionary<CardRank, float> weights,
            System.Random rng)
        {
            var total = 0.0;
            for (var i = 0; i < pool.Count; i++)
            {
                total += GetWeight(entries[pool[i]].Rank, weights);
            }

            if (total <= 0.0)
            {
                var fallbackIndex = pool[0];
                pool.RemoveAt(0);
                return fallbackIndex;
            }

            var roll = rng.NextDouble() * total;
            var cumulative = 0.0;
            for (var i = 0; i < pool.Count; i++)
            {
                cumulative += GetWeight(entries[pool[i]].Rank, weights);
                if (roll < cumulative)
                {
                    var selected = pool[i];
                    pool.RemoveAt(i);
                    return selected;
                }
            }

            // 浮動小数点誤差で累積和がわずかに roll を下回らなかった場合の保険（最後の要素を選ぶ）
            var lastIndex = pool[pool.Count - 1];
            pool.RemoveAt(pool.Count - 1);
            return lastIndex;
        }

        private static float GetWeight(CardRank rank, IReadOnlyDictionary<CardRank, float> weights)
        {
            if (weights != null && weights.TryGetValue(rank, out var weight))
            {
                return Mathf.Max(0f, weight);
            }
            return 1f;
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
            var usedAnchor = TryTakeAnchorFor(entryIndex, availableAnchors, rng, out var anchor);
            var position = usedAnchor ? anchor.Position : FindBoxPosition(rng, placedPositions);
            placedPositions.Add(position);
            // yaw の乱数は整列アンカー使用時も必ず消費する（rng 消費順を分岐させないため。
            // 消費順が分岐すると、整列アンカーの有無で後続すべてのスポーン位置が変わってしまう）
            var yaw = (float)(rng.NextDouble() * 360.0);
            var rotation = usedAnchor && anchor.AlignRotation
                ? anchor.Rotation
                : Quaternion.Euler(0f, yaw, 0f);
            schedule.Add(new ScheduledSpawn(networkId, entryIndex, position, yaw, time, usedAnchor, rotation));
            networkId++;
        }

        /// <summary>
        /// 未使用アンカーの中から、指定エントリに使えるものを rng で1点選び、プールから除去して返す。
        ///
        /// 選択は二段プール:
        /// 1. 専用アンカー（AllowedEntryIndices に entryIndex を含む）を優先
        /// 2. 無ければ汎用アンカー（AllowedEntryIndices が null/空）
        /// 3. どちらも無ければ rng を消費せず false（箱型フォールバックへ）
        ///
        /// 全アンカーが汎用のとき、候補列はプール全体と順序・要素数が一致するため、
        /// rng.Next の引数・選択・swap-remove まで専用アンカー導入前の実装とビット単位で一致する（後方互換）。
        /// </summary>
        private static bool TryTakeAnchorFor(int entryIndex, List<SpawnAnchorSpec> availableAnchors, System.Random rng, out SpawnAnchorSpec anchor)
        {
            if (availableAnchors.Count == 0)
            {
                anchor = default;
                return false;
            }

            var candidates = CollectCandidateIndices(availableAnchors, entryIndex, dedicatedOnly: true);
            if (candidates.Count == 0)
            {
                candidates = CollectCandidateIndices(availableAnchors, entryIndex, dedicatedOnly: false);
            }
            if (candidates.Count == 0)
            {
                anchor = default;
                return false;
            }

            var index = candidates[rng.Next(candidates.Count)];
            anchor = availableAnchors[index];
            // Swap-remove（O(1)）。以後の選択は毎回 rng.Next で行うため、プール内の残り順序は結果に影響しない
            var last = availableAnchors.Count - 1;
            availableAnchors[index] = availableAnchors[last];
            availableAnchors.RemoveAt(last);
            return true;
        }

        /// <summary>
        /// プール内から候補アンカーのインデックス列をプール順で集める。
        /// dedicatedOnly=true なら「entryIndex を許可する専用アンカー」、
        /// false なら「汎用アンカー（指定なし）」を集める。
        /// </summary>
        private static List<int> CollectCandidateIndices(List<SpawnAnchorSpec> availableAnchors, int entryIndex, bool dedicatedOnly)
        {
            var candidates = new List<int>(availableAnchors.Count);
            for (var i = 0; i < availableAnchors.Count; i++)
            {
                var spec = availableAnchors[i];
                if (dedicatedOnly ? spec.IsDedicated && spec.Accepts(entryIndex) : !spec.IsDedicated)
                {
                    candidates.Add(i);
                }
            }
            return candidates;
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
