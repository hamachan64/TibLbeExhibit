using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>配置候補点（Environment の VenueRoot の下に置く印）。band は荷台からの距離帯（0=近い / 1=中 / 2=遠い）</summary>
    public sealed class ThPlacementPoint : MonoBehaviour
    {
        [SerializeField, Range(0, 2)] int _band;
        public int Band { get => _band; set => _band = Mathf.Clamp(value, 0, 2); }

        void OnDrawGizmos()
        {
            Gizmos.color = _band == 0 ? Color.green : _band == 1 ? Color.yellow : Color.red;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.05f, 0.08f);
        }
    }
}
