using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// ステージ装飾（Issue #8）のアセット生成・シーン配置を行う Editor セットアップスクリプト。
    ///
    /// メニュー「GIPoker/Stage/Setup Decoration」で以下を一括生成する:
    /// 1. マテリアル（Assets/_Content/Materials/Stage/）— 接地光輪・魔法陣・パーティクル・石材
    /// 2. 台座プレハブ（Assets/_Content/Prefabs/Stage/）— RuinPillar / FloatingRock
    /// 3. 現在開いているシーンへ装飾ルート「GIPokerStageDecoration」を配置
    ///    （StageDecorationDirector＋BorderCircle＋AmbientMotes＋台座のサンプル配置）
    ///
    /// **Environment.unity を開いた状態で実行すること**（装飾は共有空間の要素のため）。
    /// 再実行した場合、マテリアル・プレハブは同じGUIDのまま内容を更新し（参照は壊れない）、
    /// シーン上の装飾ルートは削除して作り直す。
    ///
    /// シリアライズ済みアセットのYAML直接編集は禁止（.claude/rules/unity_asset_rules.md）
    /// のため、生成はすべてエディタAPI（AssetDatabase / PrefabUtility / SerializedObject）で行う。
    /// </summary>
    public static class GIPokerStageDecorationSetup
    {
        /// <summary>境界魔法陣の半径（m）。プレイエリアに合わせて変更してから実行する</summary>
        private const float BorderRadius = 2.5f;

        /// <summary>装飾ルートのオブジェクト名（再実行時の置き換え検索にも使う）</summary>
        private const string RootName = "GIPokerStageDecoration";

        private const string MaterialFolder = "Assets/_Content/Materials/Stage";
        private const string PrefabFolder = "Assets/_Content/Prefabs/Stage";

        // ---------------------------------------------------------------
        // AmbientMotes パーティクル設定値（PICO 72fps 前提の負荷設計）
        //
        // - MaxParticles 64: 定常状態は Rate 10/s × 平均寿命 6.5s ≒ 65 個で上限に張り付く。
        //   粒あたりのサイズが 0.8〜2.5cm のため、両目合計でも画面被覆は 1% 未満で
        //   オーバードローは実質無視できる（透明パスの支配要因は粒数でなく被覆面積）
        // - 加算ブレンド1マテリアル＝1ドローコール。テクスチャサンプル無し
        //   （UV距離での丸ドット描画）、Soft Particles / Collision / Trails / Lights 不使用
        // - Noise は 64 粒に対する CPU コストのみで GPU 負荷ゼロ。揺らぎに必須のため採用
        // ---------------------------------------------------------------
        private const int MotesMaxParticles = 64;
        private const float MotesEmissionRate = 10f;
        private const float MotesLifetimeMin = 5f;
        private const float MotesLifetimeMax = 8f;
        private const float MotesSizeMin = 0.008f;
        private const float MotesSizeMax = 0.025f;

        [MenuItem("GIPoker/Stage/Setup Decoration")]
        public static void SetupDecoration()
        {
            EnsureFolders();

            // --- 1. マテリアル生成 ---
            var haloMat = CreateOrUpdateMaterial($"{MaterialFolder}/GIPStage_Halo.mat", "GIPoker/Stage/RadialGlow", mat =>
            {
                // 台座の接地光輪: 内側まで塗りつぶした淡い緑金の円。
                // パススルー輝度に馴染むよう彩度・アルファは控えめ（MR_STAGE_PLAN.md §4-4）
                mat.SetColor("_Color", new Color(0.65f, 0.92f, 0.60f, 0.55f));
                mat.SetFloat("_InnerRadius", 0f);
                mat.SetFloat("_OuterRadius", 0.46f);
                mat.SetFloat("_Softness", 0.22f);
                mat.SetFloat("_Intensity", 1f);
                mat.SetFloat("_Segments", 0f);
            });

            var borderRingMat = CreateOrUpdateMaterial($"{MaterialFolder}/GIPStage_BorderRing.mat", "GIPoker/Stage/RadialGlow", mat =>
            {
                // 魔法陣の外周実線。既存 TiB_BorderLine.mat（白の細線 α0.33）の淡さを踏襲
                mat.SetColor("_Color", new Color(0.72f, 0.93f, 1.0f, 0.45f));
                mat.SetFloat("_InnerRadius", 0.435f);
                mat.SetFloat("_OuterRadius", 0.465f);
                mat.SetFloat("_Softness", 0.012f);
                mat.SetFloat("_Intensity", 1f);
                mat.SetFloat("_Segments", 0f);
            });

            var borderRuneMat = CreateOrUpdateMaterial($"{MaterialFolder}/GIPStage_BorderRune.mat", "GIPoker/Stage/RadialGlow", mat =>
            {
                // 内側のルーン風破線リング（回転して魔法陣らしさを出す）
                mat.SetColor("_Color", new Color(0.72f, 0.93f, 1.0f, 0.36f));
                mat.SetFloat("_InnerRadius", 0.33f);
                mat.SetFloat("_OuterRadius", 0.38f);
                mat.SetFloat("_Softness", 0.02f);
                mat.SetFloat("_Intensity", 1f);
                mat.SetFloat("_Segments", 24f);
                mat.SetFloat("_SegmentFill", 0.55f);
            });

            var motesMat = CreateOrUpdateMaterial($"{MaterialFolder}/GIPStage_Motes.mat", "GIPoker/Stage/MoteParticle", mat =>
            {
                // 木漏れ日・胞子: 暖色の淡い光粒（加算のため暗めの色＝画面上で控えめな加算量）
                mat.SetColor("_Color", new Color(0.55f, 0.48f, 0.30f, 1f));
                mat.SetFloat("_Softness", 0.5f);
            });

            var stoneMat = CreateOrUpdateMaterial($"{MaterialFolder}/GIPStage_Stone.mat", "Universal Render Pipeline/Lit", mat =>
            {
                mat.SetColor("_BaseColor", new Color(0.45f, 0.44f, 0.42f, 1f));
                mat.SetFloat("_Smoothness", 0.15f);
            });

            var mossMat = CreateOrUpdateMaterial($"{MaterialFolder}/GIPStage_StoneMoss.mat", "Universal Render Pipeline/Lit", mat =>
            {
                mat.SetColor("_BaseColor", new Color(0.36f, 0.45f, 0.32f, 1f));
                mat.SetFloat("_Smoothness", 0.1f);
            });

            // --- 2. 台座プレハブ生成 ---
            var pillarPrefab = BuildRuinPillarPrefab(stoneMat, mossMat, haloMat);
            var rockPrefab = BuildFloatingRockPrefab(stoneMat, mossMat, haloMat);

            // --- 3. シーン配置 ---
            PlaceDecorationRoot(pillarPrefab, rockPrefab, borderRingMat, borderRuneMat, motesMat);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[GIPokerStageDecorationSetup] 完了: マテリアル({MaterialFolder}) / プレハブ({PrefabFolder}) / シーン配置({RootName})。シーンを保存してください");
        }

        // =============================================================
        // プレハブ構築
        // =============================================================

        /// <summary>遺跡の石柱台座プレハブを生成する</summary>
        private static GameObject BuildRuinPillarPrefab(Material stone, Material moss, Material halo)
        {
            var root = new GameObject("RuinPillar");
            try
            {
                var visualRoot = new GameObject("VisualRoot");
                visualRoot.transform.SetParent(root.transform, false);

                // 土台（Cube）→ 柱（Cylinder）→ 天板（Cube）の3段構成
                AddPrimitive(visualRoot.transform, PrimitiveType.Cube, "Base",
                    new Vector3(0f, 0.06f, 0f), Quaternion.identity, new Vector3(0.5f, 0.12f, 0.5f), stone);
                AddPrimitive(visualRoot.transform, PrimitiveType.Cylinder, "Column",
                    new Vector3(0f, 0.54f, 0f), Quaternion.identity, new Vector3(0.32f, 0.42f, 0.32f), stone);
                AddPrimitive(visualRoot.transform, PrimitiveType.Cube, "Cap",
                    new Vector3(0f, 1.0f, 0f), Quaternion.Euler(0f, 12f, 0f), new Vector3(0.4f, 0.08f, 0.4f), moss);

                // アイテム出現アンカー（台座上面）。後続Issueが StageSpawnAnchor を後付けする
                var anchor = new GameObject("AnchorPoint");
                anchor.transform.SetParent(visualRoot.transform, false);
                anchor.transform.localPosition = new Vector3(0f, 1.15f, 0f);

                var haloRenderer = AddGroundHalo(root.transform, 1.1f, halo);

                WirePedestal(root, StagePedestalKind.Pillar, anchor.transform, visualRoot.transform, haloRenderer, sunkenDepth: 1.3f);

                return SavePrefab(root, $"{PrefabFolder}/RuinPillar.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>浮遊岩台座プレハブを生成する</summary>
        private static GameObject BuildFloatingRockPrefab(Material stone, Material moss, Material halo)
        {
            var root = new GameObject("FloatingRock");
            try
            {
                var visualRoot = new GameObject("VisualRoot");
                visualRoot.transform.SetParent(root.transform, false);

                // 傾けたCube2つの重なりで岩らしいシルエットを作る
                AddPrimitive(visualRoot.transform, PrimitiveType.Cube, "RockA",
                    new Vector3(0f, 0.85f, 0f), Quaternion.Euler(18f, 40f, 12f), new Vector3(0.34f, 0.26f, 0.30f), stone);
                AddPrimitive(visualRoot.transform, PrimitiveType.Cube, "RockB",
                    new Vector3(0.08f, 0.68f, -0.05f), Quaternion.Euler(-12f, 70f, -8f), new Vector3(0.20f, 0.16f, 0.22f), moss);

                var anchor = new GameObject("AnchorPoint");
                anchor.transform.SetParent(visualRoot.transform, false);
                anchor.transform.localPosition = new Vector3(0f, 1.1f, 0f);

                var haloRenderer = AddGroundHalo(root.transform, 0.9f, halo);

                // 岩は空中（y≈0.85）に浮くため、地中に完全に隠すには深めに沈める
                WirePedestal(root, StagePedestalKind.FloatingRock, anchor.transform, visualRoot.transform, haloRenderer, sunkenDepth: 1.6f);

                return SavePrefab(root, $"{PrefabFolder}/FloatingRock.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>プリミティブを生成して装飾用に整える（コライダー除去・影OFF）</summary>
        private static GameObject AddPrimitive(Transform parent, PrimitiveType type, string name,
            Vector3 localPos, Quaternion localRot, Vector3 localScale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = localScale;

            // 装飾のためコライダーは不要（ハンドトラッキングのタッチ判定を汚さない）
            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }

            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            // 影は Shadow Catcher（Issue #1）との整合を統合フェーズで確認するまでキャストのみ許可
            renderer.receiveShadows = false;
            return go;
        }

        /// <summary>接地光輪クアッドを追加する（VisualRootの外・床面固定）</summary>
        private static Renderer AddGroundHalo(Transform parent, float diameter, Material halo)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "GroundHalo";
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = new Vector3(0f, 0.01f, 0f); // 床とのZファイト回避
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // 面を上に向ける
            quad.transform.localScale = new Vector3(diameter, diameter, 1f);

            var collider = quad.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }

            var renderer = quad.GetComponent<Renderer>();
            renderer.sharedMaterial = halo;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        /// <summary>StagePedestal を追加し、SerializedObject 経由でフィールドを結線する</summary>
        private static void WirePedestal(GameObject root, StagePedestalKind kind,
            Transform anchor, Transform visualRoot, Renderer halo, float sunkenDepth)
        {
            var pedestal = root.AddComponent<StagePedestal>();
            var so = new SerializedObject(pedestal);
            so.FindProperty("kind").enumValueIndex = (int)kind;
            so.FindProperty("anchorPoint").objectReferenceValue = anchor;
            so.FindProperty("visualRoot").objectReferenceValue = visualRoot;
            so.FindProperty("groundHalo").objectReferenceValue = halo;
            so.FindProperty("sunkenDepth").floatValue = sunkenDepth;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject SavePrefab(GameObject root, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            if (prefab == null)
            {
                throw new InvalidOperationException($"プレハブの保存に失敗: {path}");
            }
            return prefab;
        }

        // =============================================================
        // シーン配置
        // =============================================================

        /// <summary>装飾ルートを現在のシーンへ配置する（既存があれば置き換え）</summary>
        private static void PlaceDecorationRoot(GameObject pillarPrefab, GameObject rockPrefab,
            Material borderRingMat, Material borderRuneMat, Material motesMat)
        {
            var existing = GameObject.Find(RootName);
            if (existing != null)
            {
                Debug.Log($"[GIPokerStageDecorationSetup] 既存の {RootName} を置き換えます");
                UnityEngine.Object.DestroyImmediate(existing);
            }

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Setup GIPoker Stage Decoration");
            // StageDecorationDirector の各参照は Awake の自動収集に任せる
            // （プレハブ差し替え・台座の増減があっても結線し直し不要にするため）
            root.AddComponent<StageDecorationDirector>();

            // --- BorderCircle（境界魔法陣） ---
            var borderGo = new GameObject("BorderCircle");
            borderGo.transform.SetParent(root.transform, false);
            borderGo.transform.localPosition = new Vector3(0f, 0.01f, 0f);
            var border = borderGo.AddComponent<BorderCircle>();

            var ringRenderer = AddFlatQuad(borderGo.transform, "RingQuad", BorderRadius * 2f, 0f, borderRingMat);
            var runeRenderer = AddFlatQuad(borderGo.transform, "RuneQuad", BorderRadius * 2f, 0.002f, borderRuneMat);

            var borderSo = new SerializedObject(border);
            borderSo.FindProperty("ringRenderer").objectReferenceValue = ringRenderer;
            borderSo.FindProperty("runeRenderer").objectReferenceValue = runeRenderer;
            borderSo.ApplyModifiedPropertiesWithoutUndo();

            // --- AmbientMotes（環境パーティクル） ---
            var motesGo = new GameObject("AmbientMotes");
            motesGo.transform.SetParent(root.transform, false);
            motesGo.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            var particleSystem = motesGo.AddComponent<ParticleSystem>();
            ConfigureMotesParticle(particleSystem, motesMat);

            var motes = motesGo.AddComponent<AmbientMotes>();
            var motesSo = new SerializedObject(motes);
            motesSo.FindProperty("motes").objectReferenceValue = particleSystem;
            motesSo.ApplyModifiedPropertiesWithoutUndo();

            // --- 台座のサンプル配置（魔法陣の内側）。位置は統合フェーズで会場に合わせて調整する ---
            PlacePedestal(pillarPrefab, root.transform, new Vector3(1.6f, 0f, 1.0f), 20f);
            PlacePedestal(pillarPrefab, root.transform, new Vector3(-1.5f, 0f, 1.4f), -35f);
            PlacePedestal(rockPrefab, root.transform, new Vector3(1.2f, 0f, -1.6f), 60f);
            PlacePedestal(rockPrefab, root.transform, new Vector3(-1.7f, 0f, -0.9f), 140f);
        }

        /// <summary>床向きのクアッドを追加する（魔法陣リング用）</summary>
        private static Renderer AddFlatQuad(Transform parent, string name, float size, float yOffset, Material material)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = new Vector3(0f, yOffset, 0f);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = new Vector3(size, size, 1f);

            var collider = quad.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }

            var renderer = quad.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        private static void PlacePedestal(GameObject prefab, Transform parent, Vector3 position, float yawDegrees)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.transform.localPosition = position;
            instance.transform.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);
        }

        /// <summary>
        /// AmbientMotes のパーティクル設定を焼き込む。
        /// 設定値の根拠はクラス冒頭の定数コメントを参照。
        /// </summary>
        private static void ConfigureMotesParticle(ParticleSystem ps, Material material)
        {
            var main = ps.main;
            main.playOnAwake = false; // 再生タイミングは AmbientMotes.EnterPhase が握る
            main.loop = true;
            main.maxParticles = MotesMaxParticles;
            main.startLifetime = new ParticleSystem.MinMaxCurve(MotesLifetimeMin, MotesLifetimeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
            main.startSize = new ParticleSystem.MinMaxCurve(MotesSizeMin, MotesSizeMax);
            main.startColor = Color.white; // 発色はマテリアル側の _Color で決める
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.gravityModifier = 0f;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = MotesEmissionRate;

            // プレイエリア全域（境界魔法陣の内側）を覆う箱から湧かせる
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(BorderRadius * 2f, 2.4f, BorderRadius * 2f);

            // 各粒の出現・消滅を滑らかにする（ポップイン/アウト禁止の要）
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.25f),
                    new GradientAlphaKey(1f, 0.7f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = new ParticleSystem.MinMaxGradient(gradient);

            // ゆらゆらと漂う有機的な動き（CPUコストのみ・GPU負荷ゼロ）
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.05f;
            noise.frequency = 0.15f;
            noise.scrollSpeed = 0.05f;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        // =============================================================
        // アセットユーティリティ
        // =============================================================

        private static void EnsureFolders()
        {
            EnsureFolder("Assets/_Content/Materials", "Stage");
            EnsureFolder("Assets/_Content/Prefabs", "Stage");
        }

        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{name}"))
            {
                AssetDatabase.CreateFolder(parent, name);
            }
        }

        /// <summary>
        /// マテリアルを生成、既存なら同じGUIDのまま内容だけ更新する（再実行しても参照が壊れない）。
        /// </summary>
        private static Material CreateOrUpdateMaterial(string path, string shaderName, Action<Material> configure)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                throw new InvalidOperationException($"シェーダーが見つかりません: {shaderName}（コンパイルエラーが残っていないか確認してください）");
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            configure(material);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
