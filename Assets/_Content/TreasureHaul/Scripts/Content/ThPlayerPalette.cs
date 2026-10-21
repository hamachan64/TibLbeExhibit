using System;
using System.Collections.Generic;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>プレーヤーの色 1 枠（練習の初めに 1 人 1 色）。ビーム・仲間に見えるアバター・練習用お宝をこの色にする</summary>
    [Serializable]
    public class ThPlayerColorSlot
    {
        [Tooltip("名前（赤・青・緑・黄）")] public string colorName = "";
        [Tooltip("記号（●▲■★）。色の見分けにくい人のため、練習用お宝の上にも出す。枠どうしで重ならない")] public string symbol = "";
        [Tooltip("ビームの色")] public Color beamColor = Color.white;
        [Tooltip("アバター（頭・手・体）の色。ビームと同じ系統")] public Color avatarColor = Color.white;
        [Tooltip("練習用お宝の色")] public Color practiceColor = Color.white;
        [Tooltip("アバターのテクスチャの差し替え（今のシェーダー TIB/AvatarTint では使わない。空のままでよい）")] public Texture avatarTexture;
    }

    /// <summary>
    /// プレーヤーの色の一覧（設計書 3.4）。4 枠（色番号 0〜3 = 赤●・青▲・緑■・黄★）と、色が無いときの既定の色。
    /// 見た目は R.E.P.O に似せない。後で調整してよい。
    /// </summary>
    [CreateAssetMenu(fileName = "ThPlayerPalette", menuName = "Treasure Haul/Player Palette")]
    public sealed class ThPlayerPalette : ScriptableObject
    {
        public const int SlotCount = ThPractice.MaxColors;

        public List<ThPlayerColorSlot> slots = DefaultSlots();
        [Tooltip("色の割り当てが無いときのビームの色（今のビームの見た目 = TH_HandleAimed と同じ）")] public Color defaultBeamColor = new Color(0.2f, 0.95f, 1f, 1f);
        [Tooltip("色の割り当てが無いときのアバターの色（実際には塗りを外す。他の色が決まるまでの目安）")] public Color defaultAvatarColor = Color.white;

        /// <summary>初期値（赤 (1.0, 0.30, 0.30)・青 (0.30, 0.55, 1.0)・緑 (0.30, 0.85, 0.40)・黄 (1.0, 0.85, 0.20)）</summary>
        public static List<ThPlayerColorSlot> DefaultSlots() => new List<ThPlayerColorSlot>
        {
            Slot("赤", "●", 1.0f, 0.30f, 0.30f), Slot("青", "▲", 0.30f, 0.55f, 1.0f), Slot("緑", "■", 0.30f, 0.85f, 0.40f), Slot("黄", "★", 1.0f, 0.85f, 0.20f),
        };

        static ThPlayerColorSlot Slot(string name, string symbol, float r, float g, float b)
        {
            var c = new Color(r, g, b, 1f);
            return new ThPlayerColorSlot { colorName = name, symbol = symbol, beamColor = c, avatarColor = c, practiceColor = c };
        }

        /// <summary>色番号の枠（範囲外・空は null）</summary>
        public ThPlayerColorSlot Get(int color) => slots != null && color >= 0 && color < slots.Count ? slots[color] : null;

        /// <summary>ビームの色（色が無ければ既定）</summary>
        public Color BeamColor(int color) => Get(color)?.beamColor ?? defaultBeamColor;

        /// <summary>問題の一覧（空なら問題なし）: 枠が 4 つある・記号が空でなく重複しない</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            if (slots == null || slots.Count != SlotCount) { errors.Add($"色の枠が {SlotCount} つありません（{slots?.Count ?? 0} 個）"); return errors; }
            var seen = new HashSet<string>();
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s == null) { errors.Add($"空の枠があります（{i} 番目）"); continue; }
                if (string.IsNullOrWhiteSpace(s.symbol)) errors.Add($"記号が空です（{i} 番目）");
                else if (!seen.Add(s.symbol)) errors.Add($"記号が重複しています: {s.symbol}");
            }
            return errors;
        }
    }
}
