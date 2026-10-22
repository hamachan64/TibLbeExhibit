using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using STYLY.TIB_Template.TreasureHaul.Editor;

namespace STYLY.TIB_Template.TreasureHaul.Tests
{
    /// <summary>お宝 12 種のカタログと、置き場所・お宝の大きさ・値段の帯に基づく配置（F-ITEM-1・2）</summary>
    public class ThPlacementTests
    {
        const string CatalogPath = "Assets/_Content/TreasureHaul/Settings/ThTreasureCatalog.asset";
        const string StageJsonPath = "Assets/_Content/TreasureHaul/Models/Stage/stage_layout.json";

        // ---- ステージの JSON（markers の role=treasure_slot）から置き場所を作る ----

        [Serializable] class StageFile { public Marker[] markers; }
        [Serializable] class Marker
        {
            public string name, role, zone;
            public float[] position_unity_xyz, facing_unity_xz, max_item_size_m;
            public string[] allowed_classes;
            public bool hidden_intent;
        }

        static List<PlacementPoint> StagePoints()
        {
            var text = AssetDatabase.LoadAssetAtPath<TextAsset>(StageJsonPath);
            Assert.IsNotNull(text, StageJsonPath);
            var file = JsonUtility.FromJson<StageFile>(text.text);
            var list = new List<PlacementPoint>();
            foreach (var m in file.markers.Where(x => x.role == "treasure_slot").OrderBy(x => x.name, StringComparer.Ordinal))
            {
                var pos = new Vector3(m.position_unity_xyz[0], m.position_unity_xyz[1], m.position_unity_xyz[2]);
                float yaw = Mathf.Atan2(m.facing_unity_xz[0], m.facing_unity_xz[1]) * Mathf.Rad2Deg;
                list.Add(new PlacementPoint(pos, ThPlacementPoint.ZoneBand(m.zone, 1),
                    m.allowed_classes.Contains("S"), m.allowed_classes.Contains("M"),
                    new Vector3(m.max_item_size_m[0], m.max_item_size_m[1], m.max_item_size_m[2]), yaw, m.zone, m.hidden_intent));
            }
            return list;
        }

        static ThTreasureCatalog Builtin() => AssetDatabase.LoadAssetAtPath<ThTreasureCatalog>(CatalogPath);

        // ---- カタログ（12 件） ----

        [Test] public void カタログは新しい12件でValidateが空()
        {
            var c = Builtin();
            CollectionAssert.IsEmpty(c.Validate(), string.Join("\n", c.Validate()));
            Assert.AreEqual(12, c.entries.Count);
            Assert.AreEqual(8, c.entries.Count(e => e.kind == TreasureKind.S));
            Assert.AreEqual(4, c.entries.Count(e => e.kind == TreasureKind.M));
            Assert.IsTrue(c.entries.All(e => e.use));
            CollectionAssert.AreEquivalent(new[]
            {
                "clay_pot", "clay_offering_bowl", "stone_beast", "stone_unguent_jar", "bronze_amulet_disc", "bronze_figure", "silver_mirror", "gold_mask",
                "clay_amphora", "stone_stele", "bronze_tripod_cauldron", "gilded_sun_disc",
            }, c.entries.Select(e => e.id).ToArray());
            Assert.IsTrue(c.entries.All(e => !string.IsNullOrEmpty(e.displayName)));
        }

        [Test] public void 値段の帯はstatsのとおりで100G刻み()
        {
            var band = new Dictionary<string, (int, int)>
            {
                ["clay_pot"] = (100, 200), ["clay_offering_bowl"] = (100, 200), ["stone_beast"] = (100, 200), ["stone_unguent_jar"] = (200, 200),
                ["bronze_amulet_disc"] = (200, 200), ["bronze_figure"] = (200, 300), ["silver_mirror"] = (300, 300), ["gold_mask"] = (300, 300),
                ["clay_amphora"] = (400, 500), ["stone_stele"] = (500, 600), ["bronze_tripod_cauldron"] = (600, 600), ["gilded_sun_disc"] = (700, 800),
            };
            foreach (var e in Builtin().entries)
            {
                Assert.AreEqual(band[e.id], (e.valueMin, e.valueMax), e.id);
                Assert.AreEqual(0, e.valueMin % 100);
                Assert.AreEqual(0, e.valueMax % 100);
            }
        }

