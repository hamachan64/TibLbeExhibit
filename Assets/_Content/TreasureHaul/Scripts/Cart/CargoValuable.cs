using System.Collections.Generic;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>Adapter for the authoritative item's current value and held state.</summary>
    public sealed class CargoValuable : MonoBehaviour
    {
        private static readonly HashSet<CargoValuable> Active = new();
        internal static IEnumerable<CargoValuable> ActiveItems => Active;
        [SerializeField, Min(0)] private int currentValue;
        [SerializeField] private Transform cargoPoint;
        [SerializeField] private bool isHeld;
        [SerializeField] private bool isEligible = true;
        public int CurrentValue => Mathf.Max(0, currentValue);
        public Vector3 CargoPosition => cargoPoint != null ? cargoPoint.position : transform.position;
        public bool CountsAsCargo => isActiveAndEnabled && isEligible && !isHeld;
        public void SetCurrentValue(int value) => currentValue = Mathf.Max(0, value);
        public void SetHeld(bool held) => isHeld = held;
        public void SetEligible(bool eligible) => isEligible = eligible;
        private void OnEnable() => Active.Add(this);
        private void OnDisable() => Active.Remove(this);
        private void OnDestroy() => Active.Remove(this);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() => Active.Clear();
    }
}
