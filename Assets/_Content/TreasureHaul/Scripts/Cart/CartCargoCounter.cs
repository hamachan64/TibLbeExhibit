using System;
using System.Globalization;
using System.Collections.Generic;
using R3;
using TMPro;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>Current contents, not extracted/session score. No trigger enter/exit bookkeeping.</summary>
    public sealed class CartCargoCounter : MonoBehaviour
    {
        [SerializeField] private BoxCollider cargoVolume;
        [SerializeField] private TMP_Text valueDisplay;
        private readonly ReactiveProperty<long> totalValue = new(0);
        private IDisposable displaySubscription;
        private IDisposable refreshSubscription;
        public ReadOnlyReactiveProperty<long> TotalValue => totalValue;
        public static string FormatTotal(long value) => Math.Max(0, value).ToString("N0", CultureInfo.InvariantCulture) + " G";
        public void SetVolume(BoxCollider volume) { cargoVolume = volume; RefreshNow(); }

        public void Configure(BoxCollider volume, TMP_Text display)
        {
            cargoVolume = volume;
            valueDisplay = display;
            RefreshNow();
            UpdateDisplay(totalValue.Value);
        }

        private void OnEnable()
        {
            displaySubscription = totalValue.Subscribe(UpdateDisplay);
            refreshSubscription = Observable.Interval(TimeSpan.FromSeconds(0.1)).Subscribe(_ => RefreshNow());
            RefreshNow();
        }

        public void RefreshNow()
        {
            RefreshFromItems(CargoValuable.ActiveItems);
        }

        // Also accepts an explicit item set for host snapshots and EditMode validation.
        public void RefreshFromItems(IEnumerable<CargoValuable> items)
        {
            long sum = 0;
            if (cargoVolume != null && cargoVolume.enabled && cargoVolume.gameObject.activeInHierarchy)
            {
                var localBounds = new Bounds(cargoVolume.center, cargoVolume.size);
                foreach (var item in items)
                {
                    if (item == null || !item.CountsAsCargo) continue;
                    if (localBounds.Contains(cargoVolume.transform.InverseTransformPoint(item.CargoPosition)))
                        sum += item.CurrentValue;
                }
            }
            totalValue.Value = sum;
        }

        private void UpdateDisplay(long value)
        {
            if (valueDisplay != null) valueDisplay.text = FormatTotal(value);
        }

        private void OnDisable()
        {
            refreshSubscription?.Dispose();
            displaySubscription?.Dispose();
            totalValue.Value = 0;
            UpdateDisplay(0);
        }
        private void OnDestroy() => totalValue.Dispose();
    }
}
