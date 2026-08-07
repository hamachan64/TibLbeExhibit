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

        /// <summary>
        /// 白煙＋衝撃波VFX（VFX_BinderSummonPoof）。バインダー出し入れ（Lサインジェスチャー）時に加え、
        /// カード化・ゲイン（アイテム化）の瞬間にもカードサイズへ縮小して再生する
        /// （CardizableItemBase.PlayCardSizedSmokeVfx を参照）。
        /// </summary>
        public static GameObject BinderSmokeVfx;

        /// <summary>
        /// 上記の白煙VFX（BinderSmokeVfx）再生と同時に鳴らすSE。
        /// 音源が未準備のため既定はnull（未設定の間はSEなしでVFXのみ再生される）。
        /// 音源が用意でき次第、GIPokerBootstrapのInspectorから設定する。
        /// </summary>
        public static AudioClip SmokeSfxClip;

        /// <summary>SmokeSfxClip の再生音量（GIPokerBootstrapのInspectorから設定）</summary>
        public static float SmokeSfxVolume = 1f;

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
        /// ランク単位の数値バランス設定（得点・出現重み・宝籤重み・テーマカラー）。
        /// 未設定（null）でも各参照元（PokerHandEvaluator/ItemSpawnManager/CardEffectService/CardVisualFactory）は
        /// 現行と同じ値へフォールバックする。
        /// </summary>
        public static CardRankConfig RankConfig;

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

        /// <summary>
        /// SEを指定位置でワンショット再生する（null安全）。clipが未設定（null）の場合は何もしない
        /// （SmokeSfxClip のように音源が後日追加される想定のフィールドを、未設定のまま安全に呼べるようにする）。
        /// AudioSource.PlayClipAtPoint は既定で3D減衰がかかってしまうため使わず、一時AudioSourceを
        /// spatialBlend=0（2D再生）で生成する。TutorialAudioController と同じく、ノイズの多い展示環境
        /// でも聞き取りやすくする方針に揃えるため。再生完了後は自動破棄する。
        /// </summary>
        public static void PlaySfxAtPoint(AudioClip clip, Vector3 position, float volume = 1f)
        {
            if (clip == null)
            {
                return;
            }
            var sfxGo = new GameObject("OneShotSfx(GIPoker)");
            sfxGo.transform.position = position;
            var source = sfxGo.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = volume;
            source.spatialBlend = 0f;
            source.playOnAwake = false;
            source.Play();
            Object.Destroy(sfxGo, clip.length);
        }
    }
}
