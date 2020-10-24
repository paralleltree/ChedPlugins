using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using Ched.Core;
using Ched.Core.Events;
using Ched.Core.Notes;
using Ched.Plugins;

namespace ChedPlugins
{
    public class DuplicateNotesValidator : IScorePlugin
    {
        public string DisplayName => "重複ショートノーツチェック";

        public void Run(IScorePluginArgs args)
        {
            var score = args.GetCurrentScore();
            var messages = CheckDuplicateNotes(score).ToList();

            if (messages.Count == 0)
            {
                MessageBox.Show("重複するショートノーツはありません。", DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                var sb = new StringBuilder();
                sb.AppendLine($"{messages.Count}箇所に重複するショートノーツが存在します。");
                for (int i = 0; i < messages.Count; i++)
                {
                    if (i == 5)
                    {
                        // 5件以上は抑制
                        sb.AppendLine($"残り{messages.Count - i}件を省略します。");
                        break;
                    }
                    sb.AppendLine(messages[i]);
                }
                MessageBox.Show(sb.ToString(), DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private IEnumerable<string> CheckDuplicateNotes(Score score)
        {
            var notes = score.Notes;
            var shortNotes = notes.Taps
                .Cast<TappableBase>()
                .Concat(notes.ExTaps)
                .Concat(notes.Flicks)
                .Concat(notes.Damages);

            var overlaps = shortNotes
                .GroupBy(p => p.Tick)
                .SelectMany(p =>
                {
                    int[] lanes = new int[Constants.LanesCount + 1];
                    foreach (var item in p)
                    {
                        lanes[item.LaneIndex] += 1;
                        lanes[item.LaneIndex + item.Width] -= 1;
                    }
                    for (int i = 0; i < Constants.LanesCount - 1; i++) lanes[i + 1] += lanes[i];
                    var results = new List<(int Tick, int LaneIndex, int Width)>();
                    for (int i = 0; i < Constants.LanesCount; i++)
                    {
                        if (lanes[i] <= 1) continue;
                        int begin = i;
                        while (i < Constants.LanesCount && lanes[i] > 1) i++;
                        results.Add((p.Key, begin, i - begin));
                    }
                    return results;
                })
                .OrderBy(p => p.Tick);

            var barIndexCalculator = new BarIndexCalculator(score.TicksPerBeat, score.Events.TimeSignatureChangeEvents);

            foreach (var item in overlaps)
            {
                var pos = barIndexCalculator.GetBarPositionFromTick(item.Tick);
                yield return $"{pos.BarIndex + 1}小節 (Tick: {pos.TickOffset}, レーン: {item.LaneIndex}" + (item.Width > 1 ? $"-{item.LaneIndex + item.Width - 1})" : ")");
            }
        }
    }

    /// <summary>
    /// 拍子変更イベントからTickに対応する小節位置を求めるクラスです。
    /// </summary>
    /// <remarks>SusExporterのものと同等</remarks>
    public class BarIndexCalculator
    {
        private int TicksPerBeat { get; }
        private int BarTick => 4 * TicksPerBeat;
        private List<TimeSignatureItem> ReversedTimeSignatures { get; } = new List<TimeSignatureItem>();

        /// <summary>
        /// TicksPerBeatと拍子変更イベントから<see cref="BarIndexCalculator"/>のインスタンスを初期化します。
        /// </summary>
        /// <param name="ticksPerBeat">譜面のTicksPerBeat</param>
        /// <param name="sigs">拍子変更イベントを表す<see cref="TimeSignatureChangeEvent"/>のリスト</param>
        public BarIndexCalculator(int ticksPerBeat, IEnumerable<TimeSignatureChangeEvent> sigs)
        {
            TicksPerBeat = ticksPerBeat;
            var ordered = sigs.OrderBy(p => p.Tick).ToList();
            var dic = new SortedDictionary<int, TimeSignatureItem>();
            int pos = 0;
            int barIndex = 0;

            for (int i = 0; i < ordered.Count; i++)
            {
                var item = new TimeSignatureItem(pos, barIndex, ordered[i]);

                // 時間逆順で追加
                if (dic.ContainsKey(-pos)) dic[-pos] = item;
                else dic.Add(-pos, item);

                if (i < ordered.Count - 1)
                {
                    int barLength = BarTick * ordered[i].Numerator / ordered[i].Denominator;
                    int duration = ordered[i + 1].Tick - pos;
                    pos += duration / barLength * barLength;
                    barIndex += duration / barLength;
                }
            }

            // TODO: sorted?
            ReversedTimeSignatures = dic.Values.ToList();
        }

        /// <summary>
        /// 指定のTickに対応する小節位置を取得します。
        /// </summary>
        /// <param name="tick">小節位置を取得するTick</param>
        /// <returns>Tickに対応する小節位置を表す<see cref="BarPosition"/></returns>
        public BarPosition GetBarPositionFromTick(int tick)
        {
            foreach (var item in ReversedTimeSignatures)
            {
                if (tick < item.StartTick) continue;
                var sig = item.TimeSignature;
                int barLength = BarTick * sig.Numerator / sig.Denominator;
                int ticksFromSignature = tick - item.StartTick;
                int barsCount = ticksFromSignature / barLength;
                int barIndex = item.StartBarIndex + barsCount;
                int tickOffset = ticksFromSignature - barsCount * barLength;
                return new BarPosition(barIndex, tickOffset);
            }

            throw new InvalidOperationException();
        }

        public TimeSignatureChangeEvent GetTimeSignatureFromBarIndex(int barIndex)
        {
            foreach (var item in ReversedTimeSignatures)
            {
                if (barIndex < item.StartBarIndex) continue;
                return item.TimeSignature;
            }

            throw new InvalidOperationException();
        }

        /// <summary>
        /// Tickに対応する小節位置を表します。
        /// </summary>
        public class BarPosition
        {
            /// <summary>
            /// 小節のインデックスを取得します。このフィールドは0-basedです。
            /// </summary>
            public int BarIndex { get; }

            /// <summary>
            /// 小節におけるTickのオフセットを表します。
            /// </summary>
            public int TickOffset { get; }

            public BarPosition(int barIndex, int tickOffset)
            {
                BarIndex = barIndex;
                TickOffset = tickOffset;
            }
        }

        public class TimeSignatureItem
        {
            public int StartTick { get; }
            public int StartBarIndex { get; }
            public TimeSignatureChangeEvent TimeSignature { get; }

            public TimeSignatureItem(int startTick, int startBarIndex, TimeSignatureChangeEvent timeSignature)
            {
                StartTick = startTick;
                StartBarIndex = startBarIndex;
                TimeSignature = timeSignature;
            }
        }
    }
}
