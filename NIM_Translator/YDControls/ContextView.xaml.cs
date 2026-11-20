using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using NIMEngine;
using NIMEngine.Engine;
using NIMEngine.Language;

namespace NIM.YDControls
{
    public partial class ContextView : UserControl
    {
        private class DemoCase
        {
            public string Name;
            public Languages Lang;
            public string Head;
            public string[] Candidates;
            public override string ToString() { return Name; }
        }

        private class RowVisual
        {
            public HashSet<string> Sig;                      // from TextTokenizer.BuildTokenSignature
            public List<string> Order = new List<string>();  // display order
            public Dictionary<string, Border> Chips = new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);
            public WrapPanel ChipPanel;
            public TextBlock CountText;
            public Border ResultPill;
            public TextBlock ResultText;
            public StackPanel Body;
        }

        private const int CellW = 30;
        private const int MaxGram = TextTokenizer.MaxGram;
        private const double BaseY = 70;

        private CancellationTokenSource _cts;
        private double _boost = 1;

        private static readonly Brush Accent = Br("#FF4FC3F7");
        private static readonly Brush ChipBg = Br("#FF1B1B1B");
        private static readonly Brush ChipBd = Br("#FF484848");
        private static readonly Brush HitBg = Br("#334FC3F7");
        private static readonly Brush CellHi = Br("#444FC3F7");
        private static readonly Brush Good = Br("#FF6FD58C");
        private static readonly Brush Bad = Br("#FFE5534B");
        private static readonly Brush Dim = Br("#FF6A6A6A");

        private static readonly char[] WordSeparators =
            { ' ', '.', ',', '?', '!', ';', ':', '(', ')', '[', ']', '{', '}', '"', '\'' };

        private readonly List<DemoCase> _cases = new List<DemoCase>
        {
            new DemoCase
            {
                Name = "English (word boundary)", Lang = Languages.English,
                Head = "The starboard thruster is misfiring again.",
                Candidates = new[] { "Starboard thruster failure detected.", "The port engine is overheating.", "Quest log updated." }
            },
            new DemoCase
            {
                Name = "German (word boundary)", Lang = Languages.German,
                Head = "Das rechte Triebwerk setzt schon wieder aus.",
                Candidates = new[] { "Das rechte Triebwerk fällt wieder aus.", "Das linke Triebwerk überhitzt.", "Questlog aktualisiert." }
            },
            new DemoCase
            {
                Name = "Japanese (per-character)", Lang = Languages.Japanese,
                Head = "右舷のスラスターがまた不調です。",
                Candidates = new[] { "右舷のスラスターが故障しました。", "左舷のエンジンが過熱しています。", "クエストが更新されました。" }
            },
            new DemoCase
            {
                Name = "Korean (per-character)", Lang = Languages.Korean,
                Head = "우현 추진기에서 반복적으로 이상 진동이 감지되고 있습니다.",
                Candidates = new[] { "우현 추진기의 출력이 불안정해지고 있습니다.", "우현 추진기의 출력 저하가 확인되었습니다.", "좌현 엔진의 냉각 계통에 문제가 발생했습니다." }
            },            
            new DemoCase
            {
                Name = "Persian (word boundary, RTL)", Lang = Languages.Persian,
                Head = "موتور سمت راست دوباره خراب شد.",
                Candidates = new[] { "موتور سمت راست خراب است.", "موتور سمت چپ داغ کرده است.", "گزارش ماموریت به روز شد." }
            }
        };

        public ContextView()
        {
            InitializeComponent();

            ThresholdSlider.Value = NIM_Engine.Config.BucketAssignmentSimilarityThreshold_WordBoundary;

            LangBox.ItemsSource = Enum.GetValues(typeof(Languages)).Cast<Languages>().ToList();
            LangBox.SelectedItem = Languages.Auto;

            CaseBox.ItemsSource = _cases;
            CaseBox.SelectedIndex = 0;

            Unloaded += (s, e) => { if (_cts != null) _cts.Cancel(); };
        }

        // ===================== Public API =====================

        /// <summary>Raised on the UI thread when a playback finishes (not when it is stopped).</summary>
        public event EventHandler PlayFinished;

        /// <summary>Similarity threshold (0.05 ~ 0.8).</summary>
        public double Threshold
        {
            get { return ThresholdSlider.Value; }
            set { ThresholdSlider.Value = Math.Max(ThresholdSlider.Minimum, Math.Min(ThresholdSlider.Maximum, value)); }
        }

