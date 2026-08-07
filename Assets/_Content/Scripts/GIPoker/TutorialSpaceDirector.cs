using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// チュートリアル中だけ体験者全員を包む「ゲーム開始の間」（G.I.具現化装置風の
    /// 円筒VR空間）をランタイム生成・制御するディレクター。
    ///
    /// 方針（Issue #13、共有ルーム化で更新）:
    /// - パススルーは切らない。プレイヤーを完全に囲む<b>不透明ジオメトリ</b>
    ///   （円筒ルーム＋床＋天井）で視界を覆うことでVR化する。壁面シェーダーが
    ///   framebuffer alpha=1 を書き込み、MR映像を確実に隠す。
    /// - ルーム（メッシュ・マテリアル）はコードでプロシージャル生成する
    ///   （シーン/プレハブのYAML編集は禁止）。紋様もテクスチャ不要のシェーダー生成。
    /// - 壁面は Unlit 発光なので環境光に依存せず、ルーム内が暗く沈まない。
    /// - <b>共有ルーム</b>: 各プレイヤーのカメラ基準に個別配置していた旧設計をやめ、
    ///   固定のワールド座標（<see cref="sharedRoomAnchor"/>）に一度だけ配置する。
    ///   STYLY NetSync で全クライアントのワールド座標系が空間アンカーにより
    ///   揃っている前提のため、固定座標にするだけで「全員が物理的に同じ1つの部屋を
    ///   共有している」状態になる（新規RPC・ネットワークオブジェクト化は不要）。
    ///
    /// GIPokerBootstrap への結線は<b>行わない</b>（遷移Issue担当が Create()/ShowAsync()/
    /// HideAsync() を呼ぶ）。本コンポーネントは単体で完結する。
    /// </summary>
    [DisallowMultipleComponent]
    public class TutorialSpaceDirector : MonoBehaviour
    {
        /// <summary>
        /// 壁面シェーダー名。シェーダーファイルはどのアセットからも参照されないため、
        /// Resources フォルダ配下（Assets/_Content/Shaders/GIPoker/Resources/）に置いて
        /// 実機ビルドへの同梱を保証している（stripping されると Shader.Find が null を
        /// 返し、ルームが透けてパススルーを隠せなくなる）。
        /// </summary>
        private const string ShaderName = "GIPoker/Tutorial/RoomWall";
        private static readonly int RevealId = Shader.PropertyToID("_Reveal");
        private static readonly int StripeIntensityId = Shader.PropertyToID("_StripeIntensity");
        private static readonly int TilingId = Shader.PropertyToID("_Tiling");
        private static readonly int LineWidthId = Shader.PropertyToID("_LineWidth");
        private static readonly int EmissionId = Shader.PropertyToID("_Emission");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");

        /// <summary>サブメッシュ番号（壁/床/天井）。BuildRoomMesh の SetTriangles と対応させる</summary>
        private const int WallSubMesh = 0;
        private const int FloorSubMesh = 1;
        private const int CeilingSubMesh = 2;

        [SerializeField, Tooltip("円筒ルームの半径（m）")]
        private float radius = 6f;

        [SerializeField, Tooltip("床から天井までの高さ（m）")]
        private float height = 5f;

        [SerializeField, Tooltip("共有ルームの固定ワールド座標（実世界の床レベルY=0基準。展示レイアウトに応じて現地調整する）")]
        private Vector3 sharedRoomAnchor = new(-4.9f, 0f, -0.5f);

        [SerializeField, Tooltip("円筒の分割数（多いほど滑らか・負荷増）")]
        private int radialSegments = 48;

        [SerializeField, Tooltip("登場演出（紋様が灯る）の所要時間（秒）")]
        private float revealDuration = 1.6f;

        [SerializeField, Tooltip("退場演出（紋様が消える）の所要時間（秒）。0で即時")]
        private float hideFadeDuration = 0.4f;

        /// <summary>ルーム本体（メッシュ）を持つ子GameObject。表示/非表示の切り替え対象</summary>
        private GameObject roomShell;

        /// <summary>壁/床/天井の順（BuildRoomMesh のサブメッシュ番号と対応）</summary>
        private Material[] roomMaterials;
        private Mesh roomMesh;
        private Tween revealTween;

        /// <summary>VR空間が現在表示中か</summary>
        public bool IsShown { get; private set; }

        /// <summary>
        /// GameObject "GIPoker_TutorialSpace" を生成して返す。生成直後は非表示。
        /// ルームのメッシュ・マテリアルはこの時点で構築され、表示だけ待機状態になる。
        /// </summary>
        public static TutorialSpaceDirector Create()
        {
            var go = new GameObject("GIPoker_TutorialSpace");
            var director = go.AddComponent<TutorialSpaceDirector>();
            director.BuildRoom();
            return director;
        }

        private void BuildRoom()
        {
            if (roomShell != null)
            {
                return;
            }

            roomShell = new GameObject("RoomShell");
            roomShell.transform.SetParent(transform, false);

            roomMesh = BuildRoomMesh(radius, height, Mathf.Max(8, radialSegments));

            var filter = roomShell.AddComponent<MeshFilter>();
            filter.sharedMesh = roomMesh;

            var renderer = roomShell.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            var shader = Shader.Find(ShaderName);
            if (shader != null)
            {
                roomMaterials = new[]
                {
                    BuildMaterial(shader, WallPreset),
                    BuildMaterial(shader, FloorPreset),
                    BuildMaterial(shader, CeilingPreset),
                };
                renderer.sharedMaterials = roomMaterials;
            }
            else
            {
                // ルーム壁面が描かれず、パススルーを隠せない致命的状態。
                // シェーダーのビルド同梱漏れ（Resources 外への移動等）が典型原因
                Debug.LogError(
                    $"[TutorialSpaceDirector] シェーダー '{ShaderName}' が見つかりません。" +
                    "ルーム壁面が描画されず、チュートリアルVR空間がパススルーを隠せない致命的状態です。" +
                    "GIP_TutorialRoomWall.shader が Resources フォルダ配下にあるか確認してください。");
            }

            // 生成直後は非表示
            roomShell.SetActive(false);
            IsShown = false;
        }

        /// <summary>
        /// VR空間を固定のワールド座標（<see cref="sharedRoomAnchor"/>）に構築・表示する。
        /// 画面フェードアウト中に呼ばれる前提のため壁は即座に立つが、
        /// 紋様が灯る登場演出（_Reveal 0→1）を重ねる。
        /// </summary>
        public async UniTask ShowAsync(CancellationToken ct)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, destroyCancellationToken);
            var token = linked.Token;

            BuildRoom();
            revealTween?.Kill();

            PositionAtSharedRoom();
            roomShell.SetActive(true);
            IsShown = true;

            if (roomMaterials == null || roomMaterials.Length == 0)
            {
                return;
            }

            SetRevealImmediate(0f);
            revealTween = DOVirtual.Float(0f, 1f, revealDuration, SetReveal)
                .SetEase(Ease.InOutSine)
                .SetLink(gameObject);

            await WaitTweenAsync(revealTween, token);
        }

        /// <summary>
        /// VR空間を非表示化する。紋様をフェードで消してからルームを無効化する
        /// （遷移側が画面を暗転させている前提なので即時でも破綻しない）。
        /// </summary>
        public async UniTask HideAsync(CancellationToken ct)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, destroyCancellationToken);
            var token = linked.Token;

            revealTween?.Kill();
            IsShown = false;

            if (roomShell == null)
            {
                return;
            }

            try
            {
                if (roomMaterials != null && roomMaterials.Length > 0 && hideFadeDuration > 0f && roomShell.activeSelf)
                {
                    var from = roomMaterials[0] != null ? roomMaterials[0].GetFloat(RevealId) : 0f;
                    revealTween = DOVirtual.Float(from, 0f, hideFadeDuration, SetReveal)
                        .SetEase(Ease.InQuad)
                        .SetLink(gameObject);

                    await WaitTweenAsync(revealTween, token);
                }
            }
            finally
            {
                // フェード待機がキャンセルされても「非表示化」だけは必ず完遂する
                // （HideAsync の契約はルームを消すこと。中途半端な表示残りを防ぐ）
                if (roomShell != null)
                {
                    roomShell.SetActive(false);
                }
            }
        }

        /// <summary>_Reveal を壁/床/天井の全マテリアルへ同じ値で反映する</summary>
        private void SetReveal(float value)
        {
            if (roomMaterials == null)
            {
                return;
            }
            for (var i = 0; i < roomMaterials.Length; i++)
            {
                if (roomMaterials[i] != null)
                {
                    roomMaterials[i].SetFloat(RevealId, value);
                }
            }
        }

        private void SetRevealImmediate(float value) => SetReveal(value);

        /// <summary>
        /// Tween の完了を UniTask で待つ。
        /// 本プロジェクトの DOTween は DLL プラグイン（Assets/Plugins/Demigiant）のため
        /// UNITASK_DOTWEEN_SUPPORT シンボルが定義されず、UniTask の
        /// <c>Tween.ToUniTask()</c> 拡張は使えない。代わりに Tween 非依存の
        /// ポーリング待機を行う（完了時は autoKill により IsActive() が false になる）。
        /// キャンセル時は Tween を Kill してから例外を伝播する。
        /// </summary>
        private static async UniTask WaitTweenAsync(Tween tween, CancellationToken ct)
        {
            try
            {
                await UniTask.WaitUntil(
                    () => tween == null || !tween.IsActive(),
                    cancellationToken: ct);
            }
            catch (OperationCanceledException)
            {
                if (tween != null && tween.IsActive())
                {
                    tween.Kill();
                }
                throw;
            }
        }

        /// <summary>ルートを固定の共有ルーム座標へ移動する。円筒は軸対称なので回転は不要。</summary>
        private void PositionAtSharedRoom()
        {
            transform.SetPositionAndRotation(sharedRoomAnchor, Quaternion.identity);
        }

        /// <summary>マテリアルの見た目プリセット（壁/床/天井で色味・模様の密度を変える）</summary>
        private readonly struct MaterialPreset
        {
            public readonly Color BaseColor;
            public readonly Color GlowColor;
            public readonly float StripeIntensity;
            public readonly float Tiling;
            public readonly float? LineWidth;
            public readonly float Emission;

            public MaterialPreset(Color baseColor, Color glowColor, float stripeIntensity,
                float tiling, float emission, float? lineWidth = null)
            {
                BaseColor = baseColor;
                GlowColor = glowColor;
                StripeIntensity = stripeIntensity;
                Tiling = tiling;
                Emission = emission;
                LineWidth = lineWidth;
            }
        }

        // 紺〜青黒ベース＋水色/白の発光（G.I.具現化装置演出風）。壁のみストライプ発光を有効化する。
        private static readonly MaterialPreset WallPreset = new(
            baseColor: new Color(0.03f, 0.05f, 0.10f),
            glowColor: new Color(0.55f, 0.85f, 1.0f),
            stripeIntensity: 1f,
            tiling: 1.8f,
            emission: 1.6f);

        private static readonly MaterialPreset FloorPreset = new(
            baseColor: new Color(0.02f, 0.03f, 0.06f),
            glowColor: new Color(0.4f, 0.7f, 0.95f),
            stripeIntensity: 0f,
            tiling: 2.4f,
            emission: 1.0f,
            lineWidth: 0.04f);

        private static readonly MaterialPreset CeilingPreset = new(
            baseColor: new Color(0.04f, 0.06f, 0.11f),
            glowColor: new Color(0.5f, 0.8f, 1.0f),
            stripeIntensity: 0f,
            tiling: 1.2f,
            emission: 0.9f);

        private static Material BuildMaterial(Shader shader, MaterialPreset preset)
        {
            var mat = new Material(shader);
            mat.SetColor(BaseColorId, preset.BaseColor);
            mat.SetColor(GlowColorId, preset.GlowColor);
            mat.SetFloat(StripeIntensityId, preset.StripeIntensity);
            mat.SetFloat(TilingId, preset.Tiling);
            mat.SetFloat(EmissionId, preset.Emission);
            if (preset.LineWidth.HasValue)
            {
                mat.SetFloat(LineWidthId, preset.LineWidth.Value);
            }
            mat.SetFloat(RevealId, 0f);
            return mat;
        }

        /// <summary>
        /// 円筒の側壁＋床＋天井を「壁/床/天井」の3サブメッシュに分けてプロシージャル生成する。
        /// UVは「メートル単位」で貼る（壁: 周長×高さ / 床天井: ローカルXZ）。
        /// シェーダーがこのUVからセル密度を決めるため、面ごとに紋様スケールが揃う。
        /// 巻き方はシェーダー Cull Off に委ねるため厳密でなくてよい。
        /// </summary>
        private static Mesh BuildRoomMesh(float radius, float height, int segments)
        {
            var vertices = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();
            var wallTriangles = new System.Collections.Generic.List<int>();
            var floorTriangles = new System.Collections.Generic.List<int>();
            var ceilingTriangles = new System.Collections.Generic.List<int>();

            var circumference = 2f * Mathf.PI * radius;

            // --- 側壁（底リング/天リングのペア） ---
            var wallStart = vertices.Count;
            for (var i = 0; i <= segments; i++)
            {
                var t = i / (float)segments;
                var a = t * Mathf.PI * 2f;
                var x = Mathf.Cos(a) * radius;
                var z = Mathf.Sin(a) * radius;
                var u = t * circumference;

                vertices.Add(new Vector3(x, 0f, z));
                uvs.Add(new Vector2(u, 0f));
                vertices.Add(new Vector3(x, height, z));
                uvs.Add(new Vector2(u, height));
            }
            for (var i = 0; i < segments; i++)
            {
                var b0 = wallStart + i * 2;
                var t0 = b0 + 1;
                var b1 = b0 + 2;
                var t1 = b0 + 3;
                wallTriangles.Add(b0); wallTriangles.Add(t0); wallTriangles.Add(b1);
                wallTriangles.Add(t0); wallTriangles.Add(t1); wallTriangles.Add(b1);
            }

            // --- 床（y=0, 三角ファン） ---
            AddCap(vertices, uvs, floorTriangles, radius, 0f, segments);
            // --- 天井（y=height, 三角ファン） ---
            AddCap(vertices, uvs, ceilingTriangles, radius, height, segments);

            var mesh = new Mesh { name = "GIPoker_TutorialRoom" };
            if (vertices.Count > 65000)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 3;
            mesh.SetTriangles(wallTriangles, WallSubMesh);
            mesh.SetTriangles(floorTriangles, FloorSubMesh);
            mesh.SetTriangles(ceilingTriangles, CeilingSubMesh);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>床/天井の円盤を三角ファンで追加する（UVはローカルXZのメートル値）。</summary>
        private static void AddCap(
            System.Collections.Generic.List<Vector3> vertices,
            System.Collections.Generic.List<Vector2> uvs,
            System.Collections.Generic.List<int> triangles,
            float radius, float y, int segments)
        {
            var center = vertices.Count;
            vertices.Add(new Vector3(0f, y, 0f));
            uvs.Add(new Vector2(0f, 0f));

            var ringStart = vertices.Count;
            for (var i = 0; i <= segments; i++)
            {
                var a = i / (float)segments * Mathf.PI * 2f;
                var x = Mathf.Cos(a) * radius;
                var z = Mathf.Sin(a) * radius;
                vertices.Add(new Vector3(x, y, z));
                uvs.Add(new Vector2(x, z));
            }
            for (var i = 0; i < segments; i++)
            {
                triangles.Add(center);
                triangles.Add(ringStart + i);
                triangles.Add(ringStart + i + 1);
            }
        }

        private void OnDestroy()
        {
            revealTween?.Kill();
            if (roomMaterials != null)
            {
                foreach (var mat in roomMaterials)
                {
                    if (mat != null)
                    {
                        Destroy(mat);
                    }
                }
            }
            if (roomMesh != null)
            {
                Destroy(roomMesh);
            }
        }
    }
}
