using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Ched.Core.Notes;
using Ched.Plugins;

namespace ScoreSplitter
{
    namespace Ched.Plugins
    {
        public class SplitScore : IScorePlugin
        {
            public string DisplayName => "割るぜぇ～超割るぜぇ～";

            public void Run(IScorePluginArgs args)
            {
                var score = args.GetCurrentScore();
                var airDic = score.Notes.Airs.ToDictionary(p => p.ParentNote, p => p);
                var airActionDic = score.Notes.AirActions.ToDictionary(p => p.ParentNote, p => p);
                Action<IAirable, IAirable> duplicateAir = (orig, dup) =>
                {
                    if (airDic.ContainsKey(orig))
                    {
                        var air = new Air(dup)
                        {
                            VerticalDirection = airDic[orig].VerticalDirection,
                            HorizontalDirection = airDic[orig].HorizontalDirection
                        };
                        score.Notes.Airs.Add(air);
                    }
                    if (airActionDic.ContainsKey(orig))
                    {
                        var airAction = new AirAction(dup);
                        airAction.ActionNotes.AddRange(airActionDic[orig].ActionNotes.Select(p => new AirAction.ActionNote(airAction) { Offset = p.Offset }));
                        score.Notes.AirActions.Add(airAction);
                    }
                };

                foreach (var tap in score.Notes.Taps.ToList())
                {
                    foreach (var sp in Enumerable.Range(1, tap.Width - 1).Select(p => new Tap() { Tick = tap.Tick, LaneIndex = tap.LaneIndex + p, Width = 1 }))
                    {
                        score.Notes.Taps.Add(sp);
                        duplicateAir(tap, sp);
                    }
                    tap.Width = 1;
                }

                foreach (var tap in score.Notes.ExTaps.ToList())
                {
                    foreach (var sp in Enumerable.Range(1, tap.Width - 1).Select(p => new ExTap() { Tick = tap.Tick, LaneIndex = tap.LaneIndex + p, Width = 1 }))
                    {
                        score.Notes.ExTaps.Add(sp);
                        duplicateAir(tap, sp);
                    }
                    tap.Width = 1;
                }

                foreach (var flick in score.Notes.Flicks.Where(p => p.Width >= 2).ToList())
                {
                    foreach (var sp in Enumerable.Range(1, flick.Width / 2 - 1).Select(p => new Flick() { Tick = flick.Tick, LaneIndex = flick.LaneIndex + 2 * p, Width = 2 }))
                    {
                        score.Notes.Flicks.Add(sp);
                        duplicateAir(flick, sp);
                    }
                    flick.Width = 2;
                }

                foreach (var hold in score.Notes.Holds.ToList())
                {
                    foreach (var sp in Enumerable.Range(1, hold.Width - 1).Select(p => new Hold() { StartTick = hold.StartTick, Duration = hold.Duration, LaneIndex = hold.LaneIndex + p, Width = 1 }))
                    {
                        score.Notes.Holds.Add(sp);
                        duplicateAir(hold.EndNote, sp.EndNote);
                    }
                    hold.Width = 1;
                }

                foreach (var slide in score.Notes.Slides.Where(p => p.StepNotes.All(q => q.WidthChange == 0)).ToList())
                {
                    var slides = Enumerable.Range(1, slide.StartWidth - 1)
                        .Select(p =>
                        {
                            var s = new Slide() { StartTick = slide.StartTick, StartLaneIndex = slide.StartLaneIndex + p, StartWidth = 1 };
                            s.StepNotes.AddRange(slide.StepNotes.OrderBy(q => q.TickOffset).Select(q => new Slide.StepTap(s) { IsVisible = q.IsVisible, TickOffset = q.TickOffset, LaneIndexOffset = q.LaneIndexOffset }));
                            return s;
                        });
                    foreach (var sp in slides)
                    {
                        score.Notes.Slides.Add(sp);
                        duplicateAir(slide.StepNotes.OrderByDescending(p => p.TickOffset).First(), sp.StepNotes[sp.StepNotes.Count - 1]);
                    }
                    slide.StartWidth = 1;
                }

                args.UpdateScore(score);
            }
        }
    }
}