        /// <summary>Animation speed multiplier (0.25 ~ 6).</summary>
        public double AnimationSpeed
        {
            get { return SpeedSlider.Value; }
            set { SpeedSlider.Value = Math.Max(SpeedSlider.Minimum, Math.Min(SpeedSlider.Maximum, value)); }
        }

        /// <summary>Set the demo texts. Does not start playback.</summary>
        public void SetDemo(string head, IEnumerable<string> candidates, Languages lang = Languages.Auto)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(new Action(() => SetDemo(head, candidates, lang)));
                return;
            }

            CaseBox.SelectedIndex = -1;
            HeadBox.Text = head ?? "";
            CandBox.Text = string.Join(Environment.NewLine, candidates ?? new string[0]);
            LangBox.SelectedItem = lang;
        }

        /// <summary>Start playing with the current texts and settings.</summary>
        public void Play()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(Play));
                return;
            }
            StartPlay();
        }

        /// <summary>Set the texts and start playing.</summary>
        public void Play(string head, IEnumerable<string> candidates, Languages lang = Languages.Auto)
        {
            SetDemo(head, candidates, lang);
            Play();
        }

        /// <summary>Stop the current playback.</summary>
        public void Stop()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(Stop));
                return;
            }
            if (_cts != null) _cts.Cancel();
            StatusText.Text = "Stopped";
        }

        // ===================== UI events =====================

        private void CaseBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var c = CaseBox.SelectedItem as DemoCase;
            if (c == null) return;
            HeadBox.Text = c.Head;
            CandBox.Text = string.Join(Environment.NewLine, c.Candidates);
            LangBox.SelectedItem = c.Lang;
        }

        private void PlayBtn_Click(object sender, RoutedEventArgs e)
        {
            StartPlay();
        }

        private void StopBtn_Click(object sender, RoutedEventArgs e)
        {
            Stop();
        }

        private async void StartPlay()
        {
            if (_cts != null) _cts.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            string head = (HeadBox.Text ?? "").Trim();
            var cands = (CandBox.Text ?? "")
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

            try
            {
                await RunDemo(head, cands, ct);
                if (!ct.IsCancellationRequested)
                {
                    var h = PlayFinished;
                    if (h != null) h(this, EventArgs.Empty);
                }
            }
            catch (OperationCanceledException) { }
        }

        // ===================== Main flow =====================

        private async Task RunDemo(string headText, List<string> cands, CancellationToken ct)
        {
            Stage.Children.Clear();
            if (headText.Length == 0)
            {
                StatusText.Text = "Source text is empty";
                return;
            }

            _boost = 1;
            StatusText.Text = "Step 1: build token signature of the source text";
            var head = await BuildRow("Source (Head)", headText, ct);

            for (int i = 0; i < cands.Count; i++)
            {
                _boost = 2.5;
                StatusText.Text = string.Format("Step 2: build token signature of candidate #{0}", i + 1);
                var row = await BuildRow("Candidate #" + (i + 1) + "  " + cands[i], cands[i], ct);
                await Match(head, row, i + 1, ct);
                await Wait(350, ct);
            }

            StatusText.Text = "Done";
        }

        // Auto -> same detector the engine uses
        private Languages ResolveLang(string text)
        {
            Languages sel = LangBox.SelectedItem is Languages ? (Languages)LangBox.SelectedItem : Languages.Auto;
            if (sel == Languages.Auto)
                sel = P_Language.DetectLanguageByLine(text);
            return sel;
        }

        // ===================== Row building =====================

        private async Task<RowVisual> BuildRow(string label, string text, CancellationToken ct)
        {
            Languages lang = ResolveLang(text);
            var row = new RowVisual();
            row.Sig = TextTokenizer.BuildTokenSignature(lang, text);

            // ---- header (same look as NodeView group header) ----
            var title = new TextBlock
            {
                Text = label,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Br("#FFF2F2F2"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            row.CountText = new TextBlock { Text = "0 tokens" };
            var countPill = MakePill(row.CountText, Br("#FFEAEAEA"));

            row.ResultText = new TextBlock();
            row.ResultPill = MakePill(row.ResultText, Br("#FFEAEAEA"));
            row.ResultPill.Visibility = Visibility.Collapsed;

            var pills = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            pills.Children.Add(MakePill(new TextBlock { Text = lang.ToString() }, Dim));
            pills.Children.Add(countPill);
            pills.Children.Add(row.ResultPill);

            var headerGrid = new Grid { Margin = new Thickness(10, 0, 10, 0) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.Children.Add(title);
            Grid.SetColumn(pills, 1);
            headerGrid.Children.Add(pills);

            var header = new Border
            {
                Height = 38,
                Background = Br("#FF262626"),
                BorderBrush = Br("#FF0C0C0C"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = headerGrid
            };

            row.Body = new StackPanel { Margin = new Thickness(12, 8, 12, 12) };

            var outer = new StackPanel();
            outer.Children.Add(header);
            outer.Children.Add(row.Body);

            var card = new Border
            {
                Background = Br("#FF111111"),
                BorderBrush = Br("#FF1C1C1C"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Margin = new Thickness(0, 0, 0, 8),
                Child = outer
            };
            Stage.Children.Add(card);
            card.BringIntoView();

            row.ChipPanel = new WrapPanel();

            if (lang.IsNoSpaceLanguage())
                await AnimateNGram(text, row, ct);
            else
                await AnimateWords(text, lang, row, ct);

            // Anything in the engine signature that the animation did not show
            foreach (var t in row.Sig)
                AddChip(row, t);

            return row;
        }

        private static Border MakePill(TextBlock tb, Brush fg)
        {
            tb.FontSize = 11;
            tb.Foreground = fg;
            tb.Margin = new Thickness(12, 0, 12, 0);
            tb.HorizontalAlignment = HorizontalAlignment.Center;
            tb.VerticalAlignment = VerticalAlignment.Center;
            return new Border
            {
                Height = 22,
                MinWidth = 70,
                CornerRadius = new CornerRadius(11),
                Background = Br("#FF141414"),
                BorderBrush = Br("#33FFFFFF"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(6, 0, 0, 0),
                Child = tb
            };
        }

        private static TextBlock Caption(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 10,
                Foreground = Dim,
                Margin = new Thickness(0, 10, 0, 4)
            };
        }

        // ---------- No-space languages: per-character recursive N-Gram ----------

        private async Task AnimateNGram(string text, RowVisual row, CancellationToken ct)
        {
            int n = text.Length;
            var canvas = new Canvas
            {
                Width = n * CellW + 20,
                Height = 112,
                Margin = new Thickness(0, 6, 0, 4)
            };
            row.Body.Children.Add(new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = canvas
            });
            row.Body.Children.Add(Caption("TOKEN SIGNATURE"));
            row.Body.Children.Add(row.ChipPanel);

            var cells = new Border[n];
            for (int i = 0; i < n; i++)
            {
                cells[i] = new Border
                {
                    Width = CellW,
                    Height = 32,
                    CornerRadius = new CornerRadius(3),
                    Child = new TextBlock
                    {
                        Text = text[i].ToString(),
                        FontSize = 20,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                Canvas.SetLeft(cells[i], 10 + i * CellW);
                Canvas.SetTop(cells[i], BaseY + 4);
                canvas.Children.Add(cells[i]);
            }

            for (int i = 0; i < n; i++)
            {
                var arcs = new List<Path>();

                for (int len = 1; len <= MaxGram && i + len <= n; len++)
                {
                    string tok = text.Substring(i, len).Replace(" ", "");

                    for (int j = i; j < i + len; j++) cells[j].Background = CellHi;

                    if (tok.Length < 2) continue;

                    double x1 = 10 + i * CellW + CellW / 2.0;
                    double x2 = 10 + (i + len - 1) * CellW + CellW / 2.0;
                    double h = 8 + len * 9;

                    var arc = MakeArc(x1, x2, BaseY, h);
                    canvas.Children.Add(arc.Item1);
                    arcs.Add(arc.Item1);

                    StatusText.Text = string.Format("start i={0}  len={1}  →  \"{2}\"", i, len, tok);

                    arc.Item1.BeginAnimation(Shape.StrokeDashOffsetProperty, new DoubleAnimation
                    {
                        From = arc.Item2,
                        To = 0,
                        Duration = Dur(260),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    });

                    if (row.Sig.Contains(tok)) AddChip(row, tok);
                    await Wait(150, ct);
                }

                await Wait(120, ct);

                foreach (var p in arcs)
                    p.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.2, Dur(300)));
                for (int j = 0; j < n; j++) cells[j].Background = null;
            }

            await Wait(200, ct);
        }

        // Returns (path, dash length in stroke-thickness units)
        private static Tuple<Path, double> MakeArc(double x1, double x2, double y, double h)
        {
            const double Thickness = 2.5;

            var fig = new PathFigure { StartPoint = new Point(x1, y), IsClosed = false };
            fig.Segments.Add(new ArcSegment(new Point(x2, y), new Size((x2 - x1) / 2.0, h), 0,
                false, SweepDirection.Clockwise, true));
            var geo = new PathGeometry();
            geo.Figures.Add(fig);

            double len = 0;
            var flat = geo.GetFlattenedPathGeometry();
            foreach (var f in flat.Figures)
            {
                var prev = f.StartPoint;
                foreach (var seg in f.Segments)
                {
                    var pl = seg as PolyLineSegment;
                    if (pl == null) continue;
                    foreach (var pt in pl.Points)
                    {
                        len += (pt - prev).Length;
                        prev = pt;
                    }
                }
            }

            double unit = len / Thickness + 1;

            var path = new Path
            {
                Data = geo,
                Stroke = Accent,
                StrokeThickness = Thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeDashArray = new DoubleCollection { unit, unit + 20 },
                StrokeDashOffset = unit,
                IsHitTestVisible = false
            };
            return Tuple.Create(path, unit);
        }

        // ---------- Space-delimited languages: word boundary ----------

        private async Task AnimateWords(string text, Languages lang, RowVisual row, CancellationToken ct)
        {
            // Splitting mirrors TextTokenizer.Tokenize (it is private); keep/drop comes from the engine signature
            string s = text.Replace('_', ' ').Replace('-', ' ');
            if (lang.IsSpaceDelimitedLanguage())
                s = Regex.Replace(s, "(?<!^)([A-Z])", " $1");
            var words = s.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);

            var wordPanel = new WrapPanel { Margin = new Thickness(0, 6, 0, 4) };
            if (IsRtl(text)) wordPanel.FlowDirection = FlowDirection.RightToLeft;

            row.Body.Children.Add(wordPanel);
            row.Body.Children.Add(Caption("TOKEN SIGNATURE"));
            row.Body.Children.Add(row.ChipPanel);

            var boxes = new List<Border>();
            var labels = new List<TextBlock>();
            foreach (var w in words)
            {
                var tb = new TextBlock { Text = w, FontSize = 18, Foreground = Br("#FF8A8A8A") };
                var b = new Border
                {
                    Child = tb,
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(3),
                    CornerRadius = new CornerRadius(4),
                    BorderBrush = Brushes.Transparent,
                    BorderThickness = new Thickness(1)
                };
                wordPanel.Children.Add(b);
                boxes.Add(b);
                labels.Add(tb);
            }

            for (int i = 0; i < words.Length; i++)
            {
                string w = words[i];
                bool keep = w.Length > 1 && row.Sig.Contains(w.ToLowerInvariant());

                boxes[i].Background = CellHi;
                labels[i].Foreground = Brushes.White;
                StatusText.Text = string.Format("word \"{0}\"  →  {1}", w,
                    keep ? "keep" : (w.Length <= 1 ? "too short, dropped" : "stop word, dropped"));
                await Wait(320, ct);

                boxes[i].Background = null;
                if (keep)
                {
                    labels[i].Foreground = Br("#FFEAEAEA");
                    boxes[i].BorderBrush = Accent;
                    AddChip(row, w.ToLowerInvariant());
                }
                else
                {
                    labels[i].Foreground = Br("#FF555555");
                    labels[i].TextDecorations = TextDecorations.Strikethrough;
                }
                await Wait(120, ct);
            }

            await Wait(200, ct);
        }

        // ===================== Matching =====================

        private async Task Match(RowVisual head, RowVisual row, int index, CancellationToken ct)
        {
            foreach (var kv in head.Chips) SetChip(kv.Value, false);

            double threshold = ThresholdSlider.Value;
            StatusText.Text = string.Format("Step 3: intersect candidate #{0} with the source", index);

            int inter = 0;
            foreach (var tok in row.Order)
            {
                if (!head.Sig.Contains(tok)) continue;
                inter++;
                SetChip(row.Chips[tok], true);
                SetChip(head.Chips[tok], true);
                await Wait(70, ct);
            }

            // Same formula as P_BucketContainer.BuildBuckets
            int union = head.Sig.Count + row.Sig.Count - inter;
            double sim = union == 0 ? 0 : (double)inter / union;
            bool ok = sim >= threshold;
            Brush color = ok ? Good : Bad;

            // Bar with a threshold marker
            var g = new Grid { Height = 10, Margin = new Thickness(0, 12, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, threshold), GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 1 - threshold), GridUnitType.Star) });

            var track = new Border
            {
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = Br("#FF1B1B1B"),
                BorderBrush = Br("#FF484848"),
                BorderThickness = new Thickness(1)
            };
            Grid.SetColumnSpan(track, 2);

            var scale = new ScaleTransform(0, 1);
            var fill = new Border
            {
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = color,
                RenderTransformOrigin = new Point(0, 0.5),
                RenderTransform = scale
            };
            Grid.SetColumnSpan(fill, 2);

            var mark = new Border
            {
                Width = 2,
                Background = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Grid.SetColumn(mark, 1);

            g.Children.Add(track);
            g.Children.Add(fill);
            g.Children.Add(mark);
            row.Body.Children.Add(g);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, Math.Min(1, sim), Dur(500)));

            row.Body.Children.Add(new TextBlock
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Foreground = color,
                Text = string.Format("Jaccard = {0} / {1} = {2:F2}   {3}   threshold {4:F2}   →   {5}",
                    inter, union, sim, ok ? "≥" : "<", threshold,
                    ok ? "RELATED (same bucket)" : "NOT related (own bucket)")
            });

            row.ResultText.Text = (ok ? "RELATED  " : "NOT RELATED  ") + sim.ToString("F2");
            row.ResultText.Foreground = color;
            row.ResultPill.BorderBrush = color;
            row.ResultPill.Visibility = Visibility.Visible;

            row.Body.BringIntoView();

            StatusText.Text = string.Format("Candidate #{0}: similarity {1:F2}  {2}", index, sim, ok ? "✔ related" : "✘ not related");
            await Wait(500, ct);
        }

        // ===================== Helpers =====================

        private void AddChip(RowVisual row, string token)
        {
            string key = token.ToLowerInvariant();
            if (row.Chips.ContainsKey(key)) return;

            var st = new ScaleTransform(0.3, 0.3);
            var chip = new Border
            {
                Background = ChipBg,
                BorderBrush = ChipBd,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(2),
                Padding = new Thickness(6, 2, 6, 2),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = st,
                Child = new TextBlock { Text = token, FontSize = 12, Foreground = Br("#FFEAEAEA") }
            };
            row.ChipPanel.Children.Add(chip);
            row.Chips[key] = chip;
            row.Order.Add(key);
            row.CountText.Text = row.Order.Count + " tokens";

            var pop = new DoubleAnimation(0.3, 1, Dur(180)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut } };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }

        private static void SetChip(Border chip, bool hit)
        {
            chip.Background = hit ? HitBg : ChipBg;
            chip.BorderBrush = hit ? Accent : ChipBd;
        }

        private Task Wait(int ms, CancellationToken ct)
        {
            double speed = Math.Max(0.1, SpeedSlider.Value * _boost);
            return Task.Delay(Math.Max(1, (int)(ms / speed)), ct);
        }

        private Duration Dur(int ms)
        {
            double speed = Math.Max(0.1, SpeedSlider.Value * _boost);
            return new Duration(TimeSpan.FromMilliseconds(Math.Max(10, ms / speed)));
        }

        // Arabic-script text (Persian, Urdu, Arabic) -> lay out words right-to-left
        private static bool IsRtl(string text)
        {
            foreach (char c in text)
            {
                if ((c >= 0x0590 && c <= 0x08FF) || (c >= 0xFB1D && c <= 0xFDFF) || (c >= 0xFE70 && c <= 0xFEFF))
                    return true;
            }
            return false;
        }

        private static SolidColorBrush Br(string hex)
        {
            var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
            b.Freeze();
            return b;
        }

        private void LangBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string Head = (HeadBox.Text ?? "").Trim();
            if (Head.Length == 0) return;

            Languages Lang = LangBox.SelectedItem is Languages
                ? (Languages)LangBox.SelectedItem
                : Languages.Auto;

            if (Lang == Languages.Auto)
            {
                Lang = P_Language.DetectLanguageByLine(Head);
            }

            ThresholdSlider.Value = Lang.IsSpaceDelimitedLanguage()
                ? NIM_Engine.Config.BucketAssignmentSimilarityThreshold_WordBoundary
                : NIM_Engine.Config.BucketAssignmentSimilarityThreshold_NoSpace;
        }
    }
}