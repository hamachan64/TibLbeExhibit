using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 練習用お宝の置き場所（Environment の VenueRoot の下に 4 つ。納品場所の前に横一列）。色番号（0〜3）と同じ番号の枠が、その色の人の練習用お宝の位置・向きになる。
    /// Gizmo に色と記号を出す（色は <see cref="ThPlayerPalette"/> の初期値に合わせた目安）。
    /// </summary>
    public sealed class ThTutorialSlot : MonoBehaviour
    {
        [SerializeField, Range(0, ThPractice.MaxColors - 1), Tooltip("色番号（0=赤● 1=青▲ 2=緑■ 3=黄★）")] int _colorIndex;

        public int ColorIndex { get => _colorIndex; set => _colorIndex = Mathf.Clamp(value, 0, ThPractice.MaxColors - 1); }

        static readonly Color[] GizmoColors =
        {
            new Color(1.0f, 0.30f, 0.30f), new Color(0.30f, 0.55f, 1.0f), new Color(0.30f, 0.85f, 0.40f), new Color(1.0f, 0.85f, 0.20f),
        };
        static readonly string[] Symbols = { "●", "▲", "■", "★" };

        void OnDrawGizmos()
        {
            int i = Mathf.Clamp(_colorIndex, 0, ThPractice.MaxColors - 1);
            Gizmos.color = GizmoColors[i];
            var p = transform.position + Vector3.up * 0.05f;
            Gizmos.DrawWireCube(p, new Vector3(0.3f, 0.1f, 0.3f));
            Gizmos.DrawLine(p, p + transform.forward * 0.25f);   // 向き（納品場所の方）
#if UNITY_EDITOR
            UnityEditor.Handles.Label(p + Vector3.up * 0.15f, Symbols[i]);
#endif
        }
    }
}
