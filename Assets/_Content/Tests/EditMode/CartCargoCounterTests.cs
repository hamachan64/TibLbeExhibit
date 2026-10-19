using NUnit.Framework;
using UnityEngine;
using STYLY.TIB_Template.TreasureHaul;

public sealed class CartCargoCounterTests
{
    private GameObject cart, first, second;
    private CartCargoCounter counter;
    private CargoValuable a, b;
    private void Refresh() => counter.RefreshFromItems(new[] { a, b });

    [SetUp] public void SetUp()
    {
        cart = new GameObject("Cart test");
        var volume = cart.AddComponent<BoxCollider>();
        volume.isTrigger = true; volume.size = Vector3.one;
        counter = cart.AddComponent<CartCargoCounter>();
        counter.SetVolume(volume);
        first = new GameObject("A"); a = first.AddComponent<CargoValuable>(); a.SetCurrentValue(1000);
        second = new GameObject("B"); b = second.AddComponent<CargoValuable>(); b.SetCurrentValue(2600);
    }
    [TearDown] public void TearDown()
    {
        Object.DestroyImmediate(first); Object.DestroyImmediate(second); Object.DestroyImmediate(cart);
    }
    [Test] public void ContentsTrackInsertionRemovalAndDamage()
    {
        Refresh(); Assert.That(counter.TotalValue.CurrentValue, Is.EqualTo(3600));
        a.SetCurrentValue(700); Refresh(); Assert.That(counter.TotalValue.CurrentValue, Is.EqualTo(3300));
        second.transform.position = Vector3.right * 2;
        Refresh(); Assert.That(counter.TotalValue.CurrentValue, Is.EqualTo(700));
        second.transform.position = Vector3.zero;
        Refresh(); Assert.That(counter.TotalValue.CurrentValue, Is.EqualTo(3300));
    }
    [Test] public void MultipleCollidersDoNotDoubleCountAndHeldItemsAreExcluded()
    {
        first.AddComponent<BoxCollider>(); first.AddComponent<SphereCollider>();
        b.SetHeld(true); Refresh(); Assert.That(counter.TotalValue.CurrentValue, Is.EqualTo(1000));
        b.SetHeld(false); Refresh(); Assert.That(counter.TotalValue.CurrentValue, Is.EqualTo(3600));
    }
    [Test] public void DisabledDestroyedAndIneligibleItemsAreRemoved()
    {
        a.SetEligible(false); second.SetActive(false); Refresh();
        Assert.That(counter.TotalValue.CurrentValue, Is.Zero);
        second.SetActive(true); Object.DestroyImmediate(second); second = null;
        Refresh(); Assert.That(counter.TotalValue.CurrentValue, Is.Zero);
    }
    [Test] public void RotatedScaledCartUsesLocalVolume()
    {
        cart.transform.SetPositionAndRotation(new Vector3(5, 2, 3), Quaternion.Euler(0, 45, 0));
        cart.transform.localScale = new Vector3(2, 1, 3);
        first.transform.position = cart.transform.TransformPoint(new Vector3(.4f, 0, 0));
        second.transform.position = cart.transform.TransformPoint(new Vector3(.6f, 0, 0));
        Refresh(); Assert.That(counter.TotalValue.CurrentValue, Is.EqualTo(1000));
    }
    [Test] public void ValuesAreNonNegativeAndSumDoesNotOverflowInt()
    {
        a.SetCurrentValue(-1); b.SetCurrentValue(0); Refresh();
        Assert.That(counter.TotalValue.CurrentValue, Is.Zero);
        a.SetCurrentValue(int.MaxValue); b.SetCurrentValue(int.MaxValue); Refresh();
        Assert.That(counter.TotalValue.CurrentValue, Is.EqualTo(4294967294L));
        Assert.That(CartCargoCounter.FormatTotal(3600), Is.EqualTo("3,600 G"));
    }
}
