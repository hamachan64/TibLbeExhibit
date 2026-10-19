using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace STYLY.TIB_Template.TreasureHaul.Tests
{
    public class ThContentTests
    {
        const string SetPath = "Assets/_Content/TreasureHaul/Settings/ThContentSet.asset";
        const string TextPath = "Assets/_Content/TreasureHaul/Settings/ThTextTable.asset";
        const string FontPath = "Assets/_Content/Fonts/Noto_Sans_JP/NotoSansJP-VariableFont_wght SDF.asset";

        static ThTextTable Table() => AssetDatabase.LoadAssetAtPath(TextPath, typeof(ThTextTable)) as ThTextTable;

        [Test] public void 組み込みのアセットに重複と文言の欠けが無い()
        {
            var set = AssetDatabase.LoadAssetAtPath(SetPath, typeof(ThContentSet)) as ThContentSet;
            Assert.IsNotNull(set);
            CollectionAssert.IsEmpty(set.Validate(), string.Join("\n", set.Validate()));
        }

        static ThTextTable MakeTable(params (ThTextId id, string text)[] items)
        {
            var t = ScriptableObject.CreateInstance<ThTextTable>();
            t.entries = new List<ThTextEntry>();
            foreach (var (id, text) in items) t.entries.Add(new ThTextEntry(id, text));
            return t;
        }

        [Test] public void Formatは書式どおりに桁区切りで埋める()
        {
            var t = MakeTable(
                (ThTextId.PriceFormat, "{0:N0}G"),
                (ThTextId.PracticeSuccessFormat, "+{0:N0}G！"),
                (ThTextId.DamageFormat, "-{0:N0}G"),
                (ThTextId.CollectDamagedFormat, "+{0:N0}G 傷物"),
                (ThTextId.ResultLossFormat, "今回の損害：{0:N0}G"),
                (ThTextId.OpDevices, "端末 接続中 {0} / 参加できる {1}"));
            Assert.AreEqual("1,200G", t.Format(ThTextId.PriceFormat, 1200));
            Assert.AreEqual("+1,500G！", t.Format(ThTextId.PracticeSuccessFormat, 1500));
            Assert.AreEqual("-90G", t.Format(ThTextId.DamageFormat, 90));
            Assert.AreEqual("+560G 傷物", t.Format(ThTextId.CollectDamagedFormat, 560));
            Assert.AreEqual("今回の損害：1,200G", t.Format(ThTextId.ResultLossFormat, 1200));
            Assert.AreEqual("端末 接続中 2 / 参加できる 1", t.Format(ThTextId.OpDevices, 2, 1));
            Object.DestroyImmediate(t);
        }

        // ID ごとの引数の数（0 = 書式なし）。新しい ID を足したらここにも足す
        static readonly Dictionary<ThTextId, int> ArgCounts = new Dictionary<ThTextId, int>
        {
            { ThTextId.OpRemaining, 1 }, { ThTextId.OpRoundId, 1 }, { ThTextId.OpDevices, 2 }, { ThTextId.OpParamsVersion, 1 },
            { ThTextId.PriceFormat, 1 }, { ThTextId.PracticeSuccessFormat, 1 }, { ThTextId.DamageFormat, 1 },
            { ThTextId.CollectFormat, 1 }, { ThTextId.CollectDamagedFormat, 1 }, { ThTextId.ResultLossFormat, 1 }, { ThTextId.BoardTotalFormat, 1 },
        };

        [Test] public void 組み込みアセットの各文言の書式は使う引数の数で例外にならない()
        {
            foreach (var e in Table().entries)
            {
                int n = ArgCounts.TryGetValue(e.id, out var c) ? c : 0;
                var args = new object[n];
                for (int i = 0; i < n; i++) args[i] = 1234;
                Assert.DoesNotThrow(() => string.Format(System.Globalization.CultureInfo.InvariantCulture, e.text, args), e.id.ToString());
            }
        }

        [Test] public void 無いIDはID名を返して警告は1回だけ()
        {
            var t = ScriptableObject.CreateInstance<ThTextTable>();
            t.entries = new List<ThTextEntry>();
            LogAssert.Expect(LogType.Warning, new Regex("StateWaiting"));
            Assert.AreEqual("StateWaiting", t.Get(ThTextId.StateWaiting));
            Assert.AreEqual("StateWaiting", t.Get(ThTextId.StateWaiting));   // 2 回目は警告なし（LogAssert が余分な警告を失敗にする）
            Object.DestroyImmediate(t);
        }

        [Test] public void Validateは重複と空を検出する()
        {
            var t = ScriptableObject.CreateInstance<ThTextTable>();
            t.entries = new List<ThTextEntry> { new ThTextEntry(ThTextId.StateWaiting, "待機"), new ThTextEntry(ThTextId.StateWaiting, "x"), new ThTextEntry(ThTextId.StateResult, " ") };
            var errors = t.Validate();
            Assert.IsTrue(errors.Exists(e => e.Contains("重複")));
            Assert.IsTrue(errors.Exists(e => e.Contains("空")));
            Assert.IsTrue(errors.Exists(e => e.Contains("欠け")));
            Object.DestroyImmediate(t);
        }

        [Test] public void 音と演出の一覧は全IDを持ち重複が無い()
        {
            var set = AssetDatabase.LoadAssetAtPath(SetPath, typeof(ThContentSet)) as ThContentSet;
            CollectionAssert.IsEmpty(set.audio.Validate(), string.Join("\n", set.audio.Validate()));
            CollectionAssert.IsEmpty(set.fx.Validate(), string.Join("\n", set.fx.Validate()));
            Assert.AreEqual(System.Enum.GetValues(typeof(ThSoundId)).Length, set.audio.entries.Count);
            Assert.AreEqual(System.Enum.GetValues(typeof(ThFxId)).Length, set.fx.entries.Count);
        }

        [Test] public void 全文言の文字が本体フォントとフォールバックで表示できる()
        {
            var font = AssetDatabase.LoadAssetAtPath(FontPath, typeof(TMP_FontAsset)) as TMP_FontAsset;
            Assert.IsNotNull(font);
            var sb = new System.Text.StringBuilder();
            foreach (var e in Table().entries) sb.Append(Regex.Replace(e.text, @"\{[^}]*\}", ""));
            sb.Append("0123456789,G+-");
            var chars = new HashSet<char>();
            foreach (var ch in sb.ToString()) if (!char.IsWhiteSpace(ch)) chars.Add(ch);
            var missing = new List<char>();
            foreach (var ch in chars) if (!font.HasCharacter(ch, true)) missing.Add(ch);   // 読み取りのみ（フォントは書き換えない）
            Assert.AreEqual(0, missing.Count, "表示できない文字: " + new string(missing.ToArray()) + "（Tools/Treasure Haul/文言の文字をフォントに足す を実行）");
        }
    }
}
