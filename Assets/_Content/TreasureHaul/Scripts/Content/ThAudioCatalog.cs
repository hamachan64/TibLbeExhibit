using System;
using System.Collections.Generic;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>音の ID。<b>末尾にだけ足す</b>（並べ替え・途中への挿入・削除をしない）。</summary>
    public enum ThSoundId
    {
        Discover,
        Damage,
        Shatter,
        OpeningNarration,
        OpeningSfx,
    }

    [Serializable]
    public class ThAudioEntry
    {
        public ThSoundId id;
        [Tooltip("複数ならランダムに 1 つ")] public AudioClip[] clips = new AudioClip[0];
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("その場所から鳴らす（3D）")] public bool spatial;
        [Tooltip("入手元（THIRD_PARTY_NOTICES.md の名前）")] public string source = "";

        public ThAudioEntry() { }
        public ThAudioEntry(ThSoundId id, bool spatial) { this.id = id; this.spatial = spatial; }

        public bool IsEmpty
        {
            get
            {
                if (clips == null || clips.Length == 0) return true;
                foreach (var c in clips) if (c != null) return false;
                return true;
            }
        }
    }

    /// <summary>音の一覧。中身（音）は #38 以降で入れる。</summary>
    [CreateAssetMenu(fileName = "ThAudioCatalog", menuName = "Treasure Haul/Audio Catalog")]
    public sealed class ThAudioCatalog : ScriptableObject
    {
        public List<ThAudioEntry> entries = DefaultEntries();

        public static List<ThAudioEntry> DefaultEntries() => new List<ThAudioEntry>
        {
            new ThAudioEntry(ThSoundId.Discover, false),
            new ThAudioEntry(ThSoundId.Damage, true),
            new ThAudioEntry(ThSoundId.Shatter, true),
            new ThAudioEntry(ThSoundId.OpeningNarration, false),
            new ThAudioEntry(ThSoundId.OpeningSfx, false),
        };

        public ThAudioEntry Find(ThSoundId id)
        {
            foreach (var e in entries) if (e != null && e.id == id) return e;
            return null;
        }

        /// <summary>重複した ID と、定義の欠けを返す。音が空なのは許す。</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            var seen = new HashSet<ThSoundId>();
            foreach (var e in entries)
            {
                if (e == null) { errors.Add("空の要素があります"); continue; }
                if (!seen.Add(e.id)) errors.Add($"ID が重複しています: {e.id}");
            }
            foreach (ThSoundId id in Enum.GetValues(typeof(ThSoundId)))
                if (!seen.Contains(id)) errors.Add($"定義が欠けています: {id}");
            return errors;
        }

        /// <summary>音が空の ID（起動時に 1 行の警告にまとめる）</summary>
        public List<ThSoundId> EmptyIds()
        {
            var list = new List<ThSoundId>();
            foreach (var e in entries) if (e != null && e.IsEmpty) list.Add(e.id);
            return list;
        }
    }
}
