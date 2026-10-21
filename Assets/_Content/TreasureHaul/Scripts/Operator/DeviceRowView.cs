using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 運営UIの端末一覧の 1 行（F-OPS-1/4）。プレハブを使わず、実行時にコードで作る（<see cref="Create"/>）。
    /// 表示する内容の判断は <see cref="DeviceRowData"/>（SessionHostCore が作る）にあり、ここは文字と色にするだけ。
    /// 列: 端末番号 / 接続 / 位置合わせ / 手 / 電池 / 練習 / 見学 / [見学ボタン] / [位置合わせ確認ボタン]。列の名前は見出しの行（<see cref="CreateHeader"/>）に出す
    /// </summary>
    public sealed class DeviceRowView : MonoBehaviour
    {
        static readonly Color NormalColor = Color.white;
        static readonly Color DimColor = new Color(0.6f, 0.6f, 0.6f, 1f);
        static readonly Color WarningColor = new Color(1f, 0.55f, 0.1f, 1f);
        static readonly Color AttentionColor = new Color(1f, 0.9f, 0.3f, 1f);

        /// <summary>一覧の文字の大きさ（キャンバス基準 1280×720 で。1080p の画面で 30px）</summary>
        public const float FontSize = 20f;
        /// <summary>1 行の高さ・ボタンの高さ</summary>
        public const float RowHeight = 32f;
        const float ButtonHeight = 30f;
        const float Spacing = 6f;
        /// <summary>
        /// 列幅（端末番号・接続・位置合わせ・手・電池・練習・見学・[見学ボタン]・[確認ボタン]）。文字 20 で最も長い文言が入る幅。
        /// 合計 約 740 で、運営UIの右側（16:10 の画面で約 750）に収まる。文言を長くしたら合わせて広げる。
        /// </summary>
        static readonly float[] ColumnWidths = { 135f, 44f, 104f, 44f, 52f, 44f, 44f, 112f, 112f };
        const int TextColumns = 7;

        TMP_Text _number, _connection, _alignment, _hand, _battery, _practice, _spectator;
        TMP_Text _spectatorLabel, _alignmentLabel;

        public string DeviceId { get; private set; } = "";
        public Button SpectatorButton { get; private set; }
        public Button AlignmentButton { get; private set; }

        /// <param name="style">フォントの見本（既存の運営UIの文字。日本語フォントを引き継ぐ）</param>
        public static DeviceRowView Create(Transform parent, TMP_Text style)
        {
            var go = CreateRow(parent, "DeviceRow");
            var row = go.AddComponent<DeviceRowView>();
            var c = new TMP_Text[TextColumns];
            for (int i = 0; i < TextColumns; i++) c[i] = Cell(go.transform, style, ColumnWidths[i]);
            row._number = c[0];
            row._connection = c[1];
            row._alignment = c[2];
            row._hand = c[3];
            row._battery = c[4];
            row._practice = c[5];
            row._spectator = c[6];
            row.SpectatorButton = CreateButton(go.transform, style, ColumnWidths[7], out row._spectatorLabel);
            row.AlignmentButton = CreateButton(go.transform, style, ColumnWidths[8], out row._alignmentLabel);
            return row;
        }

        /// <summary>列見出しの行（<paramref name="headers"/> はカンマ区切りの見出し。ボタンの列は空ける）</summary>
        public static void CreateHeader(Transform parent, TMP_Text style, string headers)
        {
            var go = CreateRow(parent, "DeviceHeader");
            var names = headers.Split(',');
            for (int i = 0; i < ColumnWidths.Length; i++)
            {
                var cell = Cell(go.transform, style, ColumnWidths[i]);
                cell.text = i < TextColumns && i < names.Length ? names[i] : "";
                cell.color = DimColor;
            }
        }

        /// <summary>横に並べる行（高さ <see cref="RowHeight"/>）</summary>
        public static GameObject CreateRow(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = Spacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            var rowLe = go.AddComponent<LayoutElement>();
            rowLe.preferredHeight = RowHeight;
            rowLe.minHeight = RowHeight;
            return go;
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
            le.preferredHeight = RowHeight;
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
            // 高さも決める（親のレイアウトが子の高さを決めるので、無いと Image の推奨高さ 0 になって押せない）
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.minWidth = width;
            le.preferredHeight = ButtonHeight;
            le.minHeight = ButtonHeight;

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
            if (style != null) tmp.font = style.font;
            tmp.fontSize = FontSize;
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
