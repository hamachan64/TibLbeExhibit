using TMPro;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// ランタイム生成されるビジュアル（カード・UI・VFX）が参照する共有アセットの置き場。
    /// GIPokerBootstrap がシーン上の SerializeField から起動時に流し込む。
    /// すべて null 許容（未設定でもゲームロジックは動作する）。
    /// </summary>
    public static class GIPokerAssets
    {
        /// <summary>日本語対応TMPフォント（Noto Sans JP SDF）</summary>
        public static TMP_FontAsset JapaneseFont;

        /// <summary>浮遊カードに常時付けるキラキラVFX</summary>
        public static GameObject CardSparkleVfx;

        /// <summary>カード化した瞬間のVFX</summary>
        public static GameObject CardifyVfx;

        /// <summary>ゲイン（具現化）した瞬間のVFX</summary>
        public static GameObject GainVfx;

        /// <summary>1分ルールで消滅する瞬間のVFX</summary>
        public static GameObject ExpireVfx;

        /// <summary>リスキーダイス「大吉」演出VFX</summary>
        public static GameObject DiceBlessVfx;

        /// <summary>リスキーダイス「大凶」演出VFX</summary>
        public static GameObject DiceCurseVfx;

        /// <summary>VFXプレハブを生成して一定時間後に破棄する（null安全）</summary>
        public static GameObject SpawnVfx(GameObject prefab, Vector3 position, float lifetime = 5f)
        {
            if (prefab == null)
            {
                return null;
            }
            var vfx = Object.Instantiate(prefab, position, Quaternion.identity);
            Object.Destroy(vfx, lifetime);
            return vfx;
        }
    }
}
