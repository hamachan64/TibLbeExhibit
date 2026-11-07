using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul.Tests
{
    /// <summary>墓守の霊・角冠の王（#67）: 扇の UV、亡霊を入れる回のオープニング、列挙の番号、文言</summary>
    public class ThEnemyModelsTests
    {
        // ---- 扇 ----

        [Test] public void 扇のUVは要がv0で弧がv1でuは左端0から右端1()
        {
            var m = new Mesh();
            PhantomView.BuildFan(m, 3f, 35f);
            var uv = m.uv;
            Assert.AreEqual(18, uv.Length);
            Assert.AreEqual(0f, uv[0].y, 1e-6f, "要は v = 0");
            Assert.AreEqual(0.5f, uv[0].x, 1e-6f);
            for (int i = 0; i <= 16; i++)
            {
                Assert.AreEqual(1f, uv[i + 1].y, 1e-6f, "弧は v = 1");
                Assert.AreEqual(i / 16f, uv[i + 1].x, 1e-5f);
            }
            Assert.AreEqual(0f, uv[1].x, 1e-6f); Assert.AreEqual(1f, uv[17].x, 1e-6f);
            Object.DestroyImmediate(m);
        }

        [Test] public void 扇の頂点と三角形は今までと同じ()
        {
            var m = new Mesh();
            PhantomView.BuildFan(m, 3f, 35f);
            var v = m.vertices; var t = m.triangles;
            Assert.AreEqual(new Vector3(0f, 0.02f, 0f), v[0]);
            float a0 = -35f * Mathf.Deg2Rad;
            Assert.AreEqual(Mathf.Sin(a0) * 3f, v[1].x, 1e-4f); Assert.AreEqual(Mathf.Cos(a0) * 3f, v[1].z, 1e-4f);
            Assert.AreEqual(Mathf.Sin(-a0) * 3f, v[17].x, 1e-4f);
            Assert.AreEqual(48, t.Length);
            for (int i = 0; i < 16; i++) { Assert.AreEqual(0, t[i * 3]); Assert.AreEqual(i + 2, t[i * 3 + 1]); Assert.AreEqual(i + 1, t[i * 3 + 2]); }
            Object.DestroyImmediate(m);
        }

        // ---- 灯りの揺れ ----

        [Test] public void 灯りの吊り点は表示を繰り返しても基準の角度がずれず_無ければ揺らさない()
        {
            var root = new GameObject("PhantomLanternTest");
            try
            {
                var pivot = new GameObject("LanternPivot");
                pivot.transform.SetParent(root.transform, false);
                pivot.transform.localRotation = Quaternion.Euler(10f, 20f, 0f);
                var view = root.AddComponent<PhantomView>();
                var so = new UnityEditor.SerializedObject(view);
                so.FindProperty("_lanternPivot").objectReferenceValue = pivot.transform;
                so.ApplyModifiedPropertiesWithoutUndo();
                void Call(string name) => typeof(PhantomView).GetMethod(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(view, null);
                Call("Awake"); Call("OnEnable");
                var baseRot = view.LanternBaseRotation;
                Assert.AreEqual(0f, Quaternion.Angle(Quaternion.Euler(10f, 20f, 0f), baseRot), 1e-3f);
                Assert.IsTrue(view.IsLanternSwinging);
                for (int i = 0; i < 5; i++)
                {
                    pivot.transform.localRotation = baseRot * Quaternion.Euler(4f, 0f, 0f);   // 揺れの途中で止まった想定
                    Call("OnDisable");
                    Assert.AreEqual(0f, Quaternion.Angle(baseRot, pivot.transform.localRotation), 1e-3f, "非表示で基準へ戻る");
                    Call("OnEnable");
                    Assert.AreEqual(0f, Quaternion.Angle(baseRot, view.LanternBaseRotation), 1e-3f, "基準はずれていかない");
                    Assert.IsTrue(view.IsLanternSwinging);
                    Assert.LessOrEqual(Quaternion.Angle(baseRot, pivot.transform.localRotation), 6.01f, "±6° の範囲");
                }
                Call("OnDisable");
                Assert.IsFalse(view.IsLanternSwinging);
            }
            finally { Object.DestroyImmediate(root); }

            var root2 = new GameObject("PhantomNoLantern");
            try
            {
                var view = root2.AddComponent<PhantomView>();
                void Call(string name) => typeof(PhantomView).GetMethod(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(view, null);
                Call("Awake"); Call("OnEnable");
                Assert.IsFalse(view.IsLanternSwinging, "吊り点が無ければ何もしない");
                Call("OnDisable");
            }
            finally { Object.DestroyImmediate(root2); }
        }

        [Test] public void 光る芯_glows_はConfigureで高さが変わらず色だけ目と同じに塗られる()
        {
            var root = new GameObject("PhantomGlowTest");
            try
            {
                Renderer Make(string n, float y) { var g = new GameObject(n); g.transform.SetParent(root.transform, false); g.transform.localPosition = new Vector3(0.1f, y, 0.2f); return g.AddComponent<MeshRenderer>(); }
                var eye = Make("Eye", 0.3f); var glow = Make("Glow", 0.4f);
                var view = root.AddComponent<PhantomView>();
                var so = new UnityEditor.SerializedObject(view);
                so.FindProperty("_eyes").arraySize = 1; so.FindProperty("_eyes").GetArrayElementAtIndex(0).objectReferenceValue = eye;
                so.FindProperty("_glows").arraySize = 1; so.FindProperty("_glows").GetArrayElementAtIndex(0).objectReferenceValue = glow;
                so.ApplyModifiedPropertiesWithoutUndo();
                view.Configure(3f, 35f, 1.5f);
                Assert.AreEqual(1.5f, eye.transform.localPosition.y, 1e-5f, "目は目の高さへ");
                Assert.AreEqual(0.4f, glow.transform.localPosition.y, 1e-5f, "芯の高さは変わらない");
                var mpb = new MaterialPropertyBlock();
                glow.GetPropertyBlock(mpb);
                Assert.AreEqual(0f, Vector4.Distance(PhantomView.EyeNormal, mpb.GetColor("_BaseColor")), 1e-4f);
                view.SetAlert(true);
                glow.GetPropertyBlock(mpb);
                Assert.AreEqual(0f, Vector4.Distance(PhantomView.EyeAlert, mpb.GetColor("_BaseColor")), 1e-4f);
                Assert.AreEqual(0f, Vector4.Distance(PhantomView.EyeAlert, mpb.GetColor("_Color")), 1e-4f);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test] public void model_がある構成では目の高さの変更でモデル全体が一緒に上下する()
        {
            var root = new GameObject("PhantomModelTest");
            try
            {
                var bodyRoot = new GameObject("BodyRoot"); bodyRoot.transform.SetParent(root.transform, false);
                var model = new GameObject("Model"); model.transform.SetParent(bodyRoot.transform, false);
                Transform Part(string n, Vector3 p) { var g = new GameObject(n); g.transform.SetParent(model.transform, false); g.transform.localPosition = p; g.AddComponent<MeshRenderer>(); return g.transform; }
                var eyeL = Part("EyeL", new Vector3(-0.043f, 1.5f, 0.11f)); var voidT = Part("Void", new Vector3(0f, 1.508f, 0.065f)); var glow = Part("Glow", new Vector3(0.24f, 1.96f, 0.8f));
                var view = root.AddComponent<PhantomView>();
                var so = new UnityEditor.SerializedObject(view);
                so.FindProperty("_bodyRoot").objectReferenceValue = bodyRoot.transform;
                so.FindProperty("_model").objectReferenceValue = model.transform;
                so.FindProperty("_eyes").arraySize = 1; so.FindProperty("_eyes").GetArrayElementAtIndex(0).objectReferenceValue = eyeL.GetComponent<Renderer>();
                so.FindProperty("_glows").arraySize = 1; so.FindProperty("_glows").GetArrayElementAtIndex(0).objectReferenceValue = glow.GetComponent<Renderer>();
                so.ApplyModifiedPropertiesWithoutUndo();
                Vector3 L(Transform t) => bodyRoot.transform.InverseTransformPoint(t.position);
                var e0 = L(eyeL); var v0 = L(voidT); var g0 = L(glow);
                view.Configure(3f, 35f, 1.5f);
                Assert.AreEqual(0f, (L(eyeL) - e0).magnitude, 1e-5f, "1.5 では何も動かない"); Assert.AreEqual(0f, (L(voidT) - v0).magnitude, 1e-5f); Assert.AreEqual(0f, (L(glow) - g0).magnitude, 1e-5f);
                view.Configure(3f, 35f, 1.6f);
                Assert.AreEqual(0.1f, L(eyeL).y - e0.y, 1e-4f, "目が 0.1 上がる"); Assert.AreEqual(0.1f, L(voidT).y - v0.y, 1e-4f, "闇も一緒に"); Assert.AreEqual(0.1f, L(glow).y - g0.y, 1e-4f, "灯りも一緒に");
                Assert.AreEqual(e0.x, L(eyeL).x, 1e-5f); Assert.AreEqual(e0.z, L(eyeL).z, 1e-5f);
                Assert.AreEqual(1.6f, L(eyeL).y, 1e-4f);
                view.Configure(3f, 35f, 1.5f);   // 繰り返し呼んでも基準からの差で決まる（積み上がらない）
                Assert.AreEqual(0f, (L(eyeL) - e0).magnitude, 1e-5f);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test] public void 目の高さは回転した親の下でも体の根元から見た高さで合う()
        {
            var root = new GameObject("PhantomEyeFrameTest");
            try
            {
                var bodyRoot = new GameObject("BodyRoot"); bodyRoot.transform.SetParent(root.transform, false);
                var model = new GameObject("Model"); model.transform.SetParent(bodyRoot.transform, false);
                model.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // 回転した親の場合
                var eye = new GameObject("Eye"); eye.transform.SetParent(model.transform, false);
                eye.transform.localPosition = new Vector3(0.043f, -0.11f, -1.4f);   // 取り込んだ後の親の座標系
                eye.AddComponent<MeshRenderer>();
                var view = root.AddComponent<PhantomView>();
                var so = new UnityEditor.SerializedObject(view);
                so.FindProperty("_bodyRoot").objectReferenceValue = bodyRoot.transform;
                so.FindProperty("_eyes").arraySize = 1; so.FindProperty("_eyes").GetArrayElementAtIndex(0).objectReferenceValue = eye.GetComponent<Renderer>();
                so.ApplyModifiedPropertiesWithoutUndo();
                var before = bodyRoot.transform.InverseTransformPoint(eye.transform.position);
                view.Configure(3f, 35f, 1.5f);
                var after = bodyRoot.transform.InverseTransformPoint(eye.transform.position);
                Assert.AreEqual(1.5f, after.y, 1e-4f);
                Assert.AreEqual(before.x, after.x, 1e-4f); Assert.AreEqual(before.z, after.z, 1e-4f);
            }
            finally { Object.DestroyImmediate(root); }
        }

        // ---- オープニングの長さ ----

        static ThProgressParams Progress() => new ThProgressParams { openingSeconds = 10f, phantomOpeningExtraSeconds = 4f, practiceSeconds = 30f, playingSeconds = 180f, resultSeconds = 15f };

        [Test] public void オープニングの長さは亡霊なしで10秒_ありで14秒()
        {
            var p = Progress();
            Assert.AreEqual(10f, ThOpeningTime.TotalSeconds(p, ThPhantomMode.Off));
            Assert.AreEqual(14f, ThOpeningTime.TotalSeconds(p, ThPhantomMode.TakeTreasure));
            Assert.AreEqual(14f, ThOpeningTime.TotalSeconds(p, ThPhantomMode.PlayerOut));
            Assert.AreEqual(0f, ThOpeningTime.ExtraSeconds(p, ThPhantomMode.Off));
            Assert.AreEqual(4f, new ThProgressParams().phantomOpeningExtraSeconds, "初期値は 4 秒");
        }

        static HostRoundMachine NewMachine()
        {
            var reg = new DeviceRegistry { HostParamsVersion = "v", AutoConfirm = true };
            reg.Connect("A", 2); reg.ReportParamsVersion("A", "v"); reg.ReportCatalogHash("A", "");
            return new HostRoundMachine(reg, Progress());
        }

        [Test] public void ホストのOpeningの長さは亡霊を入れる回だけ延びる()
        {
            var off = NewMachine();
            Assert.IsTrue(off.TryStart(0, 1000, 1));
            Assert.AreEqual(10f, off.RemainingSeconds(0), 1e-4f);
            off.Tick(10.1);
            Assert.AreEqual(RoundState.Tutorial, off.State);

            foreach (var mode in new[] { ThPhantomMode.TakeTreasure, ThPhantomMode.PlayerOut })
            {
                var on = NewMachine();
                Assert.IsTrue(on.TryStart(0, 1000, 1, mode));
                Assert.AreEqual(14f, on.RemainingSeconds(0), 1e-4f);
                on.Tick(10.1);
                Assert.AreEqual(RoundState.Opening, on.State, "10 秒では終わらない");
                on.Tick(14.1);
                Assert.AreEqual(RoundState.Tutorial, on.State);
            }
        }

        [Test] public void ホストと端末は同じ式で同じ長さを出す()
        {
            var p = Progress();
            foreach (ThPhantomMode mode in System.Enum.GetValues(typeof(ThPhantomMode)))
            {
                var m = NewMachine();
                m.TryStart(0, 1000, 1, mode);
                // 端末は ThState の settings.phantom と自分の ThParams から計算する。ホストの残り秒と一致する
                var tracker = new ClientRoundTracker("me", p, 0);
                tracker.OnState(1000, new ThStateMsg { seq = 1, state = (int)RoundState.Opening, remaining = m.RemainingSeconds(0), participants = new[] { "me" }, settings = new ThGameSettings { phantom = mode }, paramsVersion = "v" }, 0);
                Assert.AreEqual(mode, tracker.Settings.phantom);
                Assert.AreEqual(ThOpeningTime.TotalSeconds(p, tracker.Settings.phantom), tracker.RemainingSeconds.CurrentValue, 1e-3f, mode.ToString());
            }
        }

        // ---- 字幕 ----

        [Test] public void 字幕の番号_亡霊なしは今までと同じ()
        {
            var expect = new[] { (10f, 1), (8.1f, 1), (7.9f, 2), (5.9f, 3), (3.9f, 4), (1.9f, 5), (0f, 5) };
            foreach (var (rem, line) in expect)
            {
                Assert.AreEqual(line, ThPanelPresenter.OpeningLineIndex(rem, 10), rem.ToString());
                Assert.AreEqual(line, ThPanelPresenter.OpeningLineIndex(rem, 10, 0f), rem.ToString());
            }
        }

        [Test] public void 字幕の番号_亡霊ありは0から10秒で1から5_10から14秒で6()
        {
            // 残り秒は全体 14 秒から数える（経過 = 14 - 残り）
            var expect = new[] { (14f, 1), (12.1f, 1), (11.9f, 2), (9.9f, 3), (7.9f, 4), (5.9f, 5), (4.1f, 5), (3.9f, 6), (1f, 6), (0f, 6) };
            foreach (var (rem, line) in expect) Assert.AreEqual(line, ThPanelPresenter.OpeningLineIndex(rem, 10, 4f), rem.ToString());
        }

        static ThPanelInput OpeningIn(float remaining, float extra) => new ThPanelInput { State = RoundState.Opening, RoundId = 5, Remaining = remaining, OpeningSeconds = 10f, OpeningExtraSeconds = extra };

        [Test] public void 字幕の合図は亡霊ありで6枚_なしで5枚()
        {
            var on = new ThPanelPresenter(); var started = new List<int>();
            foreach (var rem in new[] { 14f, 12f, 9.9f, 7.9f, 5.9f, 3.9f, 1f }) { var f = on.Update(OpeningIn(rem, 4f), 0); if (f.LineStarted > 0) started.Add(f.LineStarted); }
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6 }, started);
            var off = new ThPanelPresenter(); started.Clear();
            foreach (var rem in new[] { 10f, 7.9f, 5.9f, 3.9f, 1.9f, 0.5f }) { var f = off.Update(OpeningIn(rem, 0f), 0); if (f.LineStarted > 0) started.Add(f.LineStarted); }
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, started);
        }

        [Test] public void 字幕6の文字と音はOpeningLine6を引く()
        {
            var t = ScriptableObject.CreateInstance<ThTextTable>();
            try
            {
                Assert.AreEqual("王の墓守が、今も灯りを手に見回っている。灯りに照らされるなよ。", ThPanelPresenter.Compose(new ThPanelFrame { Kind = ThPanelKind.Opening, OpeningLine = 6 }, t).Main);
                Assert.AreEqual(t.Get(ThTextId.OpeningLine5), ThPanelPresenter.Compose(new ThPanelFrame { Kind = ThPanelKind.Opening, OpeningLine = 5 }, t).Main);
            }
            finally { Object.DestroyImmediate(t); }
            Assert.AreEqual(ThSoundId.OpeningLine6, ThPanelPresenter.LineSound(6));
            Assert.AreEqual(ThSoundId.OpeningLine5, ThPanelPresenter.LineSound(5));
        }

        [Test] public void 正面のロゴは亡霊ありでも始まった瞬間に出る()
        {
            var p = Progress();
            foreach (ThPhantomMode mode in System.Enum.GetValues(typeof(ThPhantomMode)))
            {
                var l = new ThTitleLogoCueLogic();
                l.OnState(RoundState.Waiting, 0f, 0f);
                var a = l.OnState(RoundState.Opening, ThOpeningTime.TotalSeconds(p, mode) - 0.2f, ThOpeningTime.TotalSeconds(p, mode));
                Assert.IsTrue(a.PlayCue, mode.ToString());
            }
        }

        // ---- FBX（取り込み後のモデル。軸と法線） ----

        const string ModelDir = "Assets/_Content/TreasureHaul/Models/Enemies/";

        static Mesh LoadMesh(string fbx, string node)
        {
            var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(ModelDir + fbx + ".fbx");
            Assert.IsNotNull(go, fbx);
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true)) if (mf.name == node) return mf.sharedMesh;
            Assert.Fail("mesh not found: " + node); return null;
        }

        // z が両端の 3% 以内の頂点の法線の平均
        static void EndNormalsZ(Mesh m, out Vector3 front, out Vector3 back)
        {
            var v = m.vertices; var n = m.normals;
            float zmin = float.MaxValue, zmax = float.MinValue;
            foreach (var p in v) { zmin = Mathf.Min(zmin, p.z); zmax = Mathf.Max(zmax, p.z); }
            float band = (zmax - zmin) * 0.03f;
            front = Vector3.zero; back = Vector3.zero; int fc = 0, bc = 0;
            for (int i = 0; i < v.Length; i++)
            {
                if (v[i].z > zmax - band) { front += n[i]; fc++; }
                if (v[i].z < zmin + band) { back += n[i]; bc++; }
            }
            front /= Mathf.Max(1, fc); back /= Mathf.Max(1, bc);
        }

        [Test] public void 石像のメッシュは正面が_Z側で法線も外向き()
        {
            foreach (var node in new[] { "KingStatue_Body", "KingStatue_Head" })
            {
                var m = LoadMesh("KingStatue", node);
                EndNormalsZ(m, out var front, out var back);
                Assert.Greater(front.z, 0.2f, node + " 正面（+Z 側）の法線は +Z を向く");
                Assert.Less(back.z, -0.2f, node + " 背面（−Z 側）の法線は −Z を向く");
                Assert.AreEqual(0f, m.bounds.min.y, 0.05f, node.EndsWith("Body") ? "原点は床（Y 上）" : "頭は首の原点まわり");
            }
        }

        [Test] public void 亡霊のメッシュはY上で背面の法線が_Zを向き裾の外側の法線も外向き()
        {
            var m = LoadMesh("GraveKeeper", "GraveKeeper_Body");
            Assert.AreEqual(1.99f, m.bounds.max.y, 0.05f, "高さは Y 軸");
            EndNormalsZ(m, out _, out var back);
            Assert.Less(back.z, -0.3f, "背面の法線は −Z");
            var v = m.vertices; var n = m.normals; float fz = 0, rx = 0, lx = 0; int fc = 0, rc = 0, lc = 0;
            for (int i = 0; i < v.Length; i++)
            {
                if (v[i].y < 0.3f || v[i].y > 1.0f) continue;   // 裾の部分（竿と袖を除く）
                if (v[i].z > 0.15f) { fz += n[i].z; fc++; }
                if (v[i].x > 0.15f) { rx += n[i].x; rc++; }
                if (v[i].x < -0.15f) { lx += n[i].x; lc++; }
            }
            Assert.Greater(fz / fc, 0.1f, "前側の裾の法線は +Z 寄り");
            Assert.Greater(rx / rc, 0.2f, "右側の裾の法線は +X 寄り");
            Assert.Less(lx / lc, -0.2f, "左側の裾の法線は −X 寄り");
        }

        [Test] public void 取り込んだ亡霊と石像の節点は回転0で位置が_stats_jsonの値と一致する()
        {
            var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(ModelDir + "GraveKeeper.fbx");
            Transform Find(GameObject g, string n) { foreach (var t in g.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t; Assert.Fail(n); return null; }
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, t.localRotation), 1e-3f, t.name);
            Assert.AreEqual(new Vector3(-0.043f, 1.5f, 0.11f).x, Find(go, "GraveKeeper_Eye_L").position.x, 1e-3f);
            Assert.AreEqual(1.5f, Find(go, "GraveKeeper_Eye_L").position.y, 1e-3f);
            Assert.AreEqual(0.11f, Find(go, "GraveKeeper_Eye_L").position.z, 1e-3f);
            var p = Find(go, "GraveKeeper_LanternPivot").position;
            Assert.AreEqual(0f, (p - new Vector3(0.24f, 1.96f, 0.8f)).magnitude, 2e-3f);
            var st = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(ModelDir + "KingStatue.fbx");
            foreach (var t in st.GetComponentsInChildren<Transform>(true)) Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, t.localRotation), 1e-3f, t.name);
            Assert.AreEqual(0f, (Find(st, "KingStatue_HeadPivot").position - new Vector3(0f, 2.59f, 0.01f)).magnitude, 2e-3f);
        }

        // ---- 列挙・文言 ----

        [Test] public void 列挙の既存の番号は変わらず新しい項目は末尾()
        {
            Assert.AreEqual(31, (int)ThTextId.OpeningLine1); Assert.AreEqual(35, (int)ThTextId.OpeningLine5);
            Assert.AreEqual(79, (int)ThTextId.PhantomNoticeTake); Assert.AreEqual(80, (int)ThTextId.PhantomNoticeOut);
            Assert.AreEqual(81, (int)ThTextId.PhantomOutSelf); Assert.AreEqual(82, (int)ThTextId.PhantomOutPeerFormat);
            Assert.AreEqual(87, (int)ThTextId.PhantomTakenSelf);
            Assert.AreEqual(88, (int)ThTextId.OpeningLine6);
            Assert.AreEqual(5, (int)ThSoundId.OpeningLine1); Assert.AreEqual(9, (int)ThSoundId.OpeningLine5);
            Assert.AreEqual(10, (int)ThSoundId.TargetReached); Assert.AreEqual(11, (int)ThSoundId.PhantomWarning);
            Assert.AreEqual(12, (int)ThSoundId.OpeningLine6);
        }

        [Test] public void 文言表に新しい文言がある()
        {
            var t = ScriptableObject.CreateInstance<ThTextTable>();
            try
            {
                Assert.AreEqual("王の墓守が、今も灯りを手に見回っている。灯りに照らされるなよ。", t.Get(ThTextId.OpeningLine6));
                Assert.AreEqual("墓守の灯りに照らされると、持っているお宝を取り上げられる。灯りに入らないで", t.Get(ThTextId.PhantomNoticeTake));
                Assert.AreEqual("墓守の灯りに照らされると、その回はここまで。灯りに入らないで", t.Get(ThTextId.PhantomNoticeOut));
                Assert.AreEqual("墓守に見つかった…\nこの回はここまで", t.Get(ThTextId.PhantomOutSelf));
                Assert.AreEqual("A が墓守に見つかった", t.Format(ThTextId.PhantomOutPeerFormat, "A"));
                Assert.AreEqual("墓守にお宝を取り上げられた！", t.Get(ThTextId.PhantomTakenSelf));
                Assert.AreEqual("亡霊", t.Get(ThTextId.OpPhantomLabel), "運営UIの「亡霊」は変えない");
            }
            finally { Object.DestroyImmediate(t); }
        }

        [Test] public void 実際の文言表と音の一覧は検証を通る()
        {
            var t = UnityEditor.AssetDatabase.LoadAssetAtPath<ThTextTable>("Assets/_Content/TreasureHaul/Settings/ThTextTable.asset");
            Assert.IsNotNull(t);
            CollectionAssert.IsEmpty(t.Validate());
            Assert.AreEqual("王の墓守が、今も灯りを手に見回っている。灯りに照らされるなよ。", t.Get(ThTextId.OpeningLine6));
            var a = UnityEditor.AssetDatabase.LoadAssetAtPath<ThAudioCatalog>("Assets/_Content/TreasureHaul/Settings/ThAudioCatalog.asset");
            if (a != null) CollectionAssert.IsEmpty(a.Validate());
        }
    }
}
