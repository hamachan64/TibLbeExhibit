using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul.Tests
{
    /// <summary>配置・カタログ・握りの判定・位置の補間（純粋な部分）</summary>
    public class ThTreasureLogicTests
    {
        const string CatalogPath = "Assets/_Content/TreasureHaul/Settings/ThTreasureCatalog.asset";

        static List<PlacementPoint> Points(int n = 30)
        {
            var list = new List<PlacementPoint>();
            for (int i = 0; i < n; i++) list.Add(new PlacementPoint(new Vector3((i % 5) * 0.75f - 1.5f, 0, 3f + i / 5), i % 3));
            return list;
        }

        static readonly SpawnCatalogEntry[] Catalog =
        {
            new SpawnCatalogEntry(TreasureKind.S, 0, "s0", 100, 200), new SpawnCatalogEntry(TreasureKind.S, 1, "s1", 200, 300),
            new SpawnCatalogEntry(TreasureKind.M, 2, "m2", 500, 700),
        };

        // ---- TreasureSpawner ----

        [Test] public void 同じ入力から同じ表ができる()
        {
            var a = TreasureSpawner.Spawn(1234, 3, new ThParams(), Points(), Catalog);
            var b = TreasureSpawner.Spawn(1234, 3, new ThParams(), Points(), Catalog);
            Assert.AreEqual(a.Fingerprint(), b.Fingerprint());
            Assert.AreEqual(a.Items.Count, b.Items.Count);
            for (int i = 0; i < a.Items.Count; i++)
            {
                Assert.AreEqual(a.Items[i].Value, b.Items[i].Value);
                Assert.AreEqual(a.Items[i].PointIndex, b.Items[i].PointIndex);
                Assert.AreEqual(a.Items[i].YawDegrees, b.Items[i].YawDegrees);
            }
        }

        [Test] public void シードが違えば表が違う()
        {
            var seen = new HashSet<string>();
            for (int s = 0; s < 20; s++) seen.Add(TreasureSpawner.Spawn(s, 2, new ThParams(), Points(), Catalog).Fingerprint());
            Assert.Greater(seen.Count, 15);
        }

        [Test] public void 人数ごとの出現数はPの表のSとM()
        {
            var p = new ThParams();
            for (int players = 1; players <= 4; players++)
            {
                var r = TreasureSpawner.Spawn(7, players, p, Points(), Catalog);
                var row = p.layout.rows[players - 1];
                Assert.AreEqual(row.spawnS, r.Items.Count(t => t.Kind == TreasureKind.S), $"{players}人 S");
                Assert.AreEqual(row.spawnM, r.Items.Count(t => t.Kind == TreasureKind.M), $"{players}人 M");
                Assert.IsFalse(r.Warnings.Any(w => !w.Contains("決まりを外しました")), string.Join("/", r.Warnings));   // お宝が 2 種しかない合成データなので、1.5m の決まりは外れることがある
            }
        }

        [Test] public void 人数が範囲外ならクランプする()
        {
            var p = new ThParams();
            Assert.AreEqual(p.layout.rows[0].spawnS + p.layout.rows[0].spawnM, TreasureSpawner.Spawn(1, 0, p, Points(), Catalog).Items.Count);
            Assert.AreEqual(p.layout.rows[3].spawnS + p.layout.rows[3].spawnM, TreasureSpawner.Spawn(1, 9, p, Points(), Catalog).Items.Count);
        }

        [Test] public void 値段はそのお宝の帯の中で100G刻み()
        {
            var p = new ThParams();
            for (int seed = 0; seed < 50; seed++)
            {
                foreach (var t in TreasureSpawner.Spawn(seed, 4, p, Points(), Catalog).Items)
                {
                    var e = Catalog[t.CatalogIndex];
                    Assert.AreEqual(0, t.Value % 100);
                    Assert.GreaterOrEqual(t.Value, e.ValueMin);
                    Assert.LessOrEqual(t.Value, e.ValueMax);
                }
            }
        }

        [Test] public void 遠い帯ほど値段の平均が高い()
        {
            var p = new ThParams();
            var sum = new double[3]; var cnt = new int[3];
            var pts = Points();
            for (int seed = 0; seed < 400; seed++)
                foreach (var t in TreasureSpawner.Spawn(seed, 4, p, pts, Catalog).Items)
                {
                    if (t.Kind != TreasureKind.S) continue;
                    int band = pts[t.PointIndex].Band;
                    sum[band] += t.Value; cnt[band]++;
                }
            double m0 = sum[0] / cnt[0], m1 = sum[1] / cnt[1], m2 = sum[2] / cnt[2];
            Assert.Less(m0, m1, $"{m0} {m1}");
            Assert.Less(m1, m2, $"{m1} {m2}");
        }

        [Test] public void 候補点が足りなくても例外を出さず置ける数だけ置いて警告する()
        {
            var r = TreasureSpawner.Spawn(5, 4, new ThParams(), Points(10), Catalog);
            Assert.AreEqual(10, r.Items.Count);
            Assert.IsNotEmpty(r.Warnings);
            Assert.DoesNotThrow(() => TreasureSpawner.Spawn(5, 4, new ThParams(), Points(0), Catalog));
        }

        [Test] public void 同じ候補点を2回使わない()
        {
            for (int seed = 0; seed < 30; seed++)
            {
                var r = TreasureSpawner.Spawn(seed, 4, new ThParams(), Points(), Catalog);
                Assert.AreEqual(r.Items.Count, r.Items.Select(t => t.PointIndex).Distinct().Count());
                Assert.AreEqual(r.Items.Count, r.Items.Select(t => t.Index).Distinct().Count());
                for (int i = 0; i < r.Items.Count; i++) Assert.AreEqual(i, r.Items[i].Index);
            }
        }

        [Test] public void プレハブはその種類の使うお宝から選ばれる()
        {
            var r = TreasureSpawner.Spawn(3, 4, new ThParams(), Points(), Catalog);
            foreach (var t in r.Items)
            {
                if (t.Kind == TreasureKind.M) Assert.AreEqual(2, t.CatalogIndex);
                else Assert.That(t.CatalogIndex, Is.EqualTo(0).Or.EqualTo(1));
            }
            // M の使うお宝が無ければ M は置かず、警告する
            var onlyS = TreasureSpawner.Spawn(3, 4, new ThParams(), Points(), new[] { new SpawnCatalogEntry(TreasureKind.S, 0) });
            Assert.IsFalse(onlyS.Items.Any(t => t.Kind == TreasureKind.M));
            Assert.IsNotEmpty(onlyS.Warnings);
        }

        // ---- ThTreasureCatalog ----

        static ThTreasureCatalog Builtin() => AssetDatabase.LoadAssetAtPath<ThTreasureCatalog>(CatalogPath);

        [Test] public void 組み込みのカタログに問題が無い()
        {
            var c = Builtin();
            Assert.IsNotNull(c);
            CollectionAssert.IsEmpty(c.Validate(), string.Join("\n", c.Validate()));
            Assert.GreaterOrEqual(c.entries.Count(e => e.kind == TreasureKind.S && e.use), 2);
            Assert.GreaterOrEqual(c.entries.Count(e => e.kind == TreasureKind.M && e.use), 1);
            Assert.IsNotNull(c.entries.First(e => e.kind == TreasureKind.M).prefab.GetComponentInChildren<MeshRenderer>(true));
        }

        [Test] public void Validateは欠けと重複を検出する()
        {
            var c = ScriptableObject.CreateInstance<ThTreasureCatalog>();
            c.entries = new List<ThTreasureEntry>
            {
                new ThTreasureEntry { id = "a", kind = TreasureKind.S, prefab = null, use = true },
                new ThTreasureEntry { id = "a", kind = TreasureKind.S, prefab = null, use = true },
                new ThTreasureEntry { id = "", kind = TreasureKind.M, prefab = null, use = false },
            };
            var errors = c.Validate();
            Assert.IsTrue(errors.Exists(e => e.Contains("重複")));
            Assert.IsTrue(errors.Exists(e => e.Contains("プレハブが未設定")));
            Assert.IsTrue(errors.Exists(e => e.Contains("ID が空")));
            Assert.IsTrue(errors.Exists(e => e.Contains("M の「使う」お宝が 0 件")));
            Assert.IsTrue(errors.Exists(e => e.Contains("練習用")));
            Object.DestroyImmediate(c);
        }

        [Test] public void ハッシュは並びが同じなら同じで1件違えば変わる()
        {
            ThTreasureEntry E(TreasureKind k, string id) => new ThTreasureEntry { id = id, kind = k, valueMin = 100, valueMax = 200, width = 0.2f, depth = 0.2f, height = 0.2f };
            string Hash(params ThTreasureEntry[] e) => ThTreasureCatalog.ComputeHash(e);
            var a = Hash(E(TreasureKind.S, "x"), E(TreasureKind.S, "y"), E(TreasureKind.M, "z"));
            Assert.AreEqual(a, Hash(E(TreasureKind.S, "x"), E(TreasureKind.S, "y"), E(TreasureKind.M, "z")));
            Assert.AreEqual(8, a.Length);
            Assert.AreNotEqual(a, Hash(E(TreasureKind.S, "x"), E(TreasureKind.S, "y"), E(TreasureKind.M, "w")));
            Assert.AreNotEqual(a, Hash(E(TreasureKind.S, "x"), E(TreasureKind.S, "y")));
            Assert.AreNotEqual(a, Hash(E(TreasureKind.S, "y"), E(TreasureKind.S, "x"), E(TreasureKind.M, "z")));
        }

        [Test] public void 使わないお宝はハッシュに入らない()
        {
            var c = ScriptableObject.CreateInstance<ThTreasureCatalog>();
            c.entries = new List<ThTreasureEntry> { new ThTreasureEntry { id = "a", kind = TreasureKind.S }, new ThTreasureEntry { id = "b", kind = TreasureKind.S, use = false } };
            var h1 = c.ComputeHash();
            c.entries[1].use = true;
            Assert.AreNotEqual(h1, c.ComputeHash());
            Object.DestroyImmediate(c);
        }

        // ---- 握りの判定 ----

        static GripStateMachine Machine() => new GripStateMachine { GripThreshold = 0.7f, ReleaseThreshold = 0.4f, ReleaseHoldSeconds = 0.25f, LostGraceSeconds = 0.1f };

        [Test] public void 握りはつかむしきい値以上で握った()
        {
            var m = Machine();
            Assert.AreEqual(GripEvent.None, m.Update(0, true, 0.69f));
            Assert.IsFalse(m.IsGripping);
            Assert.AreEqual(GripEvent.Gripped, m.Update(0.1, true, 0.7f));
            Assert.IsTrue(m.IsGripping);
        }

        [Test] public void ヒステリシス_離すしきい値と継続時間()
        {
            var m = Machine();
            m.Update(0, true, 1f);
            // つかむしきい値を下回っても、離すしきい値以上なら握ったまま
            Assert.AreEqual(GripEvent.None, m.Update(0.1, true, 0.5f));
            Assert.AreEqual(GripEvent.None, m.Update(1.0, true, 0.45f));
            Assert.IsTrue(m.IsGripping);
            // 離すしきい値を下回っても、継続時間に満たなければ離さない
            Assert.AreEqual(GripEvent.None, m.Update(2.0, true, 0.1f));
            Assert.AreEqual(GripEvent.None, m.Update(2.2, true, 0.1f));
            Assert.IsTrue(m.IsGripping);
            // 継続時間を超えたら離す
            Assert.AreEqual(GripEvent.Released, m.Update(2.26, true, 0.1f));
            Assert.IsFalse(m.IsGripping);
            Assert.AreEqual(GripReleaseReason.Opened, m.LastReleaseReason);
        }

        [Test] public void 少しの間だけ下回って戻ったら開いたことにしない()
        {
            var m = Machine();
            m.Update(0, true, 1f);
            m.Update(1.0, true, 0.1f);
            m.Update(1.2, true, 0.9f);        // 戻った
            m.Update(1.4, true, 0.1f);        // もう一度下回る。数え直し
            Assert.AreEqual(GripEvent.None, m.Update(1.6, true, 0.1f));
            Assert.IsTrue(m.IsGripping);
            Assert.AreEqual(GripEvent.Released, m.Update(1.7, true, 0.1f));
        }

        [Test] public void 見失いで解除の理由は見失い()
        {
            var m = Machine();
            m.Update(0, true, 1f);
            Assert.AreEqual(GripEvent.None, m.Update(1.0, false, 0f));   // 猶予の間は握ったまま
            Assert.IsTrue(m.IsGripping);
            Assert.AreEqual(GripEvent.Released, m.Update(1.11, false, 0f));
            Assert.AreEqual(GripReleaseReason.Lost, m.LastReleaseReason);
            Assert.IsFalse(m.IsTracked);
        }

        [Test] public void 猶予内に手が戻れば握りは続く()
        {
            var m = Machine();
            m.Update(0, true, 1f);
            m.Update(1.0, false, 0f);
            Assert.AreEqual(GripEvent.None, m.Update(1.05, true, 1f));
            Assert.AreEqual(GripEvent.None, m.Update(1.5, false, 0f));   // 新しい見失い。数え直し
            Assert.IsTrue(m.IsGripping);
        }

        [Test] public void 握っていないときの見失いは何も起こさない()
        {
            var m = Machine();
            Assert.AreEqual(GripEvent.None, m.Update(0, false, 0f));
            Assert.AreEqual(GripEvent.None, m.Update(5, false, 0f));
        }

        static Vector3[] Straight() => new[] { new Vector3(0, 0, 0), new Vector3(0, 0, 0.03f), new Vector3(0, 0, 0.06f), new Vector3(0, 0, 0.09f), new Vector3(0, 0, 0.12f) };

        [Test] public void 指の曲がり具合は伸びた指で低い()
        {
            Assert.Less(GripStrength.ComputeFinger(Straight()), 0.1f);
            Assert.AreEqual(GripStrength.Invalid, GripStrength.ComputeFinger(new Vector3[3]));
            Assert.IsTrue(GripStrength.TryCompute(Straight(), Straight(), Straight(), Straight(), out var g));
            Assert.Less(g, 0.1f);
        }

        // ---- 位置の補間 ----

        [Test] public void 補間は2点の間を遅れた時刻で返す()
        {
            var ip = new PoseInterpolator();
            ip.Add(10.0, new Vector3(0, 0, 0), Quaternion.identity);
            ip.Add(10.1, new Vector3(1, 0, 0), Quaternion.identity);
            ip.Add(10.2, new Vector3(2, 0, 0), Quaternion.identity);
            Assert.IsTrue(ip.TrySample(10.15, 0.05f, 0.05f, out var p, out _));   // 描く時刻 10.10
            Assert.AreEqual(1f, p.x, 1e-4f);
            Assert.IsTrue(ip.TrySample(10.175, 0.05f, 0.05f, out p, out _));      // 10.125
            Assert.AreEqual(1.25f, p.x, 1e-3f);
        }

        [Test] public void 遅れて届いたら短い間だけ先読みして上限で止める()
        {
            var ip = new PoseInterpolator();
            ip.Add(10.0, Vector3.zero, Quaternion.identity);
            ip.Add(10.1, new Vector3(1, 0, 0), Quaternion.identity);
            ip.Add(10.1 + 0.0, new Vector3(9, 0, 0), Quaternion.identity);   // 同じ時刻は捨てる
            ip.TrySample(10.2, 0.0f, 0.05f, out var p, out _);               // 0.1 秒先 → 上限 0.05 秒（速度 10m/s）
            Assert.AreEqual(1.5f, p.x, 1e-3f);
            ip.TrySample(20.0, 0.0f, 0.05f, out p, out _);
            Assert.AreEqual(1.5f, p.x, 1e-3f);   // 何秒たっても上限で止まる
        }

        [Test] public void 補間はサンプルが無ければfalseで1点ならその点()
        {
            var ip = new PoseInterpolator();
            Assert.IsFalse(ip.TrySample(1, 0.05f, 0.05f, out _, out _));
            ip.Add(5, new Vector3(3, 0, 0), Quaternion.identity);
            Assert.IsTrue(ip.TrySample(100, 0.05f, 0.05f, out var p, out _));
            Assert.AreEqual(3f, p.x);
        }

        [Test] public void 静止の判定は継続時間で決まる()
        {
            var d = new RestDetector();
            Assert.IsFalse(d.Update(0, 0.01f, 0f, 0.2f));
            Assert.IsFalse(d.Update(0.1, 0.01f, 0f, 0.2f));
            Assert.IsFalse(d.Update(0.15, 1f, 0f, 0.2f));      // 動いた → 数え直し
            Assert.IsFalse(d.Update(0.3, 0.01f, 0f, 0.2f));
            Assert.IsTrue(d.Update(0.51, 0.01f, 0f, 0.2f));
        }
    }
}
