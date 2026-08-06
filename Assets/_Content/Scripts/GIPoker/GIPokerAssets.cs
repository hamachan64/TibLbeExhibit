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

        /// <summary>バインダーの3Dモデル（ページめくり可能な本、Blender製FBX）</summary>
        public static GameObject CardBinderModelPrefab;

        /// <summary>バインダー出し入れ（Lサインジェスチャー）時の白煙＋衝撃波VFX（VFX_BinderSummonPoof）</summary>
        public static GameObject BinderSmokeVfx;

        /// <summary>
        /// チュートリアル各ステップの読み上げ音声（Step1.mp3〜Step7.mp3、7要素、null許容）。
        /// 配列の並びは TutorialSequence.CreateDefaultSteps() のステップ順と1対1で対応する。
        /// </summary>
        public static AudioClip[] TutorialStepVoiceClips;

        /// <summary>チュートリアル冒頭（ステップ1/Step1.mp3より前）の前振り音声（Step0.mp3）</summary>
        public static AudioClip TutorialIntroVoiceClip;

        /// <summary>チュートリアル中にループ再生するBGM（BGM_Tutorial.mp3）</summary>
        public static AudioClip TutorialBgmClip;

        /// <summary>Tutorial→Playing遷移時に1回再生する開始の掛け声（Start.mp3）</summary>
        public static AudioClip SessionStartVoiceClip;

        /// <summary>本編（Playing）中にループ再生するBGM（BGM_Play.mp3）</summary>
        public static AudioClip MainBgmClip;

        /// <summary>
        /// VFXプレハブを生成して一定時間後に破棄する（null安全）。
        /// scale はプレハブ既定の1倍から変更したい場合のみ指定する
        /// （例: 汎用VFXアセットを手のひらサイズの小物向けに控えめなスケールへ縮小する）。
        /// </summary>
        public static GameObject SpawnVfx(GameObject prefab, Vector3 position, float lifetime = 5f, float scale = 1f)
        {
            if (prefab == null)
            {
                return null;
            }
            var vfx = Object.Instantiate(prefab, position, Quaternion.identity);
            if (!Mathf.Approximately(scale, 1f))
            {
                vfx.transform.localScale *= scale;
            }
            Object.Destroy(vfx, lifetime);
            return vfx;
        }
    }
}