        [Test] public void カタログの大きさはプレハブのモデルの外形と一致する()
        {
            foreach (var e in Builtin().entries)
            {
                var size = ThTreasureCatalogMenu.MeasureSize(e.prefab);
                Assert.AreEqual(size.x, e.width, 0.001f, e.id + " 幅");
                Assert.AreEqual(size.y, e.depth, 0.001f, e.id + " 奥行");
                Assert.AreEqual(size.z, e.height, 0.001f, e.id + " 高さ");
                Assert.Greater(e.width, 0.05f);
            }
        }

        [Test] public void プレハブは底が原点で正面がZプラス_Aimの空オブジェクトを持たない()
        {
            foreach (var e in Builtin().entries.Select(x => x.prefab).Append(Builtin().practicePrefab))
            {
                var mf = e.GetComponentInChildren<MeshFilter>();
                var m = e.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var lb = mf.sharedMesh.bounds;
                float minY = float.MaxValue;
                for (int c = 0; c < 8; c++)
                {
                    var corner = lb.center + Vector3.Scale(lb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    minY = Mathf.Min(minY, m.MultiplyPoint3x4(corner).y);
                }
                Assert.AreEqual(0f, minY, 0.001f, e.name + " 底が y=0");
                // Blender の正面 (-Y) がルートの +Z
                var front = m.MultiplyVector(new Vector3(0f, -1f, 0f)).normalized;
                Assert.AreEqual(1f, front.z, 1e-3f, e.name + " 正面");
                Assert.AreEqual(1, e.GetComponentsInChildren<MeshRenderer>(true).Length, e.name + " MeshRenderer は 1 つ");
                Assert.IsNotNull(e.GetComponent<Treasure>());
                Assert.IsTrue(e.GetComponentsInChildren<Transform>(true).All(t => !t.name.Contains("Aim")), e.name + " に Aim が残っている");
                Assert.AreEqual("TH_TreasureAtlas", e.GetComponentInChildren<MeshRenderer>().sharedMaterial.name);
            }
        }

        [Test] public void Mの4種はつかむ範囲が左右に分かれる()
        {
            var ms = Builtin().entries.Where(e => e.kind == TreasureKind.M).ToList();
            Assert.AreEqual(4, ms.Count);
            foreach (var e in ms)
            {
                var size = ThTreasureCatalogMenu.MeasureSize(e.prefab);
                var bounds = new Bounds(new Vector3(0f, size.z * 0.5f, 0f), new Vector3(size.x, size.z, size.y));
                Assert.IsTrue(GrabZoneLayout.SplitsAlongX(bounds), e.id);
            }
        }

        [Test] public void 練習用お宝は土の小壺のモデル()
        {
            var c = Builtin();
            var practiceMesh = c.practicePrefab.GetComponentInChildren<MeshFilter>().sharedMesh;
            var clay = c.entries.First(e => e.id == "clay_pot").prefab.GetComponentInChildren<MeshFilter>().sharedMesh;
            Assert.AreSame(clay, practiceMesh);
        }

        [Test] public void ハッシュに値段の帯と大きさが入る()
        {
            ThTreasureEntry E() => new ThTreasureEntry { id = "x", kind = TreasureKind.S, valueMin = 100, valueMax = 200, width = 0.2f, depth = 0.2f, height = 0.2f };
            string H(ThTreasureEntry e) => ThTreasureCatalog.ComputeHash(new[] { e });
            var baseHash = H(E());
            var e1 = E(); e1.valueMin = 200;
            var e2 = E(); e2.valueMax = 300;
            var e3 = E(); e3.width = 0.25f;
            var e4 = E(); e4.depth = 0.25f;
            var e5 = E(); e5.height = 0.25f;
            foreach (var other in new[] { e1, e2, e3, e4, e5 }) Assert.AreNotEqual(baseHash, H(other));
            var tiny = E(); tiny.width = 0.2f + 0.0001f;   // 1mm 未満の違いは同じ
            Assert.AreEqual(baseHash, H(tiny));
        }

        // ---- ゾーンと距離帯 ----

        [Test] public void 距離帯はゾーンから決まる()
        {
            foreach (var z in new[] { "block_front", "corridor_w", "corridor_e" }) Assert.AreEqual(0, ThPlacementPoint.ZoneBand(z, 1), z);
            foreach (var z in new[] { "front_room", "back_room", "cross" }) Assert.AreEqual(1, ThPlacementPoint.ZoneBand(z, 0), z);
            Assert.AreEqual(2, ThPlacementPoint.ZoneBand("hall", 0));
            Assert.AreEqual(2, ThPlacementPoint.ZoneBand("", 2));
        }

        [Test] public void ThPlacementPointの情報が純粋側のPlacementPointに渡る()
        {
            var go = new GameObject("p");
            try
            {
                var pt = go.AddComponent<ThPlacementPoint>();
                pt.AllowS = false; pt.AllowM = true; pt.MaxSize = new Vector3(0.4f, 0.3f, 0.9f); pt.YawDegrees = 90f; pt.Zone = "hall"; pt.Hidden = true;
                var p = pt.ToPlacementPoint(new Vector3(1f, 2f, 3f));
                Assert.AreEqual(new Vector3(1f, 2f, 3f), p.LocalPosition);
                Assert.AreEqual(2, p.Band);
                Assert.IsFalse(p.AllowS);
                Assert.IsTrue(p.AllowM);
                Assert.AreEqual(new Vector3(0.4f, 0.3f, 0.9f), p.MaxSize);
                Assert.AreEqual(90f, p.YawDegrees);
                Assert.AreEqual("hall", p.Zone);
                Assert.IsTrue(p.Hidden);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        // ---- 配置の規則（実際のステージの 34 か所と新しいカタログ） ----

        [Test] public void ステージのJSONから34か所の置き場所ができる()
        {
            var pts = StagePoints();
            Assert.AreEqual(34, pts.Count);
            Assert.AreEqual(11, pts.Count(p => p.AllowM));
            Assert.AreEqual(17, pts.Count(p => p.Hidden));
        }

        [Test] public void 全人数_シード0から999で規則1と2を守り出現数を満たす()
        {
            var pts = StagePoints();
            var catalog = Builtin().ToSpawnEntries();
            var p = new ThParams();
            int relaxed = 0;
            for (int players = 1; players <= 4; players++)
            {
                var row = p.layout.rows[players - 1];
                for (int seed = 0; seed < 1000; seed++)
                {
                    var r = TreasureSpawner.Spawn(seed, players, p, pts, catalog);
                    string ctx = $"{players}人 seed={seed}";
                    Assert.AreEqual(row.spawnS, r.Items.Count(t => t.Kind == TreasureKind.S), ctx + " S の数");
                    Assert.AreEqual(row.spawnM, r.Items.Count(t => t.Kind == TreasureKind.M), ctx + " M の数");
                    relaxed += r.Warnings.Count(w => w.Contains("決まりを外しました"));
                    Assert.IsFalse(r.Warnings.Any(w => !w.Contains("決まりを外しました")), ctx + " " + string.Join("/", r.Warnings));
                    Assert.AreEqual(r.Items.Count, r.Items.Select(t => t.PointIndex).Distinct().Count(), ctx + " 置き場所の重複");
                    foreach (var t in r.Items)
                    {
                        var pt = pts[t.PointIndex];
                        Assert.IsTrue(t.Kind == TreasureKind.M ? pt.AllowM : pt.AllowS, ctx + " 種類のフラグ");
                        var e = catalog.First(x => x.EntryIndex == t.CatalogIndex);
                        Assert.AreEqual(t.Kind, e.Kind, ctx);
                        Assert.LessOrEqual(e.Size.x, pt.MaxSize.x, ctx + " 幅 " + e.Id);
                        Assert.LessOrEqual(e.Size.y, pt.MaxSize.y, ctx + " 奥行 " + e.Id);
                        Assert.LessOrEqual(e.Size.z, pt.MaxSize.z, ctx + " 高さ " + e.Id);
                        Assert.AreEqual(pt.YawDegrees, t.YawDegrees, ctx + " 向き");
                        Assert.AreEqual(pt.LocalPosition, t.LocalPosition);
                        Assert.GreaterOrEqual(t.Value, e.ValueMin);
                        Assert.LessOrEqual(t.Value, e.ValueMax);
                        Assert.AreEqual(0, t.Value % 100);
                    }
                }
            }
            Assert.AreEqual(0, relaxed, "同じお宝を 1.5m 以内に置かない決まりを外した回数");
        }

        [Test] public void 同じお宝は1_5m未満に並ばない()
        {
            var pts = StagePoints();
            var catalog = Builtin().ToSpawnEntries();
            var p = new ThParams();
            for (int seed = 0; seed < 200; seed++)
            {
                var r = TreasureSpawner.Spawn(seed, 4, p, pts, catalog);
                for (int i = 0; i < r.Items.Count; i++)
                    for (int j = i + 1; j < r.Items.Count; j++)
                    {
                        if (r.Items[i].CatalogIndex != r.Items[j].CatalogIndex) continue;
                        var a = r.Items[i].LocalPosition; var b = r.Items[j].LocalPosition;
                        Assert.GreaterOrEqual(Mathf.Sqrt((a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z)), 1.5f, $"seed={seed}");
                    }
            }
        }

        [Test] public void 同じ入力から同じ結果_乱数の順番が決まっている()
        {
            var pts = StagePoints();
            var catalog = Builtin().ToSpawnEntries();
            var p = new ThParams();
            for (int seed = 0; seed < 20; seed++)
                Assert.AreEqual(TreasureSpawner.Spawn(seed, 4, p, pts, catalog).Fingerprint(), TreasureSpawner.Spawn(seed, 4, p, pts, catalog).Fingerprint());
            var seen = new HashSet<string>();
            for (int seed = 0; seed < 50; seed++) seen.Add(TreasureSpawner.Spawn(seed, 4, p, pts, catalog).Fingerprint());
            Assert.Greater(seen.Count, 45);
        }

        [Test] public void 奥の距離帯ほど値段が高い()
        {
            var pts = StagePoints();
            var catalog = Builtin().ToSpawnEntries();
            var p = new ThParams();
            var sum = new double[3]; var cnt = new int[3];
            for (int seed = 0; seed < 300; seed++)
                foreach (var t in TreasureSpawner.Spawn(seed, 4, p, pts, catalog).Items)
                {
                    int band = pts[t.PointIndex].Band;
                    sum[band] += t.Value; cnt[band]++;
                }
            double m0 = sum[0] / cnt[0], m1 = sum[1] / cnt[1], m2 = sum[2] / cnt[2];
            Assert.Greater(m2, m0 + 50, $"帯 2 の平均 {m2:F0} / 帯 0 の平均 {m0:F0}");
            Assert.Greater(m2, m1, $"帯 2 {m2:F0} / 帯 1 {m1:F0}");
        }

        // ---- 小さな合成データでの規則 ----

        static SpawnCatalogEntry Entry(TreasureKind k, int i, string id, int min, int max, float w = 0.1f, float d = 0.1f, float h = 0.1f) =>
            new SpawnCatalogEntry(k, i, id, min, max, new Vector3(w, d, h));

        static ThParams OneS()
        {
            var p = new ThParams();
            foreach (var row in p.layout.rows) { row.spawnS = 1; row.spawnM = 0; }
            return p;
        }

        [Test] public void 大きすぎるお宝は選ばれず_入るお宝がなければ置かない()
        {
            var points = new[] { new PlacementPoint(Vector3.zero, 1, true, true, new Vector3(0.2f, 0.2f, 0.2f)) };
            var catalog = new[] { Entry(TreasureKind.S, 0, "big", 100, 100, 0.3f, 0.1f, 0.1f), Entry(TreasureKind.S, 1, "small", 100, 100) };
            for (int seed = 0; seed < 20; seed++)
            {
                var r = TreasureSpawner.Spawn(seed, 1, OneS(), points, catalog);
                Assert.AreEqual(1, r.Items.Count);
                Assert.AreEqual(1, r.Items[0].CatalogIndex);
            }
            var none = TreasureSpawner.Spawn(1, 1, OneS(), points, new[] { catalog[0] });
            Assert.AreEqual(0, none.Items.Count);
            Assert.IsNotEmpty(none.Warnings);
        }

        [Test] public void 置き場所のフラグに合わない種類は置かない()
        {
            var p = new ThParams();
            foreach (var row in p.layout.rows) { row.spawnS = 1; row.spawnM = 1; }
            var points = new[] { new PlacementPoint(Vector3.zero, 1, true, false), new PlacementPoint(Vector3.right * 3, 1, false, true) };
            var catalog = new[] { Entry(TreasureKind.S, 0, "s", 100, 100), Entry(TreasureKind.M, 1, "m", 500, 500) };
            for (int seed = 0; seed < 30; seed++)
            {
                var r = TreasureSpawner.Spawn(seed, 1, p, points, catalog);
                Assert.AreEqual(2, r.Items.Count);
                foreach (var t in r.Items) Assert.AreEqual(t.Kind == TreasureKind.M ? 1 : 0, t.PointIndex);
            }
        }

        [Test] public void 同じお宝しか入らないときだけ1_5mの決まりを外して警告する()
        {
            var p = new ThParams();
            foreach (var row in p.layout.rows) { row.spawnS = 2; row.spawnM = 0; }
            var near = new[] { new PlacementPoint(Vector3.zero, 1), new PlacementPoint(new Vector3(1.0f, 0f, 0f), 1) };
            var onlyOne = new[] { Entry(TreasureKind.S, 0, "a", 100, 100) };
            var r = TreasureSpawner.Spawn(1, 1, p, near, onlyOne);
            Assert.AreEqual(2, r.Items.Count);
            Assert.AreEqual(1, r.Warnings.Count);
            // 候補が他にあれば外さない
            var two = new[] { Entry(TreasureKind.S, 0, "a", 100, 100), Entry(TreasureKind.S, 1, "b", 100, 100) };
            for (int seed = 0; seed < 20; seed++)
            {
                var r2 = TreasureSpawner.Spawn(seed, 1, p, near, two);
                CollectionAssert.IsEmpty(r2.Warnings);
                Assert.AreNotEqual(r2.Items[0].CatalogIndex, r2.Items[1].CatalogIndex);
            }
            // 1.5m 以上離れていれば同じお宝でもよい
            var far = new[] { new PlacementPoint(Vector3.zero, 1), new PlacementPoint(new Vector3(1.5f, 0f, 0f), 1) };
            CollectionAssert.IsEmpty(TreasureSpawner.Spawn(1, 1, p, far, onlyOne).Warnings);
        }

        [Test] public void 同じお宝の1_5m判定はmmに丸めて比べる()
        {
            var p = new ThParams();
            foreach (var row in p.layout.rows) { row.spawnS = 2; row.spawnM = 0; }
            var onlyOne = new[] { Entry(TreasureKind.S, 0, "a", 100, 100) };
            // 1499.4mm → 1499mm（1.5m 未満）。1499.6mm → 1500mm（1.5m 以上）
            var under = new[] { new PlacementPoint(Vector3.zero, 1), new PlacementPoint(new Vector3(1.4994f, 0f, 0f), 1) };
            var over = new[] { new PlacementPoint(Vector3.zero, 1), new PlacementPoint(new Vector3(1.4996f, 0f, 0f), 1) };
            Assert.AreEqual(1, TreasureSpawner.Spawn(1, 1, p, under, onlyOne).Warnings.Count);
            CollectionAssert.IsEmpty(TreasureSpawner.Spawn(1, 1, p, over, onlyOne).Warnings);
        }

        [Test] public void 実際の34か所と新しいカタログで_シード12345の4人の配置は固定値と一致する()
        {
            var r = TreasureSpawner.Spawn(12345, 4, new ThParams(), StagePoints(), Builtin().ToSpawnEntries());
            string actual = string.Join(";", r.Items.Select(t => $"{t.PointIndex},{t.CatalogIndex},{t.Value}"));
            Assert.AreEqual(23, r.Items.Count);
            CollectionAssert.IsEmpty(r.Warnings);
            Assert.AreEqual(FixedSeed12345, actual, "配置の乱数の順番や選び方を変えると、全端末の配置が変わる（変えたのなら意図を確かめて固定値を更新する）");
        }

        const string FixedSeed12345 = "17,2,100;11,7,300;20,0,100;31,9,600;16,4,200;26,9,500;10,2,100;22,5,200;9,8,500;18,2,200;7,9,600;21,6,300;25,10,600;1,3,200;28,4,200;13,3,200;30,8,400;3,0,100;14,1,100;32,2,200;0,8,400;27,11,800;8,11,700";   // (PointIndex,CatalogIndex,Value) の並び

        [Test] public void 置き場所のハッシュは属性が変わると変わり_mmと0_1度より小さいずれでは変わらない()
        {
            PlacementPoint P(Vector3? pos = null, bool s = true, bool m = false, Vector3? size = null, float yaw = 90f, int band = 1) =>
                new PlacementPoint(pos ?? new Vector3(1f, 0.9f, 5f), band, s, m, size ?? new Vector3(0.4f, 0.3f, 0.5f), yaw);
            string H(params PlacementPoint[] pts) => ThTreasureSetup.ComputeHash("cat", pts);
            string baseHash = H(P());
            Assert.AreEqual(baseHash, H(P()));
            Assert.AreEqual(baseHash, H(P(pos: new Vector3(1.0002f, 0.9001f, 4.9998f), size: new Vector3(0.4001f, 0.3f, 0.5f), yaw: 90.01f)), "mm・0.1° より小さいずれ");
            Assert.AreNotEqual(baseHash, H(P(pos: new Vector3(1.002f, 0.9f, 5f))), "位置");
            Assert.AreNotEqual(baseHash, H(P(s: false)), "AllowS");
            Assert.AreNotEqual(baseHash, H(P(m: true)), "AllowM");
            Assert.AreNotEqual(baseHash, H(P(size: new Vector3(0.4f, 0.3f, 0.52f))), "MaxSize");
            Assert.AreNotEqual(baseHash, H(P(yaw: 90.2f)), "YawDegrees");
            Assert.AreNotEqual(baseHash, H(P(band: 2)), "Band");
            Assert.AreNotEqual(baseHash, H(P(), P()), "個数");
            Assert.AreNotEqual(baseHash, ThTreasureSetup.ComputeHash("cat2", new[] { P() }), "カタログのハッシュ");
        }

        [Test] public void 候補は値段の安い順で_帯0は安い方へ_帯2は高い方へ偏る()
        {
            var p = OneS();
            var catalog = new[]
            {
                Entry(TreasureKind.S, 0, "c", 300, 300), Entry(TreasureKind.S, 1, "a", 100, 100), Entry(TreasureKind.S, 2, "b", 200, 200),
            };
            double Mean(int band)
            {
                var pts = new[] { new PlacementPoint(Vector3.zero, band) };
                double sum = 0;
                for (int seed = 0; seed < 600; seed++) sum += TreasureSpawner.Spawn(seed, 1, p, pts, catalog).Items[0].Value;
                return sum / 600;
            }
            double m0 = Mean(0), m1 = Mean(1), m2 = Mean(2);
            Assert.Less(m0, m1 - 20);
            Assert.Less(m1, m2 - 20);
        }

        [Test] public void 値段は帯の中で100G刻み()
        {
            var p = OneS();
            var pts = new[] { new PlacementPoint(Vector3.zero, 1) };
            var seen = new HashSet<int>();
            for (int seed = 0; seed < 200; seed++)
            {
                var r = TreasureSpawner.Spawn(seed, 1, p, pts, new[] { Entry(TreasureKind.S, 0, "x", 300, 600) });
                seen.Add(r.Items[0].Value);
            }
            CollectionAssert.AreEquivalent(new[] { 300, 400, 500, 600 }, seen);
        }
    }
}
