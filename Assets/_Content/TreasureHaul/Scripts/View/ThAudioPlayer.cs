using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>音の ID から鳴らす（ThAudioCatalog）。音が空の ID は何もしない（素材が入るまでは無音）。</summary>
    public static class ThAudioPlayer
    {
        public static bool Play(ThAudioCatalog catalog, ThSoundId id, Vector3 worldPos)
        {
            if (catalog == null) return false;
            var e = catalog.Find(id);
            if (e == null || e.IsEmpty) return false;

            var valid = new System.Collections.Generic.List<AudioClip>();
            foreach (var c in e.clips) if (c != null) valid.Add(c);
            var clip = valid[Random.Range(0, valid.Count)];

            var go = new GameObject("ThSound_" + id);
            go.transform.position = worldPos;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = e.volume;
            src.spatialBlend = e.spatial ? 1f : 0f;
            src.Play();
            Object.Destroy(go, clip.length + 0.1f);
            return true;
        }
    }
}
