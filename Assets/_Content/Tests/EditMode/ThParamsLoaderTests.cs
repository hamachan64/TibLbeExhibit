using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul.Tests
{
    public class ThParamsLoaderTests
    {
        static ThParams Defaults() => new ThParams();
        static ThParamsLoadResult Load(string json, ThEnvironmentInfo env = null) => ThParamsLoader.Load(Defaults(), json, env);

        [Test] public void 初期値は要件定義書P章どおり()
        {
            var p = Defaults();
            Assert.AreEqual(10f, p.progress.openingSeconds);
            Assert.AreEqual(30f, p.progress.practiceSeconds);
            Assert.AreEqual(30f, p.progress.holdMaxSeconds);
            Assert.AreEqual(180f, p.progress.playingSeconds);
            Assert.AreEqual(15f, p.progress.resultSeconds);
            Assert.AreEqual(10f, p.progress.hostLostSeconds);
            Assert.AreEqual(1f, p.progress.heartbeatInterval);
            Assert.AreEqual(0.25f, p.progress.syncTolerance);
            Assert.AreEqual(1f, p.progress.itemsInterval);
            Assert.AreEqual(0.5f, p.progress.resendInterval);
            Assert.AreEqual(60, p.progress.rpcLimit);
            Assert.AreEqual(10f, p.practice.hintSeconds);
            Assert.AreEqual(150, p.practice.practiceValue);
            Assert.AreEqual(1.5f, p.grip.beamDistance);
            Assert.AreEqual(0.3f, p.grip.flyTime);
            Assert.AreEqual(0.7f, p.grip.gripThreshold);
            Assert.AreEqual(0.4f, p.grip.releaseThreshold);
            Assert.AreEqual(0.25f, p.grip.releaseHoldSeconds);
            Assert.AreEqual(0.5f, p.grip.mGraceSeconds);
            Assert.AreEqual(1.5f, p.grip.lostWaitSeconds);
            Assert.AreEqual(0.5f, p.grip.detachDistance);
            Assert.AreEqual(0.3f, p.grip.aimHoldSeconds);
            Assert.AreEqual(0.15f, p.grip.shoulderDown);
            Assert.AreEqual(0.18f, p.grip.shoulderSide);
            Assert.AreEqual(0.05f, p.grip.snapRadius);
            Assert.AreEqual(10f, p.grip.followSpeedMax);
            Assert.AreEqual(0.1f, p.grip.trackingLossGrace);
            Assert.AreEqual(0.3f, p.grip.placeSeconds);
            Assert.AreEqual(1f, p.grip.approvalWaitSeconds);
            Assert.AreEqual(1f / 30f, p.grip.poseSendInterval, 1e-6f);
            Assert.AreEqual(0.05f, p.grip.interpDelay);
            Assert.AreEqual(100, p.treasure.sValueMin); Assert.AreEqual(300, p.treasure.sValueMax);
            Assert.AreEqual(400, p.treasure.mValueMin); Assert.AreEqual(800, p.treasure.mValueMax);
            Assert.AreEqual(1500, p.treasure.lValueMin); Assert.AreEqual(2500, p.treasure.lValueMax);
            Assert.AreEqual(3000, p.treasure.secretValue);
            Assert.AreEqual(600, p.treasure.discoverValue);
            Assert.AreEqual(0.8f, p.treasure.impactSpeed);
            Assert.AreEqual(30, p.treasure.damagePercent1); Assert.AreEqual(30, p.treasure.damagePercent2); Assert.AreEqual(40, p.treasure.damagePercent3);
            Assert.AreEqual(0.5f, p.treasure.redamageInterval);
            Assert.AreEqual(0.5f, p.treasure.correctionGraceSeconds);
            Assert.AreEqual(0.3f, p.treasure.jumpDistance);
            Assert.AreEqual(3, p.treasure.speedSmoothFrames);
            Assert.AreEqual(0.2f, p.treasure.stopSeconds);
            Assert.AreEqual(0.3f, p.treasure.dedupSeconds);
            Assert.AreEqual(0.3f, p.layout.edgeMargin);
            Assert.AreEqual(1000, p.layout.lTargetBonus);
            Assert.AreEqual(4, p.layout.rows.Length);
            CollectionAssert.AreEqual(new[] { 8, 10, 12, 14 }, p.layout.rows.Select(r => r.spawnS));
            CollectionAssert.AreEqual(new[] { 4, 5, 7, 9 }, p.layout.rows.Select(r => r.spawnM));
            CollectionAssert.AreEqual(new[] { 2000, 2500, 3300, 4100 }, p.layout.rows.Select(r => r.targetAmount));
            Assert.AreEqual(20, p.operation.batteryWarningPercent);
        }

        [Test] public void 時間の下限は結合確認で短縮できるほど小さい()
        {
            var json = "{\"progress\":{\"openingSeconds\":3,\"practiceSeconds\":5,\"playingSeconds\":10,\"resultSeconds\":3}}";
            var r = Load(json);
            Assert.AreEqual(0, r.Warnings.Count, string.Join("\n", r.Warnings));
            Assert.AreEqual(3f, r.Params.progress.openingSeconds);
            Assert.AreEqual(10f, r.Params.progress.playingSeconds);
        }

        [Test] public void すべての数値にRangeが付いている()
        {
            int missing = 0;
            void Walk(System.Type t, string path)
            {
                foreach (var f in t.GetFields())
                {
                    if (f.FieldType.IsArray) { Walk(f.FieldType.GetElementType(), path + "." + f.Name); continue; }
                    if (f.FieldType.IsClass && f.FieldType != typeof(string)) { Walk(f.FieldType, path + "." + f.Name); continue; }
                    if (f.GetCustomAttributes(typeof(UnityEngine.RangeAttribute), false).Length == 0) { missing++; Debug.Log("Range 無し: " + path + "." + f.Name); }
                }
            }
            Walk(typeof(ThParams), "ThParams");
            Assert.AreEqual(0, missing);
        }

        [Test] public void 書いた項目だけ上書きされる()
        {
            var r = Load("{\"practice\":{\"practiceValue\":200}}");
            Assert.AreEqual(200, r.Params.practice.practiceValue);
            Assert.AreEqual(10f, r.Params.practice.hintSeconds);
            Assert.AreEqual(10f, r.Params.progress.openingSeconds);
            Assert.AreEqual(0, r.Warnings.Count);
        }

        [Test] public void 渡した初期値は書き換えない()
        {
            var d = Defaults();
            ThParamsLoader.Load(d, "{\"practice\":{\"practiceValue\":200}}", null);
            Assert.AreEqual(150, d.practice.practiceValue);
        }

        [Test] public void JSONがnullまたは空なら初期値のまま警告なし()
        {
            foreach (var j in new string[] { null, "", "  " })
            {
                var r = Load(j);
                Assert.AreEqual(0, r.Warnings.Count);
                Assert.AreEqual(ThParamsLoader.ComputeVersion(Defaults()), r.Version);
            }
        }

        [Test] public void 壊れたJSONは初期値のままで警告()
        {
            var r = Load("{ これは JSON ではない");
            Assert.AreEqual(1, r.Warnings.Count);
            Assert.AreEqual(ThParamsLoader.ComputeVersion(Defaults()), r.Version);
        }

        [Test] public void 範囲外の値はその項目だけ初期値に戻る()
        {
            var r = Load("{\"progress\":{\"openingSeconds\":9999,\"practiceSeconds\":20},\"practice\":{\"practiceValue\":200}}");
            Assert.AreEqual(10f, r.Params.progress.openingSeconds);
            Assert.AreEqual(20f, r.Params.progress.practiceSeconds, "同じグループの他の項目は残る");
            Assert.AreEqual(200, r.Params.practice.practiceValue);
            Assert.AreEqual(1, r.Warnings.Count);
            StringAssert.Contains("openingSeconds", r.Warnings[0]);
        }

        [Test] public void 人数表の行数が違うと表が初期値に戻る()
        {
            var r = Load("{\"layout\":{\"rows\":[{\"spawnS\":1,\"spawnM\":1,\"targetAmount\":100}]}}");
            Assert.AreEqual(4, r.Params.layout.rows.Length);
            Assert.AreEqual(8, r.Params.layout.rows[0].spawnS);
            Assert.AreEqual(1, r.Warnings.Count);
        }

        [Test] public void 人数表の行の範囲外は行の項目だけ戻る()
        {
            var r = Load("{\"layout\":{\"rows\":[{\"spawnS\":1,\"spawnM\":1,\"targetAmount\":100},{\"spawnS\":-5,\"spawnM\":5,\"targetAmount\":2500},{\"spawnS\":12,\"spawnM\":7,\"targetAmount\":3300},{\"spawnS\":14,\"spawnM\":9,\"targetAmount\":4100}]}}");
            Assert.AreEqual(1, r.Params.layout.rows[0].spawnS);
            Assert.AreEqual(10, r.Params.layout.rows[1].spawnS);
            Assert.AreEqual(5, r.Params.layout.rows[1].spawnM);
        }

        [Test] public void 減額率の合計が100でないとP4グループ全体が初期値に戻る()
        {
            var r = Load("{\"treasure\":{\"damagePercent1\":50,\"impactSpeed\":2.0}}");
            Assert.AreEqual(30, r.Params.treasure.damagePercent1);
            Assert.AreEqual(0.8f, r.Params.treasure.impactSpeed, "同じグループの正常な上書きも戻る");
            Assert.AreEqual(1, r.Warnings.Count);
        }

        [Test] public void 別のグループは矛盾の影響を受けない()
        {
            var r = Load("{\"treasure\":{\"damagePercent1\":50},\"practice\":{\"practiceValue\":300}}");
            Assert.AreEqual(300, r.Params.practice.practiceValue);
        }

        [Test] public void つかむしきい値が離すしきい値以下ならP3グループが初期値に戻る()
        {
            var r = Load("{\"grip\":{\"gripThreshold\":0.3,\"releaseThreshold\":0.4,\"beamDistance\":2.0}}");
            Assert.AreEqual(0.7f, r.Params.grip.gripThreshold);
            Assert.AreEqual(1.5f, r.Params.grip.beamDistance);
            Assert.AreEqual(1, r.Warnings.Count);
            r = Load("{\"grip\":{\"gripThreshold\":0.5,\"releaseThreshold\":0.5}}");
            Assert.AreEqual(0.7f, r.Params.grip.gripThreshold, "等しいときも矛盾");
        }

        [Test] public void 価値の範囲は100G刻みで下限が上限以下()
        {
            var r = Load("{\"treasure\":{\"sValueMin\":150}}");
            Assert.AreEqual(100, r.Params.treasure.sValueMin);
            Assert.AreEqual(1, r.Warnings.Count);
            r = Load("{\"treasure\":{\"sValueMin\":500,\"sValueMax\":300}}");
            Assert.AreEqual(100, r.Params.treasure.sValueMin);
            r = Load("{\"treasure\":{\"sValueMin\":200,\"sValueMax\":200}}");
            Assert.AreEqual(200, r.Params.treasure.sValueMin, "等しいのは可");
            Assert.AreEqual(0, r.Warnings.Count);
        }

        [Test] public void 出現数が配置候補点より多いとP5グループが初期値に戻る()
        {
            var env = new ThEnvironmentInfo { PlacementPointCount = 20 };
            var r = Load("{\"layout\":{\"edgeMargin\":0.5}}", env);
            // 初期値自体が 23 個を要求するので戻しても矛盾する。警告は出て、グループは初期値になる。
            Assert.AreEqual(0.3f, r.Params.layout.edgeMargin);
            Assert.IsTrue(r.Warnings.Count >= 1);
            var ok = Load("{\"layout\":{\"edgeMargin\":0.5}}", new ThEnvironmentInfo { PlacementPointCount = 23 });
            Assert.AreEqual(0.5f, ok.Params.layout.edgeMargin);
            Assert.AreEqual(0, ok.Warnings.Count);
        }

        [Test] public void 初期値に戻しても矛盾していれば環境の確認を促す警告が足される()
        {
            var r = Load("{\"layout\":{\"edgeMargin\":0.5}}", new ThEnvironmentInfo { PlacementPointCount = 20 });
            Assert.AreEqual(0.3f, r.Params.layout.edgeMargin, "値は初期値のまま");
            Assert.IsTrue(r.Warnings.Exists(w => w.Contains("初期値でも矛盾")));
        }

        [Test] public void Heartbeat間隔は運営PC切断の待ちより短い()
        {
            var r = Load("{\"progress\":{\"heartbeatInterval\":5,\"hostLostSeconds\":3,\"playingSeconds\":60}}");
            Assert.AreEqual(1f, r.Params.progress.heartbeatInterval);
            Assert.AreEqual(180f, r.Params.progress.playingSeconds, "P-1 全体が戻る");
            Assert.AreEqual(1, r.Warnings.Count);
        }

        [Test] public void 発見演出の価値は100G刻みでなくてもよい()
        {
            var r = Load("{\"treasure\":{\"discoverValue\":650}}");
            Assert.AreEqual(650, r.Params.treasure.discoverValue);
            Assert.AreEqual(0, r.Warnings.Count);
        }

        [Test] public void TutorialSlotが4つ未満ならP2グループが初期値に戻る()
        {
            var r = Load("{\"practice\":{\"practiceValue\":300}}", new ThEnvironmentInfo { TutorialSlotCount = 3 });
            Assert.AreEqual(150, r.Params.practice.practiceValue);
            Assert.GreaterOrEqual(r.Warnings.Count, 1);
            r = Load("{\"practice\":{\"practiceValue\":300}}", new ThEnvironmentInfo { TutorialSlotCount = 4 });
            Assert.AreEqual(300, r.Params.practice.practiceValue);
        }

        [Test] public void 環境情報が未定ならその検査は飛ばす()
        {
            var r = Load("{\"practice\":{\"practiceValue\":300}}", null);
            Assert.AreEqual(300, r.Params.practice.practiceValue);
            r = Load("{\"practice\":{\"practiceValue\":300}}", new ThEnvironmentInfo());
            Assert.AreEqual(300, r.Params.practice.practiceValue);
            Assert.AreEqual(0, r.Warnings.Count);
        }

        [Test] public void 版は同じ値なら同じで1項目でも違えば違う()
        {
            var a = Load("{\"practice\":{\"practiceValue\":200}}");
            var b = Load("{\"practice\":{\"practiceValue\":200}}");
            var c = Load("{\"practice\":{\"practiceValue\":300}}");
            Assert.AreEqual(a.Version, b.Version);
            Assert.AreNotEqual(a.Version, c.Version);
            Assert.AreEqual(8, a.Version.Length);
            StringAssert.IsMatch("^[0-9a-f]{8}$", a.Version);
        }

        [Test] public void 値が初期値と同じ上書きは版が変わらない()
        {
            var a = Load(null);
            var b = Load("{\"practice\":{\"practiceValue\":150}}");
            Assert.AreEqual(a.Version, b.Version);
        }

        [Test] public void 雛形JSONをそのまま読むと初期値と同じ版になる()
        {
            var json = JsonUtility.ToJson(Defaults(), true);
            var r = Load(json);
            Assert.AreEqual(0, r.Warnings.Count);
            Assert.AreEqual(ThParamsLoader.ComputeVersion(Defaults()), r.Version);
        }
    }

    public class ThGameSettingsTests
    {
        [Test] public void 初期値は目標額スコア_追加要素なし_ビーム()
        {
            var s = new ThGameSettings();
            Assert.AreEqual(ThMode.TargetAndScore, s.mode);
            Assert.IsFalse(s.statue || s.large || s.golem || s.secret);
            Assert.AreEqual(ThGripStyle.Beam, s.gripStyle);
        }

        [Test] public void 無い_空は初期値で警告なし()
        {
            var s = ThGameSettings.Parse(null, out var w);
            Assert.IsNull(w);
            Assert.AreEqual(ThGripStyle.Beam, s.gripStyle);
        }

        [Test] public void 壊れていれば初期値と警告()
        {
            var s = ThGameSettings.Parse("{ x", out var w);
            Assert.IsNotNull(w);
            Assert.AreEqual(ThMode.TargetAndScore, s.mode);
            s = ThGameSettings.Parse("{\"mode\":9}", out w);
            Assert.IsNotNull(w);
        }

        [Test] public void 保存して読み直せる()
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "th_settings_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                ThSettingsFiles.SaveSettings(dir, new ThGameSettings { mode = ThMode.TargetOnly, statue = true, gripStyle = ThGripStyle.Direct });
                var s = ThSettingsFiles.LoadSettings(dir, out var w);
                Assert.IsNull(w);
                Assert.AreEqual(ThMode.TargetOnly, s.mode);
                Assert.IsTrue(s.statue);
                Assert.AreEqual(ThGripStyle.Direct, s.gripStyle);
            }
            finally { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true); }
        }

        [Test] public void ファイルが無ければ初期値()
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "th_none_" + System.Guid.NewGuid().ToString("N"));
            var s = ThSettingsFiles.LoadSettings(dir, out var w);
            Assert.IsNull(w);
            Assert.AreEqual(ThMode.TargetAndScore, s.mode);
        }
    }

    public class ThMessagesTests
    {
        [Test] public void 状態の往復()
        {
            var msg = new ThStateMsg
            {
                seq = 7, state = (int)RoundState.Playing, remaining = 123.5f, seed = -12345,
                participants = new[] { "a", "b" }, paramsVersion = "abcd1234",
                settings = new ThGameSettings { mode = ThMode.TargetOnly, large = true, gripStyle = ThGripStyle.Swing },
                tutorialColors = new[] { 0, 1 },
            };
            var args = ThMessages.Encode(1700000000123L, msg);
            Assert.AreEqual("1700000000123", args[0]);
            Assert.IsTrue(ThMessages.TryDecode(args, out long id, out ThStateMsg back));
            Assert.AreEqual(1700000000123L, id);
            Assert.AreEqual(7, back.seq);
            Assert.AreEqual((int)RoundState.Playing, back.state);
            Assert.AreEqual(123.5f, back.remaining);
            Assert.AreEqual(-12345, back.seed);
            CollectionAssert.AreEqual(new[] { "a", "b" }, back.participants);
            Assert.AreEqual("abcd1234", back.paramsVersion);
            Assert.AreEqual(ThMode.TargetOnly, back.settings.mode);
            Assert.IsTrue(back.settings.large);
            Assert.AreEqual(ThGripStyle.Swing, back.settings.gripStyle);
            CollectionAssert.AreEqual(new[] { 0, 1 }, back.tutorialColors);
        }

        [Test] public void Heartbeat_Resync_Paramsの往復()
        {
            Assert.IsTrue(ThMessages.TryDecode(ThMessages.Encode(0, new ThHeartbeatMsg { seq = 3, remaining = 9.5f, total = 0, paramsVersion = "v" }), out long i1, out ThHeartbeatMsg hb));
            Assert.AreEqual(0, i1); Assert.AreEqual(3, hb.seq); Assert.AreEqual(9.5f, hb.remaining);
            Assert.IsTrue(ThMessages.TryDecode(ThMessages.Encode(5, new ThResyncMsg { deviceId = "dev", seq = 2, paramsVersion = "v" }), out long i2, out ThResyncMsg rs));
            Assert.AreEqual(5, i2); Assert.AreEqual("dev", rs.deviceId);
            Assert.IsTrue(ThMessages.TryDecode(ThMessages.Encode(5, new ThParamsMsg { targetClientNo = -1, version = "v", json = "{\"a\":1}" }), out long _, out ThParamsMsg pm));
            Assert.AreEqual(-1, pm.targetClientNo); Assert.AreEqual("{\"a\":1}", pm.json);
        }

        [Test] public void 壊れた引数と空の引数は例外を出さずに捨てる()
        {
            ThStateMsg dto;
            long id;
            Assert.IsFalse(ThMessages.TryDecode<ThStateMsg>(null, out id, out dto));
            Assert.IsFalse(ThMessages.TryDecode<ThStateMsg>(new string[0], out id, out dto));
            Assert.IsFalse(ThMessages.TryDecode<ThStateMsg>(new[] { "1" }, out id, out dto));
            Assert.IsFalse(ThMessages.TryDecode<ThStateMsg>(new[] { "x", "{}" }, out id, out dto));
            Assert.IsFalse(ThMessages.TryDecode<ThStateMsg>(new[] { "-5", "{}" }, out id, out dto));
            Assert.IsFalse(ThMessages.TryDecode<ThStateMsg>(new[] { "1", "" }, out id, out dto));
            Assert.IsFalse(ThMessages.TryDecode<ThStateMsg>(new[] { "1", null }, out id, out dto));
            Assert.IsFalse(ThMessages.TryDecode<ThStateMsg>(new[] { "1", "{ broken" }, out id, out dto));
            Assert.IsFalse(ThMessages.TryDecode<ThStateMsg>(new[] { "1", "not json at all" }, out id, out dto));
        }
    }
}
