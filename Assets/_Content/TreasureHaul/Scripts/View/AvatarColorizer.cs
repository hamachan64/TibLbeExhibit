using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using R3;
using Styly.NetSync;
using TMPro;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 仲間のアバター（NetSync の STYLY Default Avatar A 1）の頭・手・体を、その人の色で塗る（設計書 3.4）。
    /// MaterialPropertyBlock で塗る（共有マテリアルは書き換えない）。色のない人・待機中は塗りを外す。自分のアバターは塗らない。
    /// 既定のアバターのシェーダー（Shader Graphs/Avatar）には色のプロパティが無いので、塗るあいだだけ、同じ計算を手書きで再現して色（_BaseColor）を持たせた
    /// 共有マテリアル（<see cref="_tintMaterial"/>。シェーダー TIB/AvatarTint。半透明・縁が光る）に差し替える。元のマテリアルは覚えておき、塗りを外すときに戻す。
    /// </summary>
    public sealed class AvatarColorizer : MonoBehaviour
    {
        [SerializeField] SessionClient _client;
        [SerializeField, Tooltip("塗るあいだ差し替える、色（_BaseColor）を持つ共有マテリアル（TH_AvatarTint。シェーダー TIB/AvatarTint）。空なら差し替えず、元のマテリアルに色のプロパティがあるときだけ塗れる")] Material _tintMaterial;

        /// <summary>付け直しの間隔（秒）。ClientNo の入れ替わり・後から生成されたアバターに備える</summary>
        const double RefreshSeconds = 0.5;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        // アバターごとに今塗っている色番号（-1 = 塗っていない）
        readonly Dictionary<NetSyncAvatar, int> _applied = new Dictionary<NetSyncAvatar, int>();
        readonly List<Renderer> _rendererBuf = new List<Renderer>();
        // 差し替える前の共有マテリアル（塗りを外すときに戻す）
        readonly Dictionary<Renderer, Material> _originals = new Dictionary<Renderer, Material>();
        readonly List<NetSyncAvatar> _removeBuf = new List<NetSyncAvatar>();
        MaterialPropertyBlock _block;
        SessionClientCore _core;
        double _nextRefresh;
        bool _subscribed;
        System.Func<string, int> _clientNoOf;
        // 付け直しのたびに作らない使い回しの入れ物
        readonly HashSet<NetSyncAvatar> _seen = new HashSet<NetSyncAvatar>();
        readonly List<Renderer> _deadBuf = new List<Renderer>();

        /// <summary>今塗っているアバター数（確認用）</summary>
        public int PaintedCount { get { int n = 0; foreach (var kv in _applied) if (kv.Value >= 0) n++; return n; } }

        void Update()
        {
            var core = _client != null ? _client.Core : null;
            if (core == null) return;
            _core = core;
            // 接続イベントの購読は 1 回だけ。NetSyncManager がそろってから（Core が先にできても、後で購読する）
            if (!_subscribed)
            {
                var nm0 = NetSyncManager.Instance;
                if (nm0 != null)
                {
                    _subscribed = true;
                    _clientNoOf = nm0.GetClientNoByDeviceId;
                    nm0.OnAvatarConnected.AsObservable().Subscribe(_ => RefreshLater().Forget()).AddTo(this);
                }
            }
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < _nextRefresh) return;
            _nextRefresh = now + RefreshSeconds;
            Refresh();
        }

        async UniTaskVoid RefreshLater()
        {
            try
            {
                await UniTask.Delay(1000, cancellationToken: destroyCancellationToken);   // アバターの生成を待つ
                Refresh();
            }
            catch (System.OperationCanceledException) { }
        }

        /// <summary>今のアバターすべてについて、あるべき色に塗る・塗りを外す</summary>
        public void Refresh()
        {
            var nm = NetSyncManager.Instance;
            var palette = _client != null && _client.Content != null ? _client.Content.palette : null;
            if (_core == null || nm == null || palette == null) return;
            var colors = _core.Tracker.DeviceColors;
            int own = nm.ClientNo;
            var seen = _seen;
            seen.Clear();
            _clientNoOf ??= nm.GetClientNoByDeviceId;
            foreach (var avatar in FindObjectsByType<NetSyncAvatar>(FindObjectsSortMode.None))
            {
                seen.Add(avatar);
                int color = avatar.IsLocalAvatar ? -1 : AvatarColorMap.ColorOfClient(colors, _clientNoOf, avatar.ClientNo, own);
                if (_applied.TryGetValue(avatar, out int cur) && cur == color) continue;
                Apply(avatar, palette.Get(color), color);
                _applied[avatar] = color;
            }
            _removeBuf.Clear();
            foreach (var kv in _applied) if (kv.Key == null || !seen.Contains(kv.Key)) _removeBuf.Add(kv.Key);
            foreach (var k in _removeBuf) _applied.Remove(k);
            // 消えたアバターのレンダラーの記録は捨てる
            _deadBuf.Clear();
            foreach (var kv in _originals) if (kv.Key == null) _deadBuf.Add(kv.Key);
            foreach (var k in _deadBuf) _originals.Remove(k);
        }

        void Apply(NetSyncAvatar avatar, ThPlayerColorSlot slot, int color)
        {
            avatar.GetComponentsInChildren(true, _rendererBuf);
            _block ??= new MaterialPropertyBlock();
            foreach (var r in _rendererBuf)
            {
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer) || r.GetComponent<TMP_Text>() != null) continue;   // 文字（名札など）は塗らない
                if (color < 0 || slot == null)
                {
                    if (_originals.TryGetValue(r, out var original)) { r.sharedMaterial = original; _originals.Remove(r); }
                    r.SetPropertyBlock(null);
                    continue;
                }
                if (_tintMaterial != null && r.sharedMaterial != _tintMaterial)
                {
                    _originals[r] = r.sharedMaterial;
                    r.sharedMaterial = _tintMaterial;
                }
                r.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, slot.avatarColor);
                _block.SetColor(ColorId, slot.avatarColor);
                r.SetPropertyBlock(_block);
            }
        }

        void OnDestroy()
        {
            // 塗りを外す（アバターが残っている場合のため）
            foreach (var kv in _applied) if (kv.Key != null) Apply(kv.Key, null, -1);
            _applied.Clear();
        }
    }
}
