using System;
using System.Collections.Generic;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>演出の ID。<b>末尾にだけ足す</b>（並べ替え・途中への挿入・削除をしない）。</summary>
    public enum ThFxId
    {
        /// <summary>使わない（2026-10-21、光る持ち手は廃止した）。列挙からは消せないので残す</summary>
        HandleNormal,
        /// <summary>使わない（2026-10-21、光る持ち手は廃止した。ビームの線は <see cref="Beam"/>）。列挙からは消せないので残す</summary>
        HandleAimed,
        DiscoverLight,
        Crack1,
        Crack2,
        Shatter,
        /// <summary>使わない（2026-10-21、荷台の案内はやめた）。列挙からは消せないので残す</summary>
        CargoGlow,
        /// <summary>ビームが当たっているお宝全体の輪郭（背面法のマテリアル）</summary>
        AimOutline,
        /// <summary>ビームの線のマテリアル</summary>
        Beam,
    }

    [Serializable]
    public class ThFxEntry
    {
        public ThFxId id;
        [Tooltip("パーティクル等のプレハブ（任意）")] public GameObject prefab;
        [Tooltip("マテリアル（任意）")] public Material material;

        public ThFxEntry() { }
        public ThFxEntry(ThFxId id) { this.id = id; }

        public bool IsEmpty => prefab == null && material == null;
    }

    /// <summary>演出の一覧。中身は #38 以降で入れる。</summary>
    [CreateAssetMenu(fileName = "ThFxCatalog", menuName = "Treasure Haul/Fx Catalog")]
    public sealed class ThFxCatalog : ScriptableObject
    {
        public List<ThFxEntry> entries = DefaultEntries();

        public static List<ThFxEntry> DefaultEntries()
        {
            var list = new List<ThFxEntry>();
            foreach (ThFxId id in Enum.GetValues(typeof(ThFxId))) list.Add(new ThFxEntry(id));
            return list;
        }

        public ThFxEntry Find(ThFxId id)
        {
            foreach (var e in entries) if (e != null && e.id == id) return e;
            return null;
        }

        public List<string> Validate()
        {
            var errors = new List<string>();
            var seen = new HashSet<ThFxId>();
            foreach (var e in entries)
            {
                if (e == null) { errors.Add("空の要素があります"); continue; }
                if (!seen.Add(e.id)) errors.Add($"ID が重複しています: {e.id}");
            }
            foreach (ThFxId id in Enum.GetValues(typeof(ThFxId)))
                if (!seen.Contains(id)) errors.Add($"定義が欠けています: {id}");
            return errors;
        }

        public List<ThFxId> EmptyIds()
        {
            var list = new List<ThFxId>();
            foreach (var e in entries) if (e != null && e.IsEmpty) list.Add(e.id);
            return list;
        }
    }
}
