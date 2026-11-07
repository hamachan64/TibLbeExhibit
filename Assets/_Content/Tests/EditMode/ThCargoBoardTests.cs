using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;


namespace STYLY.TIB_Template.TreasureHaul.Tests
{
    public class ThCargoBoardTests
    {
        // a=1 b=2 c=4 d=8 e=16 f=32 g=64
        [TestCase('0', 0b0111111)]
        [TestCase('1', 0b0000110)]
        [TestCase('2', 0b1011011)]
        [TestCase('3', 0b1001111)]
        [TestCase('4', 0b1100110)]
        [TestCase('5', 0b1101101)]
        [TestCase('6', 0b1111101)]
        [TestCase('7', 0b0000111)]
        [TestCase('8', 0b1111111)]
        [TestCase('9', 0b1101111)]
        [TestCase('G', 0b0111101)]
        [TestCase(' ', 0)]
        public void 文字のセグメントの表(char c, int expected) => Assert.AreEqual(expected, SevenSegment.Mask(c));

        [TestCase(0, "    0")]
        [TestCase(7, "    7")]
        [TestCase(150, "  150")]
        [TestCase(1250, " 1250")]
        [TestCase(99999, "99999")]
        [TestCase(100000, "99999")]
        [TestCase(-5, "    0")]
        public void 桁分けは右詰めで上の桁の0は消灯(int total, string expected)
            => Assert.AreEqual(expected, new string(SevenSegment.Digits(total)));

        [TestCase(0, false)]
        [TestCase(999, false)]
        [TestCase(1000, true)]
        [TestCase(1250, true)]
        [TestCase(99999, true)]
        [TestCase(100000, true)]
        [TestCase(-5, false)]
        public void 千の位の区切りは1000以上で点灯(int total, bool lit) => Assert.AreEqual(lit, SevenSegment.CommaLit(total));

        [TestCase(-5, 0)]
        [TestCase(0, 0)]
        [TestCase(99999, 99999)]
        [TestCase(100000, 99999)]
        public void 頭打ちと負の数(int total, int expected) => Assert.AreEqual(expected, SevenSegment.Clamp(total));

        [Test] public void 増えたときだけ数え上げる()
        {
            Assert.IsTrue(CargoBoardView.ShouldCountUp(true, true, 100, 400));
        }

        [Test] public void 減ったとき_同じ額_最初の値_非表示ではすぐその額()
        {
            Assert.IsFalse(CargoBoardView.ShouldCountUp(true, true, 400, 100), "減った");
            Assert.IsFalse(CargoBoardView.ShouldCountUp(true, true, 400, 400), "同じ");
            Assert.IsFalse(CargoBoardView.ShouldCountUp(true, false, 0, 400), "最初の値");
            Assert.IsFalse(CargoBoardView.ShouldCountUp(false, true, 100, 400), "非表示（再表示の前）");
        }

        [Test] public void 頭打ちを超える増加は数え上げない()
        {
            Assert.IsFalse(CargoBoardView.ShouldCountUp(true, true, 99999, 120000));
        }

        [Test] public void 七セグのメッシュは正面から見て表向きで左から右へ読める()
        {
            var go = new GameObject("SegTest", typeof(SevenSegmentDisplay));
            try
            {
                var d = go.GetComponent<SevenSegmentDisplay>();
                d.Show(1250);
                var mesh = go.GetComponent<MeshFilter>().sharedMesh;
                mesh.RecalculateNormals();
                foreach (var n in mesh.normals) Assert.Greater(n.z, 0.99f, "三角形が +Z 側から見て表（Unity は時計回りが表）");
                // 最初の桁（0 番）は見る人の左 = +X 側、G（5 番）は右 = -X 側。1 セルは 7 セグメント × 6 頂点
                var v = mesh.vertices;
                float Cx(int cell) { float s = 0; for (int i = 0; i < 42; i++) s += v[cell * 42 + i].x; return s / 42f; }
                Assert.Greater(Cx(0), Cx(1)); Assert.Greater(Cx(4), Cx(5), "G は右端（-X 側）");
                // G の内向きの横棒（C と見分ける）は最後の 6 頂点で、常に点灯
                var cols = new System.Collections.Generic.List<Color32>();
                mesh.GetColors(cols);
                for (int i = v.Length - 6; i < v.Length; i++) Assert.AreEqual(255, cols[i].r, "G の横棒は点灯");
            }
            finally
            {
                // エディタでは OnDestroy が呼ばれないので、作ったメッシュもここで消す
                var m = go.GetComponent<MeshFilter>().sharedMesh;
                if (m != null) Object.DestroyImmediate(m);
                Object.DestroyImmediate(go);
            }
        }

