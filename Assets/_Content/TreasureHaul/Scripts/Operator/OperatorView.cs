using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 運営UIの最小（F-OPS-2/3 の一部）。SessionHost の状態を表示し、「開始」「中断」「パラメータ再読み込み」を押せる。
    /// 判断は SessionHostCore / AbortConfirmGate にあり、ここは表示と配線だけ。
    /// </summary>
    public sealed class OperatorView : MonoBehaviour
    {
        [SerializeField] SessionHost _host;
        [SerializeField] TMP_Text _stateText;
        [SerializeField] TMP_Text _timeText;
        [SerializeField] TMP_Text _roundText;
        [SerializeField] TMP_Text _devicesText;
        [SerializeField] TMP_Text _versionText;
        [SerializeField] TMP_Text _warningText;
        [SerializeField] Button _startButton;
        [SerializeField] Button _abortButton;
        [SerializeField] TMP_Text _abortLabel;
        [SerializeField] Button _reloadButton;

        [SerializeField] TMP_Text _startLabel;
        [SerializeField] TMP_Text _reloadLabel;

        // #42: 保留・本編開始・合計額と目標額
        [SerializeField] Button _holdButton;
        [SerializeField] TMP_Text _holdLabel;
        [SerializeField, Tooltip("保留中の表示（「保留中（あと N 秒で自動解除）」）")] TMP_Text _holdingText;
        [SerializeField] Button _beginPlayButton;
        [SerializeField] TMP_Text _beginPlayLabel;
        [SerializeField] TMP_Text _totalText;

        // #44: 端末一覧（行は実行時にコードで作る）。未設定なら _devicesText と同じキャンバスの下に自動で作る
        [SerializeField, Tooltip("端末一覧の行を並べる場所。未設定なら _devicesText と同じキャンバスの下に自動で作る")] RectTransform _deviceListRoot;
        [SerializeField, Tooltip("「接続中の端末を書き出す」ボタン。未設定なら端末一覧の先頭に自動で作る")] Button _writeTemplateButton;

        ThTextTable _t;
        readonly Dictionary<string, DeviceRowView> _rows = new Dictionary<string, DeviceRowView>();
        TMP_Text _noDevicesText;

        static readonly Dictionary<RoundState, ThTextId> StateIds = new Dictionary<RoundState, ThTextId>
        {
            { RoundState.Waiting, ThTextId.StateWaiting },
            { RoundState.Opening, ThTextId.StateOpening },
            { RoundState.Tutorial, ThTextId.StateTutorial },
            { RoundState.Playing, ThTextId.StatePlaying },
            { RoundState.Result, ThTextId.StateResult },
        };

        AbortConfirmGate _abortGate;

        static double Now => Time.realtimeSinceStartupAsDouble;

        void Start()
        {
            var core = _host.Core;
            _t = _host.Content.text;
            _startLabel.text = _t.Get(ThTextId.OpStart);
            _reloadLabel.text = _t.Get(ThTextId.OpReloadParams);
            _holdLabel.text = _t.Get(ThTextId.OpHold);
            _beginPlayLabel.text = _t.Get(ThTextId.OpBeginPlay);
            _abortGate = new AbortConfirmGate(core.Params.operation.abortConfirmSeconds);

            core.State.Subscribe(s =>
            {
                _stateText.text = _t.Get(StateIds[s]);
                if (s == RoundState.Waiting) _abortGate.Reset();
            }).AddTo(this);
            core.RemainingSeconds.Select(r => Mathf.CeilToInt(r)).DistinctUntilChanged()
                .Subscribe(sec => _timeText.text = _t.Format(ThTextId.OpRemaining, sec)).AddTo(this);
            core.RoundId.Subscribe(id => _roundText.text = _t.Format(ThTextId.OpRoundId, id)).AddTo(this);
            core.ConnectedCount.CombineLatest(core.EligibleCount, (c, e) => (c, e))
                .Subscribe(t => _devicesText.text = _t.Format(ThTextId.OpDevices, t.c, t.e)).AddTo(this);
            core.ParamsVersionText.Subscribe(v =>
            {
                _versionText.text = _t.Format(ThTextId.OpParamsVersion, v);
                _abortGate.SetWindow(core.Params.operation.abortConfirmSeconds);   // 再読み込み後の値に合わせる
            }).AddTo(this);
            core.Warnings.Subscribe(w => _warningText.text = w.Length == 0 ? "" : _t.Get(ThTextId.OpWarning) + "\n" + string.Join("\n", w)).AddTo(this);

            // 押せる条件
            core.State.CombineLatest(core.EligibleCount, (s, e) => s == RoundState.Waiting && e >= 1)
                .Subscribe(v => _startButton.interactable = v).AddTo(this);
            core.State.Subscribe(s =>
            {
                _abortButton.interactable = s != RoundState.Waiting;
                _reloadButton.interactable = s == RoundState.Waiting;
            }).AddTo(this);

            // 保留: 練習中かつ（保留中、または保留できる）ときだけ。保留中はラベルが「保留を解除」
            core.State.CombineLatest(core.IsHeld, core.CanHold, (s, h, c) => s == RoundState.Tutorial && (h || c))
                .Subscribe(v => _holdButton.interactable = v).AddTo(this);
            core.IsHeld.Subscribe(h => _holdLabel.text = _t.Get(h ? ThTextId.OpHoldRelease : ThTextId.OpHold)).AddTo(this);
            core.IsHeld.CombineLatest(core.HoldRemainingSeconds, (h, r) => (h, r))
                .Subscribe(x => _holdingText.text = x.h ? _t.Format(ThTextId.OpHoldingFormat, x.r) : "").AddTo(this);
            core.State.Subscribe(s => _beginPlayButton.interactable = s == RoundState.Tutorial).AddTo(this);
            // 目標額は Playing 以降だけ（それより前は 0 と出る）
            core.Total.CombineLatest(core.Target, (tot, tgt) => (tot, tgt))
                .Subscribe(x => _totalText.text = _t.Format(ThTextId.OpTotalFormat, x.tot, x.tgt)).AddTo(this);

            BuildDeviceList();
            core.Devices.Subscribe(rows => ApplyDevices(rows)).AddTo(this);

            _holdButton.OnClickAsObservable().Subscribe(_ => _host.RequestToggleHold()).AddTo(this);
            _beginPlayButton.OnClickAsObservable().Subscribe(_ => _host.RequestBeginPlay()).AddTo(this);
            _startButton.OnClickAsObservable().Subscribe(_ => _host.RequestStart()).AddTo(this);
            _reloadButton.OnClickAsObservable().Subscribe(_ => _host.RequestReloadParams()).AddTo(this);
            _abortButton.OnClickAsObservable().Subscribe(_ =>
            {
                if (_abortGate.Press(Now)) _host.RequestAbort();
            }).AddTo(this);

            UpdateAbortLabelLoop(destroyCancellationToken).Forget();
        }

        // ---- 端末一覧（F-OPS-1/4） ----

        /// <summary>置き場所が未設定なら作り、先頭に見出しと「接続中の端末を書き出す」ボタンを置く</summary>
        void BuildDeviceList()
        {
            if (_deviceListRoot == null)
            {
                var parent = _devicesText != null ? _devicesText.transform.parent : transform;
                var go = new GameObject("DeviceList", typeof(RectTransform));
                go.transform.SetParent(parent, false);
                _deviceListRoot = (RectTransform)go.transform;
                _deviceListRoot.anchorMin = new Vector2(0.45f, 0.3f);
                _deviceListRoot.anchorMax = new Vector2(0.98f, 0.75f);
                _deviceListRoot.offsetMin = Vector2.zero;
                _deviceListRoot.offsetMax = Vector2.zero;
                var v = go.AddComponent<VerticalLayoutGroup>();
                v.spacing = 4f;
                v.childAlignment = TextAnchor.UpperLeft;
                v.childControlWidth = true;
                v.childControlHeight = true;
                v.childForceExpandWidth = true;
                v.childForceExpandHeight = false;
            }
            var title = DeviceRowView.Cell(_deviceListRoot, _devicesText, 400f);
            title.text = _t.Get(ThTextId.OpDeviceListTitle);
            if (_writeTemplateButton == null)
            {
                _writeTemplateButton = DeviceRowView.CreateButton(_deviceListRoot, _devicesText, 300f, out var label);
                label.text = _t.Get(ThTextId.OpWriteDeviceTemplate);
            }
            _writeTemplateButton.OnClickAsObservable().Subscribe(_ => _host.RequestWriteDeviceTemplate()).AddTo(this);
            _noDevicesText = DeviceRowView.Cell(_deviceListRoot, _devicesText, 400f);
            _noDevicesText.text = _t.Get(ThTextId.OpNoDevices);
        }

        void ApplyDevices(DeviceRowData[] rows)
        {
            if (_deviceListRoot == null) return;
            _noDevicesText.gameObject.SetActive(rows.Length == 0);
            for (int i = 0; i < rows.Length; i++)
            {
                var d = rows[i];
                if (!_rows.TryGetValue(d.DeviceId, out var row))
                {
                    row = DeviceRowView.Create(_deviceListRoot, _devicesText);
                    string id = d.DeviceId;
                    row.SpectatorButton.OnClickAsObservable().Subscribe(_ => _host.RequestToggleSpectator(id)).AddTo(row);
                    row.AlignmentButton.OnClickAsObservable().Subscribe(_ => _host.RequestToggleAlignment(id)).AddTo(row);
                    _rows[id] = row;
                }
                row.transform.SetAsLastSibling();   // 先頭から順に末尾へ回して、見出し・ボタンの後ろに一覧の順で並べる
                row.Apply(d, _t);
            }
        }

        async UniTaskVoid UpdateAbortLabelLoop(System.Threading.CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    _abortLabel.text = _t.Get(_abortGate.IsArmed(Now) ? ThTextId.OpAbortArmed : ThTextId.OpAbort);
                    await UniTask.Delay(TimeSpan.FromMilliseconds(100), cancellationToken: ct);
                }
            }
            catch (OperationCanceledException) { }
        }
    }
}
