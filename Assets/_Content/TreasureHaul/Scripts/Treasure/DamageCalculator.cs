namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>減額 1 回ぶんの結果</summary>
    public readonly struct DamageResult
    {
        /// <summary>減額を適用したか（すでに消滅していたら false）</summary>
        public readonly bool Applied;
        /// <summary>今回の減額の量（G）</summary>
        public readonly int Amount;
        /// <summary>減額後の価値</summary>
        public readonly int Value;
        /// <summary>減額後のぶつけた回数</summary>
        public readonly int Hits;
        /// <summary>3 回目で砕けて消えるか</summary>
        public readonly bool Destroyed;

        public DamageResult(bool applied, int amount, int value, int hits, bool destroyed)
        {
            Applied = applied; Amount = amount; Value = value; Hits = hits; Destroyed = destroyed;
        }
    }

    /// <summary>
    /// 減額の計算（設計書 5章。純粋）。回数 n 回目の減額 = 元の値段 × 減額率[n] / 100。今の価値より大きければ今の価値で止める。
    /// 3 回目は減額率の合計が 100% でなくても、価値を必ず 0 にして消滅にする。
    /// </summary>
    public static class DamageCalculator
    {
        public const int MaxHits = 3;

        /// <param name="baseValue">元の値段</param>
        /// <param name="value">今の価値</param>
        /// <param name="hits">これまでのぶつけた回数</param>
        public static DamageResult Apply(int baseValue, int value, int hits, int percent1, int percent2, int percent3)
        {
            if (hits >= MaxHits) return new DamageResult(false, 0, value, hits, true);
            int next = hits + 1;
            int percent = next == 1 ? percent1 : next == 2 ? percent2 : percent3;
            int amount = baseValue * percent / 100;
            if (amount > value) amount = value;
            if (amount < 0) amount = 0;
            int after = value - amount;
            if (next >= MaxHits)
            {
                amount = value;   // 3 回目は必ず 0
                after = 0;
            }
            return new DamageResult(true, amount, after, next, next >= MaxHits);
        }

        public static DamageResult Apply(int baseValue, int value, int hits, ThTreasureParams p) =>
            Apply(baseValue, value, hits, p.damagePercent1, p.damagePercent2, p.damagePercent3);
    }
}