        // ---- プレハブとシーン ----
        const string PrefabPath = "Assets/_Content/TreasureHaul/Prefabs/TH_CargoBoard.prefab";
        const string EnvScenePath = "Assets/_Content/TreasureHaul/Scenes/TreasureHaulEnvironment.unity";

        [Test] public void プレハブは全て結線されコライダーが無く7セグは正面を向く()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab);
            var view = prefab.GetComponent<CargoBoardView>();
            Assert.IsNotNull(view, "ルートに CargoBoardView");
            Assert.AreEqual(Quaternion.identity, prefab.transform.localRotation, "ルートの回転は 0");
            var so = new SerializedObject(view);
            foreach (var n in new[] { "_display", "_lamp", "_label", "_visual" })
                Assert.IsNotNull(so.FindProperty(n).objectReferenceValue, n + " が未結線");
            Assert.IsEmpty(prefab.GetComponentsInChildren<Collider>(true), "当たり判定を付けない");
            var disp = (SevenSegmentDisplay)so.FindProperty("_display").objectReferenceValue;
            Assert.AreEqual(Vector3.forward, disp.transform.forward, "7 セグは +Z 向き");
            var visual = (GameObject)so.FindProperty("_visual").objectReferenceValue;
            Assert.IsTrue(disp.transform.IsChildOf(visual.transform));
            var lamp = (Renderer)so.FindProperty("_lamp").objectReferenceValue;
            Assert.AreEqual("TH_CargoBoardLamp", lamp.sharedMaterial.name);
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, disp.GetComponent<MeshRenderer>().shadowCastingMode);
            var label = (TMP_Text)so.FindProperty("_label").objectReferenceValue;
            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, label.GetComponent<Renderer>().shadowCastingMode);
            // TMP は自分の -Z 側から読めるので、+Z 側の体験者に向けて -Z 向き（鏡文字にしない）
            Assert.AreEqual(-1f, label.transform.forward.z, 1e-4f, "札の文字は体験者（+Z 側）から読める向き");
            // 回収したお宝やステージの電球と追加ライトを取り合わないよう、表示板は灯りを持たない
            Assert.IsEmpty(prefab.GetComponentsInChildren<Light>(true), "表示板に灯りを付けない");
            // 平行光源に背を向けても札と塗装が見えるよう、箱はベースマップでうっすら自己発光する
            var bodyMat = prefab.transform.Find("Visual/Model/CargoBoard_Body").GetComponent<Renderer>().sharedMaterial;
            Assert.IsTrue(bodyMat.IsKeywordEnabled("_EMISSION"), "箱の材質は自己発光あり");
            var screen = prefab.transform.Find("Visual/Model/CargoBoard_Screen");
            Assert.IsNotNull(screen);
            Assert.AreEqual(0f, screen.position.magnitude, 1e-4f, "ルートの原点は画面の中心");
            Assert.AreEqual(-1.5f, prefab.transform.Find("Visual/Model").position.y, 1e-4f, "モデルの足元はルートの 1.5m 下（置いたとき y = 0）");
        }

        [Test] public void Environmentシーンの表示板はこのプレハブのインスタンスでThVenueに結線されている()
        {
            var existing = SceneManager.GetSceneByPath(EnvScenePath);
            bool opened = !existing.isLoaded;
            var scene = existing.isLoaded ? existing : EditorSceneManager.OpenScene(EnvScenePath, OpenSceneMode.Additive);
            try
            {
                ThVenue venue = null;
                foreach (var go in scene.GetRootGameObjects()) { venue = go.GetComponentInChildren<ThVenue>(true); if (venue != null) break; }
                Assert.IsNotNull(venue);
                var board = venue.Board;
                Assert.IsNotNull(board, "ThVenue._board");
                Assert.AreSame(venue.Root, board.transform.parent, "VenueRoot 直下");
                Assert.AreEqual("CargoBoard", board.name);
                Assert.AreEqual(Quaternion.identity, board.transform.localRotation);
                Assert.AreEqual(0f, Vector3.Distance(new Vector3(0f, 1.5f, 0.32f), board.transform.localPosition), 1e-4f);
                var src = PrefabUtility.GetCorrespondingObjectFromSource(board.gameObject);
                Assert.AreEqual(PrefabPath, AssetDatabase.GetAssetPath(src), "TH_CargoBoard.prefab のインスタンス");
                int n = 0; foreach (Transform c in venue.Root) if (c.name == "CargoBoard") n++;
                Assert.AreEqual(1, n, "旧い CargoBoard が残っていない");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
