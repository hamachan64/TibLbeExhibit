using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// チュートリアル中だけプレイヤーを包む「ゲーム開始の間」（G.I.風の白い紋様空間）を
    /// ランタイム生成・制御するディレクター。
    ///
    /// 方針（Issue #13）:
    /// - パススルーは切らない。プレイヤーを完全に囲む<b>不透明ジオメトリ</b>
    ///   （円筒ルーム＋床＋天井）で視界を覆うことでVR化する。壁面シェーダーが
    ///   framebuffer alpha=1 を書き込み、MR映像を確実に隠す。
    /// - ルーム（メッシュ・マテリアル）はコードでプロシージャル生成する
    ///   （シーン/プレハブのYAML編集は禁止）。紋様もテクスチャ不要のシェーダー生成。
    /// - 壁面は Unlit 発光なので環境光に依存せず、ルーム内が暗く沈まない。
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

        [SerializeField, Tooltip("円筒ルームの半径（m）")]
        private float radius = 4f;

        [SerializeField, Tooltip("床から天井までの高さ（m）")]
        private float height = 3f;

        [SerializeField, Tooltip("床をプレイヤー頭部からどれだけ下に置くか（m）")]
        private float floorBelowEye = 1.4f;

        [SerializeField, Tooltip("円筒の分割数（多いほど滑らか・負荷増）")]
        private int radialSegments = 48;

        [SerializeField, Tooltip("Camera.main が無い場合に想定する目線の高さ（m）")]
        private float defaultEyeHeight = 1.5f;

        [SerializeField, Tooltip("登場演出（紋様が灯る）の所要時間（秒）")]
        private float revealDuration = 1.6f;

        [SerializeField, Tooltip("退場演出（紋様が消える）の所要時間（秒）。0で即時")]
        private float hideFadeDuration = 0.4f;

        /// <summary>ルーム本体（メッシュ）を持つ子GameObject。表示/非表示の切り替え対象</summary>
        private GameObject roomShell;
        private Material roomMaterial;
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
                roomMaterial = new Material(shader);
                roomMaterial.SetFloat(RevealId, 0f);
                renderer.sharedMaterial = roomMaterial;
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
        /// VR空間をプレイヤー（Camera.main / XrOriginLocator.Find() 基準）を中心に
        /// 構築・表示する。画面フェードアウト中に呼ばれる前提のため壁は即座に立つが、
        /// 紋様が灯る登場演出（_Reveal 0→1）を重ねる。
        /// </summary>
        public async UniTask ShowAsync(CancellationToken ct)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, destroyCancellationToken);
            var token = linked.Token;

            BuildRoom();
            revealTween?.Kill();

            PositionAtPlayer();
            roomShell.SetActive(true);
            IsShown = true;

            if (roomMaterial == null)
            {
                return;
            }

            roomMaterial.SetFloat(RevealId, 0f);
            revealTween = DOVirtual.Float(0f, 1f, revealDuration, v =>
                {
                    if (roomMaterial != null)
                    {
                        roomMaterial.SetFloat(RevealId, v);
                    }
                })
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
                if (roomMaterial != null && hideFadeDuration > 0f && roomShell.activeSelf)
                {
                    var from = roomMaterial.GetFloat(RevealId);
                    revealTween = DOVirtual.Float(from, 0f, hideFadeDuration, v =>
                        {
                            if (roomMaterial != null)
                            {
                                roomMaterial.SetFloat(RevealId, v);
                            }
                        })
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

        /// <summary>ルートをプレイヤー足元（頭部の真下）へ移動する。円筒は軸対称なので回転は不要。</summary>
        private void PositionAtPlayer()
        {
            Vector3 head;
            var cam = Camera.main;
            if (cam != null)
            {
                head = cam.transform.position;
            }
            else
            {
                var origin = XrOriginLocator.Find();
                head = (origin != null ? origin.position : Vector3.zero) + Vector3.up * defaultEyeHeight;
            }

            transform.SetPositionAndRotation(
                new Vector3(head.x, head.y - floorBelowEye, head.z),
                Quaternion.identity);
        }

        /// <summary>
        /// 円筒の側壁＋床＋天井を1メッシュとしてプロシージャル生成する。
        /// UVは「メートル単位」で貼る（壁: 周長×高さ / 床天井: ローカルXZ）。
        /// シェーダーがこのUVからセル密度を決めるため、面ごとに紋様スケールが揃う。
        /// 巻き方はシェーダー Cull Off に委ねるため厳密でなくてよい。
        /// </summary>
        private static Mesh BuildRoomMesh(float radius, float height, int segments)
        {
            var vertices = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();
            var triangles = new System.Collections.Generic.List<int>();

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
                triangles.Add(b0); triangles.Add(t0); triangles.Add(b1);
                triangles.Add(t0); triangles.Add(t1); triangles.Add(b1);
            }

            // --- 床（y=0, 三角ファン） ---
            AddCap(vertices, uvs, triangles, radius, 0f, segments);
            // --- 天井（y=height, 三角ファン） ---
            AddCap(vertices, uvs, triangles, radius, height, segments);

            var mesh = new Mesh { name = "GIPoker_TutorialRoom" };
            if (vertices.Count > 65000)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
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
            if (roomMaterial != null)
            {
                Destroy(roomMaterial);
            }
            if (roomMesh != null)
            {
                Destroy(roomMesh);
            }
        }
    }
}
