using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 運営UIの端末一覧の 1 行（F-OPS-1/4）。プレハブを使わず、実行時にコードで作る（<see cref="Create"/>）。
    /// 表示する内容の判断は <see cref="DeviceRowData"/>（SessionHostCore が作る）にあり、ここは文字と色にするだけ。
    /// 列: 端末番号 / 接続 / 位置合わせ / 手 / 電池 / 練習 / 見学 / [見学ボタン] / [位置合わせ確認ボタン]
    /// </summary>
    public sealed class DeviceRowView : MonoBehaviour
    {
        static readonly Color NormalColor = Color.white;
        static readonly Color DimColor = new Color(0.6f, 0.6f, 0.6f, 1f);
        static readonly Color WarningColor = new Color(1f, 0.55f, 0.1f, 1f);
        static readonly Color AttentionColor = new Color(1f, 0.9f, 0.3f, 1f);

        TMP_Text _number, _connection, _alignment, _hand, _battery, _practice, _spectator;
        TMP_Text _spectatorLabel, _alignmentLabel;

        public string DeviceId { get; private set; } = "";
        public Button SpectatorButton { get; private set; }
        public Button AlignmentButton { get; private set; }

        /// <param name="style">フォント・文字の大きさ・色の見本（既存の運営UIの文字。日本語フォントを引き継ぐ）</param>
        public static DeviceRowView Create(Transform parent, TMP_Text style)
        {
            var go = new GameObject("DeviceRow", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var row = go.AddComponent<DeviceRowView>();
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            var rowLe = go.AddComponent<LayoutElement>();
            rowLe.preferredHeight = 40f;
            rowLe.minHeight = 40f;

            row._number = Cell(go.transform, style, 150f);
            row._connection = Cell(go.transform, style, 70f);
            row._alignment = Cell(go.transform, style, 190f);
            row._hand = Cell(go.transform, style, 70f);
            row._battery = Cell(go.transform, style, 70f);
            row._practice = Cell(go.transform, style, 110f);
            row._spectator = Cell(go.transform, style, 60f);
            row.SpectatorButton = CreateButton(go.transform, style, 150f, out row._spectatorLabel);
            row.AlignmentButton = CreateButton(go.transform, style, 200f, out row._alignmentLabel);
            return row;
        }

        public static TMP_Text Cell(Transform parent, TMP_Text style, float width)
        {
            var go = new GameObject("Cell", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            StyleLike(tmp, style);
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.raycastTarget = false;
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.minWidth = width;
            return tmp;
        }

        public static Button CreateButton(Transform parent, TMP_Text style, float width, out TMP_Text label)
        {
            var go = new GameObject("Button", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = new Color(0.25f, 0.25f, 0.3f, 1f);
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.minWidth = width;

            var lgo = new GameObject("Label", typeof(RectTransform));
            lgo.transform.SetParent(go.transform, false);
            var rt = (RectTransform)lgo.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            label = lgo.AddComponent<TextMeshProUGUI>();
            StyleLike(label, style);
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            return button;
        }

        static void StyleLike(TMP_Text tmp, TMP_Text style)
        {
            if (style != null)
            {
                tmp.font = style.font;
                tmp.fontSize = style.fontSize;
            }
            tmp.color = NormalColor;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
        }

        /// <summary>表示を更新する（一覧が変わったときだけ呼ぶ）</summary>
        public void Apply(DeviceRowData d, ThTextTable t)
        {
            DeviceId = d.DeviceId;
            _number.text = d.Number > 0
                ? t.Format(ThTextId.OpDeviceNumberFormat, d.Number)
                : t.Format(ThTextId.OpDeviceUnregisteredFormat, DeviceRowData.ShortId(d.DeviceId));
            _number.color = d.Number > 0 ? NormalColor : DimColor;
            _connection.text = t.Get(d.Connected ? ThTextId.OpDeviceConnected : ThTextId.OpDeviceDisconnected);
            _connection.color = d.Connected ? NormalColor : DimColor;
            _alignment.text = t.Get(d.AlignmentConfirmed ? ThTextId.OpAlignConfirmed : ThTextId.OpAlignUnconfirmed);
            _alignment.color = d.AlignmentConfirmed ? NormalColor : AttentionColor;
            _hand.text = t.Get(HandId(d.Hand));
            _hand.color = d.Hand == ThHandStatus.Both ? NormalColor : d.Hand == ThHandStatus.Unknown ? DimColor : AttentionColor;
            _battery.text = d.Battery >= 0 ? t.Format(ThTextId.OpBatteryFormat, d.Battery) : t.Get(ThTextId.OpBatteryUnknown);
            _battery.color = d.Battery < 0 ? DimColor : d.BatteryWarning ? WarningColor : NormalColor;
            _practice.text = !d.PracticeVisible ? "" : t.Get(d.PracticeDone ? ThTextId.OpPracticeDone : ThTextId.OpPracticeNotDone);
            _spectator.text = t.Get(d.Spectator ? ThTextId.OpSpectatorTag : ThTextId.OpParticipantTag);
            _spectator.color = d.Spectator ? AttentionColor : NormalColor;
            _spectatorLabel.text = t.Get(d.Spectator ? ThTextId.OpClearSpectator : ThTextId.OpSetSpectator);
            _alignmentLabel.text = t.Get(d.AlignmentConfirmed ? ThTextId.OpUnconfirmAlignment : ThTextId.OpConfirmAlignment);
            // 切断中の端末は切り替えても意味がない（再接続で未確認に戻る）ので、確認ボタンは接続中だけ
            AlignmentButton.interactable = d.Connected;
        }

        static ThTextId HandId(ThHandStatus h)
        {
            switch (h)
            {
                case ThHandStatus.Both: return ThTextId.OpHandBoth;
                case ThHandStatus.One: return ThTextId.OpHandOne;
                case ThHandStatus.None: return ThTextId.OpHandNone;
                default: return ThTextId.OpStatusUnknown;
            }
        }
    }
}
