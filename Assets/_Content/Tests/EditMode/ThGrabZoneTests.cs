using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul.Tests
{
    /// <summary>つかむ範囲（GrabZone）の自動生成・ビームの対象選び・高い壁（ThWall）でビームが止まること</summary>
    public class ThGrabZoneTests
    {
        const float Eps = 1e-4f;
        readonly List<GameObject> _made = new List<GameObject>();

        [TearDown] public void TearDown()
        {
            foreach (var g in _made) if (g != null) Object.DestroyImmediate(g);
            _made.Clear();
        }

        static bool Covers(Bounds outer, Bounds inner) =>
            outer.min.x <= inner.min.x + Eps && outer.min.y <= inner.min.y + Eps && outer.min.z <= inner.min.z + Eps
            && outer.max.x >= inner.max.x - Eps && outer.max.y >= inner.max.y - Eps && outer.max.z >= inner.max.z - Eps;

        // ---- GrabZoneLayout ----

        [Test] public void Sはモデル全体を覆う箱が1つできる()
        {
            var model = new Bounds(new Vector3(0, 0.1f, 0), new Vector3(0.12f, 0.2f, 0.12f));
            var zones = GrabZoneLayout.Compute(model, TreasureKind.S, 0.02f);
            Assert.AreEqual(1, zones.Length);
            Assert.IsTrue(Covers(zones[0], model));
            Assert.AreEqual(0.12f + 0.04f, zones[0].size.x, Eps);   // 各辺に余白 0.02m
            Assert.AreEqual(0.2f + 0.04f, zones[0].size.y, Eps);
            Assert.AreEqual(model.center.y, zones[0].center.y, Eps);
        }

        [Test] public void Mは水平の長いXの軸で左右2つに分かれ合わせるとモデルを覆う()
        {
            var model = new Bounds(new Vector3(0, 0.2f, 0), new Vector3(0.6f, 0.4f, 0.3f));
            var zones = GrabZoneLayout.Compute(model, TreasureKind.M, 0.02f);
            Assert.AreEqual(2, zones.Length);
            Assert.Less(zones[0].center.x, zones[1].center.x, "負側が 0 番");
            Assert.AreEqual(zones[0].center.z, zones[1].center.z, Eps);
            var both = zones[0]; both.Encapsulate(zones[1]);
            Assert.IsTrue(Covers(both, model));
            Assert.IsTrue(Covers(zones[0], new Bounds(new Vector3(-0.15f, 0.2f, 0), new Vector3(0.3f, 0.4f, 0.3f))));
            Assert.IsTrue(Covers(zones[1], new Bounds(new Vector3(0.15f, 0.2f, 0), new Vector3(0.3f, 0.4f, 0.3f))));
        }

        [Test] public void MはZの方が長いモデルではZで分かれる()
        {
            var model = new Bounds(new Vector3(0, 0.2f, 0), new Vector3(0.3f, 0.4f, 0.6f));
            var zones = GrabZoneLayout.Compute(model, TreasureKind.M, 0.02f);
            Assert.AreEqual(2, zones.Length);
            Assert.Less(zones[0].center.z, zones[1].center.z);
            Assert.AreEqual(zones[0].center.x, zones[1].center.x, Eps);
            var both = zones[0]; both.Encapsulate(zones[1]);
            Assert.IsTrue(Covers(both, model));
        }

        [Test] public void MはXとZが同じならXで分かれる()
        {
            var model = new Bounds(Vector3.zero, new Vector3(0.4f, 0.4f, 0.4f));
            Assert.IsTrue(GrabZoneLayout.SplitsAlongX(model));
            var zones = GrabZoneLayout.Compute(model, TreasureKind.M, 0.02f);
            Assert.Less(zones[0].center.x, zones[1].center.x);
        }

        [Test] public void つかむ範囲の数はMだけ2つ()
        {
            Assert.AreEqual(1, GrabZoneLayout.CountFor(TreasureKind.S));
            Assert.AreEqual(2, GrabZoneLayout.CountFor(TreasureKind.M));
            Assert.AreEqual(1, GrabZoneLayout.CountFor(TreasureKind.L));
        }

        // ---- GrabTargetPicker ----

        static GrabCandidate[] C(params (float d, bool ok)[] xs) => xs.Select(x => new GrabCandidate(x.d, x.ok)).ToArray();

        [Test] public void 有効で一番近いものが選ばれる()
        {
            var c = C((1.2f, true), (0.6f, true), (0.3f, false));
            Assert.AreEqual(1, GrabTargetPicker.Pick(c, c.Length));
        }

        [Test] public void 予約済みや壁に遮られた無効な範囲は選ばれない()
        {
            var c = C((0.4f, false), (0.9f, true));
            Assert.AreEqual(1, GrabTargetPicker.Pick(c, c.Length));
            var none = C((0.4f, false));
            Assert.AreEqual(-1, GrabTargetPicker.Pick(none, none.Length));
        }

        [Test] public void ビームの長さは壁で止まる()
        {
            Assert.AreEqual(1.5f, GrabTargetPicker.BeamLength(1.5f, GrabTargetPicker.NoWall), Eps);
            Assert.AreEqual(0.7f, GrabTargetPicker.BeamLength(1.5f, 0.7f), Eps);
            Assert.AreEqual(1.5f, GrabTargetPicker.BeamLength(1.5f, 3f), Eps);
        }

        // ---- Treasure.Initialize（持ち手の無いプレハブ相当） ----

        Treasure MakeTreasure(TreasureKind kind, Vector3 size, Material outline)
        {
            var root = new GameObject("T_" + kind);
            _made.Add(root);
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);   // MeshFilter + MeshRenderer + BoxCollider だけの見た目
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0, size.y * 0.5f, 0);
            body.transform.localScale = size;
            var t = root.AddComponent<Treasure>();   // Rigidbody は RequireComponent で付く
            t.Initialize(new ClientItem { Kind = kind }, null, outline, LayerMask.NameToLayer("Treasure"), LayerMask.NameToLayer("TreasureGrabZone"));
            return t;
        }

        [Test] public void 持ち手の無い見た目でもSは1つのつかむ範囲ができる()
        {
            var t = MakeTreasure(TreasureKind.S, new Vector3(0.12f, 0.2f, 0.12f), null);
            Assert.AreEqual(1, t.GrabZoneCount);
            AssertZone(t.GrabZones[0]);
            var zb = t.GrabZones[0].bounds;
            Assert.IsTrue(Covers(zb, new Bounds(new Vector3(0, 0.1f, 0), new Vector3(0.12f, 0.2f, 0.12f))));
        }

        [Test] public void 持ち手の無い見た目でもMは2つのつかむ範囲ができ中点が保持点になる()
        {
            var t = MakeTreasure(TreasureKind.M, new Vector3(0.4f, 0.44f, 0.4f), null);
            Assert.AreEqual(2, t.GrabZoneCount);
            AssertZone(t.GrabZones[0]);
            AssertZone(t.GrabZones[1]);
            Assert.Less(t.GrabZoneLocalPosition(0).x, t.GrabZoneLocalPosition(1).x);
            Assert.AreEqual(0f, t.HoldPoint.x, Eps);
            Assert.AreEqual(0.22f, t.HoldPoint.y, Eps);
        }

        static void AssertZone(Collider c)
        {
            Assert.IsTrue(c.isTrigger);
            Assert.AreEqual("TreasureGrabZone", LayerMask.LayerToName(c.gameObject.layer));
        }

        [Test] public void 輪郭はビームが当たっている間だけ有効になり影を落とさない()
        {
            var shader = Shader.Find("TIB/TreasureHaul/AimOutline");
            Assert.IsNotNull(shader);
            var mat = new Material(shader);
            try
            {
                var t = MakeTreasure(TreasureKind.S, new Vector3(0.12f, 0.2f, 0.12f), mat);
                var outlines = t.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name == "AimOutline").ToArray();
                Assert.AreEqual(1, outlines.Length);
                Assert.IsFalse(outlines[0].enabled);
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, outlines[0].shadowCastingMode);
                Assert.IsFalse(outlines[0].receiveShadows);
                Assert.AreSame(mat, outlines[0].sharedMaterial);
                t.SetAimed(true);
                Assert.IsTrue(outlines[0].enabled);
                t.SetAimed(false);
                Assert.IsFalse(outlines[0].enabled);
                t.SetAimed(true);
                t.SetGrabZonesActive(false);
                Assert.IsFalse(outlines[0].enabled, "つかめなくなったら光らない");
                Assert.IsFalse(t.GrabZones[0].enabled);
            }
            finally { Object.DestroyImmediate(mat); }
        }

        // ---- 壁（ThWall）でビームが止まる（物理込み） ----

        [Test] public void 壁を挟むとビームの対象が無くなり壁が無ければ選ばれる()
        {
            int wallLayer = LayerMask.NameToLayer("ThWall");
            int zoneLayer = LayerMask.NameToLayer("TreasureGrabZone");
            Assert.GreaterOrEqual(wallLayer, 0, "ThWall レイヤーがありません");
            Assert.GreaterOrEqual(zoneLayer, 0);
            var t = MakeTreasure(TreasureKind.S, new Vector3(0.12f, 0.2f, 0.12f), null);
            t.transform.position = new Vector3(0f, 0f, 1.0f);
            Physics.SyncTransforms();
            var hits = new RaycastHit[16];
            var cands = new GrabCandidate[16];
            var origin = new Vector3(0f, 0.1f, 0f);
            int zoneMask = 1 << zoneLayer, wallMask = 1 << wallLayer;

            // 壁が無い
            float wd = HandGrabber.WallDistance(origin, Vector3.forward, 1.5f, wallMask);
            Assert.IsTrue(float.IsPositiveInfinity(wd));
            var found = HandGrabber.FindTarget(origin, Vector3.forward, 1.5f, 0.05f, zoneMask, wallMask, hits, cands, (x, z) => true, out int zone);
            Assert.AreSame(t, found);
            Assert.AreEqual(0, zone);

            // 壁を挟む
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _made.Add(wall);
            wall.layer = wallLayer;
            wall.transform.position = new Vector3(0f, 1f, 0.5f);
            wall.transform.localScale = new Vector3(2f, 2.4f, 0.1f);
            Physics.SyncTransforms();
            wd = HandGrabber.WallDistance(origin, Vector3.forward, 1.5f, wallMask);
            Assert.AreEqual(0.45f, wd, 1e-3f, "壁の手前の面までの距離");
            Assert.IsNull(HandGrabber.FindTarget(origin, Vector3.forward, 1.5f, 0.05f, zoneMask, wallMask, hits, cands, (x, z) => true, out zone));
            Assert.AreEqual(-1, zone);
            Assert.AreEqual(0.45f, GrabTargetPicker.BeamLength(1.5f, wd), 1e-3f);

            // 壁より手前のお宝はつかめる
            t.transform.position = new Vector3(0f, 0f, 0.2f);
            Physics.SyncTransforms();
            Assert.AreSame(t, HandGrabber.FindTarget(origin, Vector3.forward, 1.5f, 0.05f, zoneMask, wallMask, hits, cands, (x, z) => true, out zone));

            // 壁以外のレイヤー（低い瓦礫）はビームを止めない
            wall.layer = 0;
            Physics.SyncTransforms();
            wd = HandGrabber.WallDistance(origin, Vector3.forward, 1.5f, wallMask);
            Assert.IsTrue(float.IsPositiveInfinity(wd));
            t.transform.position = new Vector3(0f, 0f, 1.0f);
            Physics.SyncTransforms();
            Assert.AreSame(t, HandGrabber.FindTarget(origin, Vector3.forward, 1.5f, 0.05f, zoneMask, wallMask, hits, cands, (x, z) => true, out zone));
        }

        static GameObject MakeWall(int layer, Vector3 pos, Vector3 scale)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.layer = layer;
            wall.transform.position = pos;
            wall.transform.localScale = scale;
            return wall;
        }

        [Test] public void 薄い壁の裏に接したお宝は選ばれず手前に接したお宝は選ばれる()
        {
            int wallLayer = LayerMask.NameToLayer("ThWall");
            int zoneLayer = LayerMask.NameToLayer("TreasureGrabZone");
            var t = MakeTreasure(TreasureKind.S, new Vector3(0.12f, 0.2f, 0.12f), null);
            var wall = MakeWall(wallLayer, new Vector3(0f, 1f, 0.5f), new Vector3(2f, 2.4f, 0.05f));   // z 0.475〜0.525
            _made.Add(wall);
            var hits = new RaycastHit[16];
            var cands = new GrabCandidate[16];
            var origin = new Vector3(0f, 0.1f, 0f);
            int zoneMask = 1 << zoneLayer, wallMask = 1 << wallLayer;

            // つかむ範囲の手前の面が壁の裏面にちょうど接する（範囲の奥行きは 0.12 + 余白 0.04 = 0.16）
            t.transform.position = new Vector3(0f, 0f, 0.525f + 0.08f);
            Physics.SyncTransforms();
            Assert.IsNull(HandGrabber.FindTarget(origin, Vector3.forward, 1.5f, 0.05f, zoneMask, wallMask, hits, cands, (x, z) => true, out int zone));

            // つかむ範囲の奥の面が壁の手前面に接する
            t.transform.position = new Vector3(0f, 0f, 0.475f - 0.08f);
            Physics.SyncTransforms();
            Assert.AreSame(t, HandGrabber.FindTarget(origin, Vector3.forward, 1.5f, 0.05f, zoneMask, wallMask, hits, cands, (x, z) => true, out zone));
        }

        [Test] public void 手のひらが壁の中や壁の向こうならビームは使えない()
        {
            int wallLayer = LayerMask.NameToLayer("ThWall");
            int wallMask = 1 << wallLayer;
            var wall = MakeWall(wallLayer, new Vector3(0f, 1f, 0.5f), new Vector3(2f, 2.4f, 0.1f));   // z 0.45〜0.55
            _made.Add(wall);
            Physics.SyncTransforms();
            var shoulder = new Vector3(0f, 1f, 0f);
            Assert.IsTrue(HandGrabber.IsHandBehindWall(shoulder, new Vector3(0f, 1f, 0.5f), wallMask), "手のひらが壁の箱の中");
            Assert.IsTrue(HandGrabber.IsHandBehindWall(shoulder, new Vector3(0f, 1f, 0.9f), wallMask), "壁の向こうに腕を突き出した");
            Assert.IsFalse(HandGrabber.IsHandBehindWall(shoulder, new Vector3(0f, 1f, 0.3f), wallMask), "壁の手前");
            Assert.IsFalse(HandGrabber.IsHandBehindWall(shoulder, new Vector3(0f, 1f, 0.9f), 0), "壁のマスクなし");
            // 壁の中に入ってしまっている手: 壁を通り抜けた側の肩 → 手のひらも、手が壁の外へ出ていれば使える
            Assert.IsFalse(HandGrabber.IsHandBehindWall(new Vector3(0f, 1f, 0.9f), new Vector3(0f, 1f, 1.3f), wallMask), "壁の向こう側同士");
        }

        // ---- カタログ ----

        [Test] public void カタログの検証はMeshRendererの有無を見る()
        {
            var empty = new GameObject("NoMesh");
            _made.Add(empty);
            empty.AddComponent<Treasure>();
            var c = ScriptableObject.CreateInstance<ThTreasureCatalog>();
            try
            {
                c.entries = new List<ThTreasureEntry> { new ThTreasureEntry { id = "x", kind = TreasureKind.S, prefab = empty, use = true } };
                Assert.IsTrue(c.Validate().Exists(e => e.Contains("MeshRenderer")));
                var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.transform.SetParent(empty.transform, false);
                Assert.IsFalse(c.Validate().Exists(e => e.Contains("MeshRenderer")));
            }
            finally { Object.DestroyImmediate(c); }
        }

        [Test] public void 組み込みのプレハブに持ち手の子は残っていない()
        {
            var cat = UnityEditor.AssetDatabase.LoadAssetAtPath<ThTreasureCatalog>("Assets/_Content/TreasureHaul/Settings/ThTreasureCatalog.asset");
            Assert.IsNotNull(cat);
            var prefabs = cat.entries.Select(e => e.prefab).Append(cat.practicePrefab).Where(p => p != null).ToList();
            Assert.GreaterOrEqual(prefabs.Count, 4);
            foreach (var p in prefabs)
            {
                foreach (var tr in p.GetComponentsInChildren<Transform>(true))
                    Assert.IsFalse(tr.name.StartsWith("Handle"), p.name + "/" + tr.name);
                Assert.IsNotNull(p.GetComponentInChildren<MeshRenderer>(true), p.name);
                Assert.AreEqual(0, p.GetComponent<Treasure>().GrabZoneCount, "つかむ範囲は実行時に作る: " + p.name);
            }
        }
    }
}
