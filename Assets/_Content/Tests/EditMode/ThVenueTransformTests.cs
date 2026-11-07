using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using STYLY.TIB_Template.TreasureHaul.Editor;

namespace STYLY.TIB_Template.TreasureHaul.Tests
{
    /// <summary>VenueRoot を Y 回転・平行移動しても ThVenue が返すローカル値は変わらないこと、Environment の VenueRoot が会場図面の位置にあること（#48）</summary>
    public class ThVenueTransformTests
    {
        // Inter BEE 会場の図面に合わせた VenueRoot の位置・回転（誤って戻るのを防ぐ）
        static readonly Vector3 ExpectedVenuePosition = new Vector3(11.45f, 0f, 2.4f);
        const float ExpectedVenueYaw = -90f;

        /// <summary>その場で作る会場（置き場所 2・回収範囲・練習の枠 4）。Environment シーンには依存しない</summary>
        sealed class VenueRig : System.IDisposable
        {
            public readonly GameObject RootObject;
            public readonly ThVenue Venue;

            public VenueRig()
            {
                RootObject = new GameObject("TestVenueRoot") { hideFlags = HideFlags.HideAndDontSave };
                Venue = RootObject.AddComponent<ThVenue>();
                var so = new SerializedObject(Venue);

                var points = so.FindProperty("_points");
                points.arraySize = 2;
                var local = new[] { new Vector3(1.5f, 0.8f, 6f), new Vector3(-1.2f, 0.4f, 9f) };
                for (int i = 0; i < 2; i++)
                {
                    var go = Child("P" + i, local[i]);
                    go.transform.localRotation = Quaternion.Euler(0f, 30f * (i + 1), 0f);
                    var p = go.AddComponent<ThPlacementPoint>();
                    p.YawDegrees = 30f * (i + 1);
                    p.Zone = "hall";
                    points.GetArrayElementAtIndex(i).objectReferenceValue = p;
                }

                var zone = Child("CollectZone", new Vector3(0f, 0.49f, 1.2f)).AddComponent<BoxCollider>();
                zone.size = new Vector3(2.3f, 0.92f, 1.56f);
                zone.isTrigger = true;
                so.FindProperty("_collectZone").objectReferenceValue = zone;

                var slots = so.FindProperty("_tutorialSlots");
                slots.arraySize = 4;
                for (int i = 0; i < 4; i++)
                {
                    var go = Child("Slot" + i, new Vector3(-1.2f + 0.8f * i, 0f, 2.5f));
                    go.transform.localRotation = Quaternion.identity;
                    go.AddComponent<ThTutorialSlot>().ColorIndex = i;
                    slots.GetArrayElementAtIndex(i).objectReferenceValue = go.GetComponent<ThTutorialSlot>();
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            GameObject Child(string name, Vector3 localPosition)
            {
                var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
                go.transform.SetParent(RootObject.transform, false);
                go.transform.localPosition = localPosition;
                return go;
            }

            public void Place(Vector3 position, float yaw)
            {
                RootObject.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
                Physics.SyncTransforms();
            }

            public void Dispose() => Object.DestroyImmediate(RootObject);
        }

        sealed class Snapshot
        {
            public Bounds Bounds;
            public List<PlacementPoint> Points = new List<PlacementPoint>();
            public List<PracticeSlot> Slots = new List<PracticeSlot>();

            public static Snapshot Take(ThVenue v)
            {
                var s = new Snapshot { Bounds = v.GetCollectBounds() };
                s.Points.AddRange(v.GetPlacementPoints());
                s.Slots.AddRange(v.GetPracticeSlots());
                return s;
            }
        }

        static void AssertSame(Snapshot a, Snapshot b, string label)
        {
            Assert.AreEqual(0f, Vector3.Distance(a.Bounds.center, b.Bounds.center), 0.001f, label + " 回収範囲の中心");
            Assert.AreEqual(0f, Vector3.Distance(a.Bounds.size, b.Bounds.size), 0.001f, label + " 回収範囲の大きさ");
            Assert.AreEqual(a.Points.Count, b.Points.Count, label);
            for (int i = 0; i < a.Points.Count; i++)
            {
                Assert.AreEqual(0f, Vector3.Distance(a.Points[i].LocalPosition, b.Points[i].LocalPosition), 0.001f, $"{label} 置き場所 {i}");
                Assert.AreEqual(a.Points[i].YawDegrees, b.Points[i].YawDegrees, 0.001f, $"{label} 置き場所 {i} の向き");
            }
            Assert.AreEqual(4, a.Slots.Count, label);
            Assert.AreEqual(4, b.Slots.Count, label);
            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(0f, Vector3.Distance(a.Slots[i].LocalPosition, b.Slots[i].LocalPosition), 0.001f, $"{label} 練習の枠 {i}");
                Assert.AreEqual(0f, Mathf.DeltaAngle(a.Slots[i].YawDegrees, b.Slots[i].YawDegrees), 0.01f, $"{label} 練習の枠 {i} の向き");
            }
        }

        [Test]
        public void VenueRootをY回転と平行移動してもローカルの値は変わらない()
        {
            using (var rig = new VenueRig())
            {
                rig.Place(Vector3.zero, 0f);
                var baseline = Snapshot.Take(rig.Venue);
                Assert.AreEqual(2, baseline.Points.Count);
                Assert.AreEqual(new Vector3(1.5f, 0.8f, 6f).x, baseline.Points[0].LocalPosition.x, 0.001f);

                foreach (var (pos, yaw) in new[]
                {
                    (ExpectedVenuePosition, ExpectedVenueYaw),
                    (new Vector3(-3f, 0f, 7f), 90f),
                    (new Vector3(4f, 0.5f, -2f), 180f),
                    (new Vector3(0.3f, 0f, 0.9f), 37f),
                })
                {
                    rig.Place(pos, yaw);
                    AssertSame(baseline, Snapshot.Take(rig.Venue), $"pos={pos} yaw={yaw}");
                }
            }
        }

