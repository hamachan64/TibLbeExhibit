using System.Collections.Generic;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 見た目・音・文言のまとめ役。SessionHost / SessionClient がこれ 1 つを持つ。
    /// プレーヤーの色（#43）は palette。
    /// </summary>
    [CreateAssetMenu(fileName = "ThContentSet", menuName = "Treasure Haul/Content Set")]
    public sealed class ThContentSet : ScriptableObject
    {
        public ThTextTable text;
        public ThAudioCatalog audio;
        public ThFxCatalog fx;
        public ThTreasureCatalog treasures;
        [Tooltip("プレーヤーの色（#43。ビーム・仲間のアバター・練習用お宝の色と記号）")] public ThPlayerPalette palette;

        /// <summary>重複・欠けの一覧（空なら問題なし）</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            if (text == null) errors.Add("ThTextTable が未設定"); else errors.AddRange(text.Validate());
            if (audio == null) errors.Add("ThAudioCatalog が未設定"); else errors.AddRange(audio.Validate());
            if (fx == null) errors.Add("ThFxCatalog が未設定"); else errors.AddRange(fx.Validate());
            if (treasures == null) errors.Add("ThTreasureCatalog が未設定"); else errors.AddRange(treasures.Validate());
            if (palette == null) errors.Add("ThPlayerPalette が未設定"); else errors.AddRange(palette.Validate());
            return errors;
        }

        /// <summary>起動時に呼ぶ。問題と、音・演出が空の ID を、それぞれ 1 行の警告にまとめて出す。</summary>
        public void LogWarnings(string who)
        {
            var errors = Validate();
            if (errors.Count > 0) Debug.LogWarning($"[{who}] ThContentSet に問題があります: {string.Join(" / ", errors)}");
            var empties = new List<string>();
            if (audio != null) foreach (var id in audio.EmptyIds()) empties.Add("音:" + id);
            if (fx != null) foreach (var id in fx.EmptyIds()) empties.Add("演出:" + id);
            if (empties.Count > 0) Debug.LogWarning($"[{who}] 中身が空の音・演出（#38 以降で入れる）: {string.Join(", ", empties)}");
        }
    }
}
