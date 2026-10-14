using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace STYLY.TIB_Template.GIPoker.Editor
{
    /// <summary>
    /// MRステージ「島の侵蝕」基盤（Issue #7）の統合用セットアップメニュー。
    ///
    /// シリアライズ済みアセット（.mat / .unity）の YAML 直接編集を避けるため、
    /// マテリアル生成・オクルーダー適用・ステージルート構築はすべて
    /// このメニューコマンド（エディタAPI）経由で行う（.claude/rules/unity_asset_rules.md）。
    ///
    /// 実行順:
    /// 1. GIPoker/Stage/Setup Foundation Materials …… テクスチャ・マテリアルを生成
    /// 2. （Environment.unity を開いて）GIPoker/Stage/Apply Occluder To TIB Mesh
    /// 3. （Environment.unity をアクティブにして）GIPoker/Stage/Create Stage Root
    /// </summary>
    public static class StageFoundationSetup
    {
        private const string StageFolder = "Assets/_Content/GIPoker/Materials/Stage";

        private const string ErosionShaderName = "GIPoker/Stage/ErosionPatch";
        private const string LeyLineShaderName = "GIPoker/Stage/LeyLine";
        private const string OccluderShaderName = "TIB/PassthroughOccluder";
        private const string ShadowCatcherShaderName = "TIB/ShadowCatcher";

        private const string ErosionMatPath = StageFolder + "/GIP_ErosionPatch.mat";
        private const string LeyLineMatPath = StageFolder + "/GIP_LeyLine.mat";
        private const string OccluderMatPath = "Assets/_Content/Materials/Common/PassthroughOccluder.mat";
        private const string ShadowCatcherMatPath = StageFolder + "/GIP_ShadowCatcher.mat";

        private const string NoiseTexPath = StageFolder + "/GIP_StageNoise.asset";
        private const string RuneTexPath = StageFolder + "/GIP_RuneCircle.asset";
        private const string FlowTexPath = StageFolder + "/GIP_LeyFlow.asset";

        private const string TibMeshRootName = "TIB_20260429";
        private const string StageRootName = "GIPoker_Stage";

        // ------------------------------------------------------------
        // 1. テクスチャ・マテリアル生成
        // ------------------------------------------------------------

        [MenuItem("GIPoker/Stage/Setup Foundation Materials")]
        public static void SetupFoundationMaterials()
        {
            EnsureFolder(StageFolder);

            // プロシージャルテクスチャ（外部アセット不要で再現可能にする）
            var noiseTex = EnsureTexture(NoiseTexPath, 128, 128, GenerateNoisePixel);
            var runeTex = EnsureTexture(RuneTexPath, 256, 256, GenerateRunePixel);
            var flowTex = EnsureTexture(FlowTexPath, 128, 16, GenerateFlowPixel);

            // 侵蝕パッチ
            var erosion = EnsureMaterial(ErosionMatPath, ErosionShaderName);
            if (erosion != null)
            {
                erosion.SetTexture("_NoiseTex", noiseTex);
                erosion.SetTexture("_RuneTex", runeTex);
                erosion.SetColor("_GrassColorA", new Color(0.28f, 0.52f, 0.25f));
                erosion.SetColor("_GrassColorB", new Color(0.14f, 0.33f, 0.16f));
                erosion.SetColor("_RuneColor", new Color(0.45f, 0.95f, 0.65f));
                erosion.SetColor("_EdgeColor", new Color(0.5f, 0.9f, 0.6f));
                erosion.SetColor("_GoldColor", new Color(1.0f, 0.76f, 0.32f));
                EditorUtility.SetDirty(erosion);
            }

            // レイライン
            var leyLine = EnsureMaterial(LeyLineMatPath, LeyLineShaderName);
            if (leyLine != null)
            {
                leyLine.SetTexture("_FlowTex", flowTex);
                leyLine.SetColor("_LineColor", new Color(0.4f, 0.9f, 0.65f));
                leyLine.SetColor("_GoldColor", new Color(1.0f, 0.76f, 0.32f));
                EditorUtility.SetDirty(leyLine);
            }

            // オクルーダー（depth-only。プロパティはCullのみ）
            EnsureMaterial(OccluderMatPath, OccluderShaderName);

            // Shadow Catcher
            var shadowCatcher = EnsureMaterial(ShadowCatcherMatPath, ShadowCatcherShaderName);
            if (shadowCatcher != null)
            {
                shadowCatcher.SetColor("_ShadowColor", Color.black);
                shadowCatcher.SetFloat("_ShadowStrength", 0.65f);
                EditorUtility.SetDirty(shadowCatcher);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[StageFoundationSetup] ステージ基盤マテリアルを {StageFolder} に生成・更新しました");
        }

        // ------------------------------------------------------------
        // 2. TIBメッシュへのオクルーダー適用
        // ------------------------------------------------------------

        [MenuItem("GIPoker/Stage/Apply Occluder To TIB Mesh")]
        public static void ApplyOccluderToTibMesh()
        {
            var occluder = AssetDatabase.LoadAssetAtPath<Material>(OccluderMatPath);
            if (occluder == null)
            {
                Debug.LogError("[StageFoundationSetup] オクルーダーマテリアルがありません。先に GIPoker/Stage/Setup Foundation Materials を実行してください");
                return;
            }

            var tibRoot = FindInLoadedScenes(TibMeshRootName);
            if (tibRoot == null)
            {
                Debug.LogError($"[StageFoundationSetup] {TibMeshRootName} が見つかりません。Environment.unity を開いてから実行してください");
                return;
            }

            var renderers = tibRoot.GetComponentsInChildren<MeshRenderer>(true);
            var count = 0;
            foreach (var renderer in renderers)
            {
                Undo.RecordObject(renderer, "Apply Passthrough Occluder");
                var materials = renderer.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                {
                    materials[i] = occluder;
                }
                renderer.sharedMaterials = materials;
                // 現実メッシュが仮想の影を落とす／受けると二重になるため無効化する
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                EditorUtility.SetDirty(renderer);
                count++;
            }

            EditorSceneManager.MarkSceneDirty(tibRoot.scene);
            Debug.Log($"[StageFoundationSetup] {TibMeshRootName} 配下の {count} 個のレンダラーにオクルーダーを適用しました（シーンを保存してください）");
        }

        // ------------------------------------------------------------
        // 3. ステージルート（StageDirector＋検証用サンプル）構築
        // ------------------------------------------------------------

        [MenuItem("GIPoker/Stage/Create Stage Root")]
        public static void CreateStageRoot()
        {
            if (FindInLoadedScenes(StageRootName) != null)
            {
                Debug.LogWarning($"[StageFoundationSetup] {StageRootName} は既に存在します（多重生成を防ぐため中断）");
                return;
            }

            var erosionMat = AssetDatabase.LoadAssetAtPath<Material>(ErosionMatPath);
            var leyLineMat = AssetDatabase.LoadAssetAtPath<Material>(LeyLineMatPath);
            var shadowMat = AssetDatabase.LoadAssetAtPath<Material>(ShadowCatcherMatPath);
            if (erosionMat == null || leyLineMat == null || shadowMat == null)
            {
                Debug.LogError("[StageFoundationSetup] マテリアルが不足しています。先に GIPoker/Stage/Setup Foundation Materials を実行してください");
                return;
            }

            var root = new GameObject(StageRootName);
            Undo.RegisterCreatedObjectUndo(root, "Create GIPoker Stage Root");

            // Environment シーンがロードされていればそこへ移動する（StageDirector は Environment 常駐）
            var environmentScene = FindLoadedScene("Environment");
            if (environmentScene.IsValid())
            {
                SceneManager.MoveGameObjectToScene(root, environmentScene);
            }

            // プレイエリア中心（CenterPosition）があれば位置を合わせる
            var center = FindInLoadedScenes("CenterPosition");
            root.transform.position = center != null ? center.transform.position : Vector3.zero;

            root.AddComponent<StageDirector>();

            // Shadow Catcher: 床全域の影受け（床面よりわずかに浮かせて z-fight 回避）
            CreateFloorQuad(root.transform, "ShadowCatcher_Floor", shadowMat,
                new Vector3(0f, 0.005f, 0f), new Vector3(6f, 6f, 1f));

            // 検証用サンプル（実配置は Issue #2 が行う。不要になったら削除してよい）
            CreateFloorQuad(root.transform, "ErosionPatch_Sample", erosionMat,
                new Vector3(0f, 0.01f, 0f), new Vector3(3f, 3f, 1f));
            CreateFloorQuad(root.transform, "LeyLine_Sample", leyLineMat,
                new Vector3(1.5f, 0.008f, 0.8f), new Vector3(3f, 0.3f, 1f));

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log($"[StageFoundationSetup] {StageRootName} をシーン '{root.scene.name}' に作成しました（シーンを保存してください）");
        }

        /// <summary>床に沿った Quad（上向き）を生成してマテリアルを割り当てる</summary>
        private static void CreateFloorQuad(Transform parent, string name, Material material,
            Vector3 localPosition, Vector3 localScale)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = localPosition;
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // 法線を上向きに
            quad.transform.localScale = localScale;
            quad.GetComponent<MeshRenderer>().sharedMaterial = material;
            // 演出用クアッドに当たり判定は不要
            var collider = quad.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }
        }

        // ------------------------------------------------------------
        // 共通ヘルパー
        // ------------------------------------------------------------

        /// <summary>"Assets/..." 形式のフォルダを親から順に作成する</summary>
        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }
            var slash = folder.LastIndexOf('/');
            var parent = folder.Substring(0, slash);
            var leaf = folder.Substring(slash + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        /// <summary>マテリアルを生成（既存ならシェーダーだけ張り替えて再利用し、GUIDを保つ）</summary>
        private static Material EnsureMaterial(string path, string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"[StageFoundationSetup] シェーダー '{shaderName}' が見つかりません（コンパイルエラーの可能性。Console を確認してください）");
                return null;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
                Debug.Log($"[StageFoundationSetup] 生成: {path}");
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
                EditorUtility.SetDirty(material);
            }
            return material;
        }

        /// <summary>
        /// プロシージャルテクスチャを生成して .asset として保存する
        /// （既存ならそのまま再利用し、GUID・参照を保つ）。
        /// </summary>
        private static Texture2D EnsureTexture(string path, int width, int height,
            System.Func<float, float, float> pixelFunc)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
            {
                return existing;
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true, true)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var value = Mathf.Clamp01(pixelFunc((x + 0.5f) / width, (y + 0.5f) / height));
                    pixels[y * width + x] = new Color(value, value, value, 1f);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(true, false);
            AssetDatabase.CreateAsset(texture, path);
            Debug.Log($"[StageFoundationSetup] 生成: {path}");
            return texture;
        }

        /// <summary>ロード済み全シーンから名前一致（部分一致）のGameObjectを探す（非アクティブ含む）</summary>
        private static GameObject FindInLoadedScenes(string name)
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }
                foreach (var rootObject in scene.GetRootGameObjects())
                {
                    foreach (var transform in rootObject.GetComponentsInChildren<Transform>(true))
                    {
                        if (transform.name.Contains(name))
                        {
                            return transform.gameObject;
                        }
                    }
                }
            }
            return null;
        }

        private static Scene FindLoadedScene(string sceneName)
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && scene.name == sceneName)
                {
                    return scene;
                }
            }
            return default;
        }

        // ------------------------------------------------------------
        // プロシージャルテクスチャ生成関数（すべてタイル可能）
        // ------------------------------------------------------------

        /// <summary>侵蝕縁の揺らぎ用: 3オクターブのタイル可能バリューノイズ</summary>
        private static float GenerateNoisePixel(float u, float v)
        {
            var value = 0.5f * ValueNoise(u, v, 4, 1)
                        + 0.3f * ValueNoise(u, v, 8, 2)
                        + 0.2f * ValueNoise(u, v, 16, 3);
            return value;
        }

        /// <summary>ルーン模様用: 同心円＋破線リング＋放射スポークの魔法陣風パターン</summary>
        private static float GenerateRunePixel(float u, float v)
        {
            var dx = (u - 0.5f) * 2f;
            var dy = (v - 0.5f) * 2f;
            var r = Mathf.Sqrt(dx * dx + dy * dy); // 1.0 = 内接円の縁
            var angle = Mathf.Atan2(dy, dx);

            float Ring(float target, float width)
            {
                return Mathf.Clamp01(1f - Mathf.Abs(r - target) / width);
            }

            var value = 0f;
            value = Mathf.Max(value, Ring(0.86f, 0.02f));          // 外周円
            value = Mathf.Max(value, Ring(0.78f, 0.012f));         // 二重線

            // 破線リング（24分割で明滅）
            var dashes = Mathf.Repeat(angle / (2f * Mathf.PI) * 24f, 1f) < 0.55f ? 1f : 0f;
            value = Mathf.Max(value, Ring(0.55f, 0.03f) * dashes);

            value = Mathf.Max(value, Ring(0.3f, 0.015f));          // 内円

            // 放射スポーク6本（内円〜二重線の間）
            var spoke = Mathf.Abs(Mathf.Repeat(angle / (2f * Mathf.PI) * 6f + 0.5f, 1f) - 0.5f);
            if (r > 0.3f && r < 0.78f)
            {
                value = Mathf.Max(value, Mathf.Clamp01(1f - spoke / 0.02f) * 0.8f);
            }

            // 四方位の小円
            for (var k = 0; k < 4; k++)
            {
                var a = k * Mathf.PI * 0.5f;
                var cx = Mathf.Cos(a) * 0.55f;
                var cy = Mathf.Sin(a) * 0.55f;
                var d = Mathf.Sqrt((dx - cx) * (dx - cx) + (dy - cy) * (dy - cy));
                value = Mathf.Max(value, Mathf.Clamp01(1f - Mathf.Abs(d - 0.06f) / 0.02f));
            }

            // 中心の淡い光
            value = Mathf.Max(value, Mathf.Clamp01(1f - r / 0.08f) * 0.6f);
            return value;
        }

        /// <summary>レイラインの流れ用: X方向に流れるソフトパルス（整数周波数でタイル可能）</summary>
        private static float GenerateFlowPixel(float u, float v)
        {
            var pulse1 = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(u * 2f * Mathf.PI * 3f), 3f);
            var pulse2 = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(u * 2f * Mathf.PI * 5f + 1.7f), 5f);
            return 0.35f + 0.65f * Mathf.Max(pulse1, pulse2 * 0.8f);
        }

        /// <summary>格子点の決定論ハッシュ（0〜1）</summary>
        private static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                var h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        /// <summary>周期 period でタイルするバリューノイズ</summary>
        private static float ValueNoise(float u, float v, int period, int seed)
        {
            var x = u * period;
            var y = v * period;
            var x0 = Mathf.FloorToInt(x);
            var y0 = Mathf.FloorToInt(y);
            var fx = x - x0;
            var fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            var x1 = (x0 + 1) % period;
            var y1 = (y0 + 1) % period;
            x0 %= period;
            y0 %= period;
            var a = Hash01(x0, y0, seed);
            var b = Hash01(x1, y0, seed);
            var c = Hash01(x0, y1, seed);
            var d = Hash01(x1, y1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }
    }
}
