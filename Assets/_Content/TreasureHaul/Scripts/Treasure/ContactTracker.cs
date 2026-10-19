using System.Collections.Generic;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 再接触の判定（純粋。設計書 5章）。お宝ごとに、相手（お宝なら番号、そうでなければコライダーの ID）単位で接触を数える。
    /// 接触している間は新しい Enter を報告しない（同じ相手の別のコライダーとの接触も含む）。
    /// 接触が終わってから〔再減額の間隔〕たつまでの Enter も報告しない。時刻は引数。
    /// </summary>
    public sealed class ContactTracker
    {
        sealed class Entry
        {
            public int Active;
            public double EndedAt = double.NegativeInfinity;
        }

        readonly Dictionary<long, Entry> _entries = new Dictionary<long, Entry>();

        /// <summary>相手のお宝の番号から相手のキーを作る</summary>
        public static long KeyOfItem(int itemIndex) => itemIndex;
        /// <summary>お宝でない相手（コライダーの InstanceID）からキーを作る</summary>
        public static long KeyOfCollider(int instanceId) => (1L << 40) | (uint)instanceId;

        public int TrackedCount => _entries.Count;

        /// <summary>接触が始まった。報告してよい「新しい衝突」なら true</summary>
        public bool OnEnter(long key, double now, float redamageInterval)
        {
            if (!_entries.TryGetValue(key, out var e)) { e = new Entry(); _entries[key] = e; }
            bool wasTouching = e.Active > 0;
            e.Active++;
            if (wasTouching) return false;
            return now - e.EndedAt >= redamageInterval;
        }

        /// <summary>接触が終わった（同じ相手のすべてのコライダーと離れたとき、その時刻を覚える）</summary>
        public void OnExit(long key, double now)
        {
            if (!_entries.TryGetValue(key, out var e) || e.Active <= 0) return;
            e.Active--;
            if (e.Active == 0) e.EndedAt = now;
        }

        public void Clear() => _entries.Clear();
    }
}
