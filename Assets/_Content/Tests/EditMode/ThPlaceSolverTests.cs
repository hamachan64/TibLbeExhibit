using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace STYLY.TIB_Template.TreasureHaul.Tests
{
    /// <summary>そっと置くときの横の押し出し（ThPlaceSolver.SolveClear）</summary>
    public class ThPlaceSolverTests
    {
        readonly List<GameObject> _made = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var g in _made) if (g != null) Object.DestroyImmediate(g);
            _made.Clear();
        }

        GameObject Box(string name, Vector3 center, Vector3 size, Transform parent = null)
        {
            var g = new GameObject(name);
            _made.Add(g);
            if (parent != null) g.transform.SetParent(parent, false);
            g.transform.position = center;
            g.AddComponent<BoxCollider>().size = size;
            return g;
        }

        /// <summary>床（y=0 が上面）と、x=0 に内面を持つ壁（x が 0〜wallThickness）</summary>
        Transform BuildVenue(float wallThickness)
        {
            var venue = new GameObject("Venue").transform;
            _made.Add(venue.gameObject);
            Box("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f), venue);
            Box("Wall", new Vector3(wallThickness * 0.5f, 1.5f, 0f), new Vector3(wallThickness, 3f, 20f), venue);
            return venue;
        }

        /// <summary>底が原点の 0.4m の箱のお宝。root は Z に 45° 傾けて、localFrom の位置に持っている</summary>
        (Transform root, Collider body) BuildTreasure(Vector3 held)
        {
            var root = new GameObject("Treasure");
            _made.Add(root);
            int layer = LayerMask.NameToLayer("Treasure");
            if (layer >= 0) root.layer = layer;
            root.transform.position = held;
            root.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
            var child = new GameObject("Body");
            child.transform.SetParent(root.transform, false);
            child.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            if (layer >= 0) child.layer = layer;
            var box = child.AddComponent<BoxCollider>();
            box.size = Vector3.one * 0.4f;
            return (root.transform, box);
        }

        static float Overlap(Transform venue, Vector3 dest, Quaternion rot, Transform root, Collider body, Collider wall)
        {
            var bt = body.transform;
            var relPos = root.InverseTransformPoint(bt.position);
            var relRot = Quaternion.Inverse(root.rotation) * bt.rotation;
            var pos = venue.TransformPoint(dest) + venue.rotation * rot * relPos;
            return Physics.ComputePenetration(body, pos, venue.rotation * rot * relRot, wall, wall.transform.position, wall.transform.rotation, out _, out var d) ? d : 0f;
        }

        [Test]
        public void 壁に接して持っていた傾いたお宝は_立て直して置いても壁と重ならない()
        {
            var venue = BuildVenue(1f);
            var held = new Vector3(-0.1f, 0.5f, 0f);   // 傾いた外形は壁に触れていないが、立てると 10cm 食い込む
            var (root, body) = BuildTreasure(held);
            Physics.SyncTransforms();
            var rot = ThPlaceSolver.Upright(root.localRotation);

            var dest = ThPlaceSolver.SolveClear(venue, root.localPosition, rot, new[] { body }, root);

            var wall = venue.Find("Wall").GetComponent<Collider>();
            Assert.Less(Overlap(venue, dest, rot, root, body, wall), 0.001f);
            Assert.Less(dest.x, -0.19f);   // 箱の半幅 0.2 の分だけ壁から離れる
            Assert.AreEqual(0f, dest.y, 1e-3f);
            Assert.AreEqual(0f, dest.z, 1e-3f);
        }

        [Test]
        public void 家具が非凸の_MeshCollider_でも_立て直して置いたお宝を押し出す()
        {
            var venue = BuildVenue(1f);
            var wall = venue.Find("Wall").gameObject;
            var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var cubeMesh = mesh.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(wall.GetComponent<BoxCollider>());
            wall.transform.localScale = new Vector3(1f, 3f, 20f);   // Box() の size の代わりに拡縮で同じ大きさにする
            var mc = wall.AddComponent<MeshCollider>();
            mc.sharedMesh = cubeMesh;
            mc.convex = false;
            var (root, body) = BuildTreasure(new Vector3(-0.1f, 0.5f, 0f));
            Physics.SyncTransforms();
            var rot = ThPlaceSolver.Upright(root.localRotation);

            var dest = ThPlaceSolver.SolveClear(venue, root.localPosition, rot, new[] { body }, root);

            Assert.Less(Overlap(venue, dest, rot, root, body, mc), 0.001f);
            Assert.Less(dest.x, -0.19f);
        }

        [Test]
        public void 重なりが無いときは_Solve_と同じ位置になる()
        {
            var venue = BuildVenue(1f);
            var (root, body) = BuildTreasure(new Vector3(-3f, 0.5f, 2f));
            Physics.SyncTransforms();
            var rot = ThPlaceSolver.Upright(root.localRotation);

            var expected = ThPlaceSolver.Solve(venue, root.localPosition);
            var actual = ThPlaceSolver.SolveClear(venue, root.localPosition, rot, new[] { body }, root);

            Assert.AreEqual(expected, actual);
        }

        [Test]
        public void 押し出しは合計_0_3m_までで_解消できなければ警告してそのまま置く()
        {
            var venue = BuildVenue(2f);   // 厚い壁
            var held = new Vector3(0.15f, 0.5f, 0f);   // 立てた外形が壁に 35cm 食い込む（上限 0.3m では抜けられない）
            var (root, body) = BuildTreasure(held);
            Physics.SyncTransforms();
            var rot = ThPlaceSolver.Upright(root.localRotation);
            LogAssert.Expect(LogType.Warning, new Regex("ThPlaceSolver"));

            var dest = ThPlaceSolver.SolveClear(venue, root.localPosition, rot, new[] { body }, root);

            var moved = new Vector2(dest.x - held.x, dest.z - held.z).magnitude;
            Assert.LessOrEqual(moved, ThPlaceSolver.MaxPush + 1e-3f);
            Assert.Greater(moved, 0.1f);   // 押し出しはしている
        }
    }
}
