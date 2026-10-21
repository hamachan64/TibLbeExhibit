using DG.Tweening;
using TMPro;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 10 秒のヒント（F-TUT-5）: 練習用お宝を持てない人の手の横に、手のモデルが「開く⇄握る」を繰り返す様子と、文字（GripBeam）を出す。
    /// 出す・消すの判断は <see cref="PracticeHintJudge"/>（純粋クラス）。ここは見た目だけ。
    /// 手のモデルは XR Hands の HandVisualizer サンプルの FBX をそのまま使う（改変しない。実行時に関節の回転を動かすだけ）。
    /// コライダーは付けない（お宝・手の判定・物理に影響しない）。回転は DOTween で動かす（Update の手計算はしない）。
    /// </summary>
    public sealed class PracticeHintHand : MonoBehaviour
    {
        [SerializeField] SessionClient _client;
        [SerializeField] TreasureClient _treasure;
        [SerializeField, Tooltip("右手のモデル（サンプルの RightHand.fbx。改変しない）")] GameObject _rightModel;
        [SerializeField, Tooltip("左手のモデル（サンプルの LeftHand.fbx。改変しない）")] GameObject _leftModel;
        [SerializeField, Tooltip("手のモデルに使うマテリアル（TH_HintHand）")] Material _material;
        [SerializeField, Tooltip("文字のフォント（日本語フォント）")] TMP_FontAsset _font;
        [SerializeField, Tooltip("手の横にずらす距離（m）")] float _sideOffset = 0.15f;
        [SerializeField, Tooltip("手のモデルの拡大（実物の手に対して）")] float _scale = 1.8f;
        [SerializeField, Tooltip("手を見失っている間の、頭からの位置（正面やや下）")] Vector3 _headOffset = new Vector3(0.2f, -0.22f, 0.8f);
        [SerializeField, Tooltip("文字の位置（手のモデルの下 m）")] float _textDrop = 0.16f;
        [SerializeField] float _textSize = 0.45f;
        [SerializeField, Tooltip("「開く→握る」の片道の秒数（1 往復 = 2 倍）")] float _halfCycleSeconds = 0.6f;

        /// <summary>関節の曲げ（度）。指は X 軸まわり。開いた手はサンプルの初期の姿勢から少し反らす</summary>
        const float OpenExtension = -12f;
        static readonly float[] FingerCurl = { 80f, 95f, 65f };   // Proximal, Intermediate, Distal
        static readonly string[] Fingers = { "Index", "Middle", "Ring", "Little" };
        static readonly string[] Segments = { "Proximal", "Intermediate", "Distal" };

        sealed class Joint
        {
            public Transform T;
            public Quaternion Open, Closed;
        }

        sealed class HandInstance
        {
            public GameObject Root;
            public readonly System.Collections.Generic.List<Joint> Joints = new System.Collections.Generic.List<Joint>();
        }

        readonly PracticeHintJudge _judge = new PracticeHintJudge();
        GameObject _root;
        HandInstance _right, _left;
        TextMeshPro _text;
        Tween _loop;
        Transform _head;
        bool _shown;
        bool _useLeft;

        public bool IsShown => _shown;

        static double Now => Time.realtimeSinceStartupAsDouble;

        void Update()
        {
            var core = _client != null ? _client.Core : null;
            if (core == null || _client.Content == null) { Hide(); return; }
            var tracker = core.Tracker;
            bool inTutorial = tracker.State.CurrentValue == RoundState.Tutorial && tracker.IsParticipant.CurrentValue;
            var item = ClassifyOwn(core);
            bool has = inTutorial && core.Items.CurrentValue != null && core.Items.CurrentValue.IsPractice && core.Items.CurrentValue.Get(core.OwnPracticeIndex) != null;
            bool show = _judge.ShouldShow(tracker.RoundId, inTutorial, has, item, core.Params.practice.hintSeconds, Now);
            if (!show) { Hide(); return; }
            if (!_shown) Show(core);
            Follow();
        }

        PracticeItemState ClassifyOwn(SessionClientCore core)
        {
            var table = core.Items.CurrentValue;
            if (table == null || !table.IsPractice) return PracticeItemState.None;
            // 「持てたか」だけを見るので、納品場所の範囲は見ない
            return PracticeGuide.Classify(table.Get(core.OwnPracticeIndex), false);
        }

        void Show(SessionClientCore core)
        {
            if (_rightModel == null || _leftModel == null || _material == null) return;
            _shown = true;
            _root = new GameObject("PracticeHintHand");
            _root.transform.SetParent(transform, false);
            _right = Build(_rightModel, "R_");
            _left = Build(_leftModel, "L_");
            _useLeft = false;
            _left.Root.SetActive(false);
            var tgo = new GameObject("HintText");
            tgo.transform.SetParent(_root.transform, false);
            _text = tgo.AddComponent<TextMeshPro>();
            if (_font != null) _text.font = _font;
            _text.alignment = TextAlignmentOptions.Center;
            _text.textWrappingMode = TextWrappingModes.NoWrap;
            _text.fontSize = _textSize;
            _text.color = Color.white;
            _text.rectTransform.sizeDelta = new Vector2(1.6f, 0.3f);
            _text.text = _client.Content.text.Get(ThTextId.GripBeam);
            Apply(0f);
            // 「開く → 握る」を往復する（DOTween。ホストの Update では動かさない）
            _loop = DOVirtual.Float(0f, 1f, _halfCycleSeconds, Apply).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetLink(_root);
        }

        HandInstance Build(GameObject model, string prefix)
        {
            var inst = new HandInstance { Root = Instantiate(model, _root.transform) };
            inst.Root.name = prefix + "HintModel";
            inst.Root.transform.localScale = Vector3.one * _scale;
            foreach (var c in inst.Root.GetComponentsInChildren<Collider>(true)) Destroy(c);   // 判定・物理に影響させない（FBX に無いが念のため）
            foreach (var r in inst.Root.GetComponentsInChildren<Renderer>(true))
            {
                r.sharedMaterial = _material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            foreach (var finger in Fingers)
                for (int i = 0; i < Segments.Length; i++)
                    AddJoint(inst, prefix + finger + Segments[i], FingerCurl[i], Vector3.right);
            AddJoint(inst, prefix + "ThumbProximal", 40f, Vector3.up);   // 親指は内側へ（軸と向きは見た目で調整した値）
            AddJoint(inst, prefix + "ThumbDistal", 45f, Vector3.right);
            return inst;
        }

        static void AddJoint(HandInstance inst, string name, float curl, Vector3 axis)
        {
            Transform t = null;
            foreach (var c in inst.Root.GetComponentsInChildren<Transform>(true)) if (c.name == name) { t = c; break; }
            if (t == null) return;
            var rest = t.localRotation;
            inst.Joints.Add(new Joint { T = t, Open = rest * Quaternion.AngleAxis(OpenExtension, axis), Closed = rest * Quaternion.AngleAxis(curl, axis) });
        }

        /// <summary>0 = 開く、1 = 握る（DOTween から呼ばれる）</summary>
        void Apply(float t)
        {
            var h = _useLeft ? _left : _right;
            if (h == null) return;
            foreach (var j in h.Joints) if (j.T != null) j.T.localRotation = Quaternion.Slerp(j.Open, j.Closed, t);
            // 切り替え前の手もそろえておく（切り替えた瞬間に飛ばない）
            var other = _useLeft ? _right : _left;
            if (other != null) foreach (var j in other.Joints) if (j.T != null) j.T.localRotation = Quaternion.Slerp(j.Open, j.Closed, t);
        }

        /// <summary>見えている手（右を優先、見えなければ左）の横に置く。両方見失っていれば頭の前やや下</summary>
        void Follow()
        {
            if (_root == null) return;
            if (_head == null) { var cam = Camera.main; if (cam == null) return; _head = cam.transform; }
            var right = _treasure != null ? _treasure.Right : null;
            var left = _treasure != null ? _treasure.Left : null;
            bool rightOk = right != null && right.Tracker != null && right.Tracker.IsTracked.CurrentValue && right.Tracker.HasPalm;
            bool leftOk = left != null && left.Tracker != null && left.Tracker.IsTracked.CurrentValue && left.Tracker.HasPalm;
            var yaw = Quaternion.Euler(0f, _head.eulerAngles.y, 0f);
            Vector3 pos;
            bool useLeft;
            if (rightOk)
            {
                useLeft = false;
                pos = right.Tracker.PalmPose.position + yaw * Vector3.right * _sideOffset;
            }
            else if (leftOk)
            {
                useLeft = true;
                pos = left.Tracker.PalmPose.position + yaw * Vector3.left * _sideOffset;
            }
            else
            {
                useLeft = false;
                pos = _head.position + yaw * _headOffset;
            }
            if (useLeft != _useLeft)
            {
                _useLeft = useLeft;
                _right.Root.SetActive(!useLeft);
                _left.Root.SetActive(useLeft);
            }
            // 指先を前（頭の向きの水平）に向け、手の甲を上にして、自分の手を見たときと同じ向きに見せる
            _root.transform.SetPositionAndRotation(pos, yaw);
            if (_text != null)
            {
                _text.transform.position = pos + Vector3.down * _textDrop;
                var dir = _text.transform.position - _head.position;
                if (dir.sqrMagnitude > 1e-6f) _text.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }
        }

        void Hide()
        {
            if (!_shown) return;
            _shown = false;
            _loop?.Kill();
            _loop = null;
            if (_root != null) Destroy(_root);
            _root = null; _right = null; _left = null; _text = null;
        }

        void OnDestroy() { _loop?.Kill(); }
    }
}