        [Test]
        public void 練習の枠の高さはVenueRootのyに依らずローカルで0になる()
        {
            using (var rig = new VenueRig())
            {
                rig.Place(new Vector3(1f, 0.7f, 2f), -90f);
                foreach (var s in rig.Venue.GetPracticeSlots()) Assert.AreEqual(0f, s.LocalPosition.y, 0.0001f);
            }
        }

        [Test]
        public void 会場位置でも置き場所のローカル値は完全一致しハッシュは原点のときと同じ()
        {
            // .5mm ちょうどの値（stage_layout.json にある）を、恒等の中間の親の下に置く
            var locals = new[] { new Vector3(-2.3725f, 0.8f, 6.0075f), new Vector3(1.0f, 0.72f, 6.0075f), new Vector3(-2.3725f, 0.4f, 3.65f) };
            var rootObj = new GameObject("HashVenueRoot") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var venue = rootObj.AddComponent<ThVenue>();
                var mid = new GameObject("Mid") { hideFlags = HideFlags.HideAndDontSave };
                mid.transform.SetParent(rootObj.transform, false);
                var so = new SerializedObject(venue);
                var points = so.FindProperty("_points");
                points.arraySize = locals.Length;
                for (int i = 0; i < locals.Length; i++)
                {
                    var go = new GameObject("P" + i) { hideFlags = HideFlags.HideAndDontSave };
                    go.transform.SetParent(mid.transform, false);
                    go.transform.localPosition = locals[i];
                    points.GetArrayElementAtIndex(i).objectReferenceValue = go.AddComponent<ThPlacementPoint>();
                }
                so.ApplyModifiedPropertiesWithoutUndo();

                rootObj.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                string atOrigin = ThTreasureSetup.ComputeHash("", venue.GetPlacementPoints());
                rootObj.transform.SetPositionAndRotation(ExpectedVenuePosition, Quaternion.Euler(0f, ExpectedVenueYaw, 0f));
                var moved = venue.GetPlacementPoints();
                for (int i = 0; i < locals.Length; i++) Assert.IsTrue(locals[i] == moved[i].LocalPosition, $"置き場所 {i}: {moved[i].LocalPosition:F7}");
                string atVenue = ThTreasureSetup.ComputeHash("", moved);
                Debug.Log($"[ThVenueTransformTests] ハッシュ 原点={atOrigin} 会場位置={atVenue}");
                Assert.AreEqual(atOrigin, atVenue);
            }
            finally { Object.DestroyImmediate(rootObj); }
        }

        [Test]
        public void Environmentシーンの照合ハッシュはVenueRootを動かしても変わらない()
        {
            var existing = SceneManager.GetSceneByPath(ThStageBuilder.EnvironmentScenePath);
            bool opened = !existing.isLoaded;
            var scene = existing.isLoaded ? existing : EditorSceneManager.OpenScene(ThStageBuilder.EnvironmentScenePath, OpenSceneMode.Additive);
            Transform t = null; Vector3 pos = default; Quaternion rot = default;
            try
            {
                ThVenue venue = null;
                foreach (var go in scene.GetRootGameObjects()) { venue = go.GetComponentInChildren<ThVenue>(true); if (venue != null) break; }
                Assert.IsNotNull(venue);
                t = venue.Root; pos = t.position; rot = t.rotation;
                string atVenue = venue.CreateSetup(null).CatalogHash;
                t.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                string atOrigin = venue.CreateSetup(null).CatalogHash;
                Debug.Log($"[ThVenueTransformTests] シーンのハッシュ 原点={atOrigin} 会場位置={atVenue}");
                Assert.AreEqual(atOrigin, atVenue);
            }
            finally
            {
                if (t != null) t.SetPositionAndRotation(pos, rot);
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void Environmentシーンの会場は図面に合わせた位置と向きにある()
        {
            var existing = SceneManager.GetSceneByPath(ThStageBuilder.EnvironmentScenePath);
            bool opened = !existing.isLoaded;
            var scene = existing.isLoaded ? existing : EditorSceneManager.OpenScene(ThStageBuilder.EnvironmentScenePath, OpenSceneMode.Additive);
            try
            {
                ThVenue venue = null;
                foreach (var go in scene.GetRootGameObjects()) { venue = go.GetComponentInChildren<ThVenue>(true); if (venue != null) break; }
                Assert.IsNotNull(venue);
                var t = venue.Root;
                Assert.AreEqual(0f, Vector3.Distance(ExpectedVenuePosition, t.position), 0.001f, "VenueRoot の位置");
                Assert.AreEqual(0f, Mathf.DeltaAngle(ExpectedVenueYaw, t.eulerAngles.y), 0.01f, "VenueRoot の Y 回転");
                Assert.AreEqual(0f, Mathf.DeltaAngle(0f, t.eulerAngles.x), 0.01f);
                Assert.AreEqual(0f, Mathf.DeltaAngle(0f, t.eulerAngles.z), 0.01f);
                Assert.AreEqual(0f, Vector3.Distance(Vector3.one, t.lossyScale), 0.0001f, "VenueRoot の拡縮");
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
