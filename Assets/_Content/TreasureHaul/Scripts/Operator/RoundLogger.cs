using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>CSV に出す文言（見出し・完了／中断・はい／いいえ）。本番は ThTextTable から作る</summary>
    public sealed class CsvLabels
    {
        public string[] RoundsHeader;
        public string[] PlayersHeader;
        public string Completed;
        public string Aborted;
        public string Yes;
        public string No;

        public static CsvLabels From(ThTextTable t) => new CsvLabels
        {
            RoundsHeader = t.Get(ThTextId.CsvRoundsHeader).Split(','),
            PlayersHeader = t.Get(ThTextId.CsvPlayersHeader).Split(','),
            Completed = t.Get(ThTextId.CsvCompleted),
            Aborted = t.Get(ThTextId.CsvAborted),
            Yes = t.Get(ThTextId.CsvYes),
            No = t.Get(ThTextId.CsvNo),
        };
    }

    /// <summary>CSV の組み立て（純粋関数。企画書 10.3、要件 F-OPS-7）。1 行 = 改行（CRLF）で終わる文字列。</summary>
    public static class RoundCsv
    {
        public const string NewLine = "\r\n";

        /// <summary>カンマ・改行・ダブルクォートを含む値は、ダブルクォートで囲み、中のダブルクォートは 2 つにする。null は空</summary>
        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        public static string Line(IEnumerable<string> values)
        {
            var sb = new StringBuilder();
            bool first = true;
            foreach (var v in values)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(Escape(v));
            }
            sb.Append(NewLine);
            return sb.ToString();
        }

        /// <summary>ファイル名の日付（YYYYMMDD。回の開始時刻のローカル日付）</summary>
        public static string DateKey(DateTimeOffset startedAt) => startedAt.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        public static string RoundsFileName(DateTimeOffset startedAt) => "rounds_" + DateKey(startedAt) + ".csv";
        public static string PlayersFileName(DateTimeOffset startedAt) => "players_" + DateKey(startedAt) + ".csv";

        static string Iso(DateTimeOffset t) => t.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        static string Int(int v) => v.ToString(CultureInfo.InvariantCulture);
        static string Sec(double? v) => v.HasValue ? v.Value.ToString("0.0", CultureInfo.InvariantCulture) : "";

        public static string RoundsHeaderLine(CsvLabels l) => Line(l.RoundsHeader);
        public static string PlayersHeaderLine(CsvLabels l) => Line(l.PlayersHeader);

        /// <summary>rounds の 1 行（15 列）</summary>
        public static string RoundsRow(RoundRecord r, CsvLabels l) => Line(new[]
        {
            Iso(r.StartedAt),
            r.RoundId.ToString(CultureInfo.InvariantCulture),
            r.Aborted ? l.Aborted : l.Completed,
            r.Aborted ? r.AbortedState.ToString() : "",
            Int(r.ParticipantCount),
            r.Mode,
            r.Extras,
            r.GripStyle,
            r.ParamsVersion,
            r.WasHeld ? l.Yes : l.No,
            r.HeldSeconds.ToString("0.0", CultureInfo.InvariantCulture),
            Int(r.CollectedAmount),
            Int(r.TargetAmount),
            r.TargetReached ? l.Yes : l.No,
            Int(r.DamageTotal),
        });

        /// <summary>players の 1 行（11 列）。端末番号 0（未登録）・色なし・無い秒は空欄</summary>
        public static string PlayersRow(RoundRecord r, PlayerRecord p) => Line(new[]
        {
            r.RoundId.ToString(CultureInfo.InvariantCulture),
            p.Number > 0 ? Int(p.Number) : "",
            p.DeviceId,
            p.Color >= 0 ? Int(p.Color) : "",
            Sec(p.PracticeDoneSeconds),
            Sec(p.FirstGrabSeconds),
            Sec(p.FirstCollectSeconds),
            Int(p.CollectedCount),
            Int(p.CollectedAmount),
            Int(p.DamageCount),
            Int(p.DamageAmount),
        });
    }

    /// <summary>
    /// 1 回ごとの記録を CSV に追記する薄い層（F-OPS-7）。<c>persistentDataPath/logs/rounds_YYYYMMDD.csv</c> に 1 行、
    /// <c>players_YYYYMMDD.csv</c> に参加端末ごとに 1 行。ファイルが無い（または空）なら見出し行から書く。UTF-8（BOM 付き）で、
    /// 1 回ごとに開いて書いて閉じる（Flush）。書けなかったら例外を出さず警告の文字列を返す（進行を止めない）。
    /// </summary>
    public sealed class RoundLogger
    {
        public const string DirectoryName = "logs";

        readonly string _logDirectory;
        readonly CsvLabels _labels;

        /// <param name="dataDirectory">persistentDataPath（テストでは一時フォルダ）</param>
        public RoundLogger(string dataDirectory, CsvLabels labels)
        {
            _logDirectory = Path.Combine(dataDirectory, DirectoryName);
            _labels = labels;
        }

        /// <summary>1 回ぶんを書く。全部書けたら null、書けなかったファイルがあれば警告（rounds が失敗しても players は試す）</summary>
        public string Write(RoundRecord r)
        {
            var warnings = new List<string>();
            try
            {
                Directory.CreateDirectory(_logDirectory);
            }
            catch (Exception e)
            {
                return $"記録フォルダを作れません（{_logDirectory}）: {e.Message}";
            }
            Append(Path.Combine(_logDirectory, RoundCsv.RoundsFileName(r.StartedAt)), RoundCsv.RoundsHeaderLine(_labels), RoundCsv.RoundsRow(r, _labels), warnings);
            var players = new StringBuilder();
            foreach (var p in r.Players()) players.Append(RoundCsv.PlayersRow(r, p));
            if (players.Length > 0)
                Append(Path.Combine(_logDirectory, RoundCsv.PlayersFileName(r.StartedAt)), RoundCsv.PlayersHeaderLine(_labels), players.ToString(), warnings);
            return warnings.Count == 0 ? null : string.Join(" / ", warnings);
        }

        static void Append(string path, string header, string rows, List<string> warnings)
        {
            try
            {
                bool needHeader = !File.Exists(path) || new FileInfo(path).Length == 0;
                using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    var sb = new StringBuilder();
                    if (needHeader)
                    {
                        // Excel で文字化けしないよう、新しいファイルの先頭にだけ BOM を書く
                        var bom = new UTF8Encoding(true).GetPreamble();
                        fs.Write(bom, 0, bom.Length);
                        sb.Append(header);
                    }
                    sb.Append(rows);
                    var bytes = new UTF8Encoding(false).GetBytes(sb.ToString());
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(true);
                }
            }
            catch (Exception e)
            {
                warnings.Add($"{Path.GetFileName(path)} に書けません: {e.Message}");
            }
        }
    }
}
