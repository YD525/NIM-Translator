using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NIMEngine;
using NIMEngine.Platform;
using NIMEngine.Platform.Nodes;

namespace NIM.YDControls
{
    // =====================================================================
    //  Events
    // =====================================================================
    public class NodeEventArgs : EventArgs
    {
        /// <summary>Node config.</summary>
        public PlatformConfig Config { get; private set; }
        /// <summary>Group type (P_Node.xxx).</summary>
        public int Type { get; private set; }
        /// <summary>New enable state (only meaningful for NodeEnableChanged).</summary>
        public bool Enable { get; private set; }

        public NodeEventArgs(PlatformConfig config, int type, bool enable)
        {
            Config = config; Type = type; Enable = enable;
        }
    }

    // =====================================================================
    //  View models
    // =====================================================================
    public abstract class NotifyBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void Raise([CallerMemberName] string name = null)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }
    }

    public class NodeItemVM : NotifyBase
    {
        private static readonly Brush OffLight = Make(Color.FromRgb(0x3A, 0x3A, 0x3A));
        private static readonly Brush NameOn = Make(Color.FromRgb(0xF0, 0xF0, 0xF0));
        private static readonly Brush NameOff = Make(Color.FromRgb(0x70, 0x70, 0x70));
        private static readonly Brush BarOff = Make(Color.FromRgb(0x3A, 0x3A, 0x3A));
        private static readonly Brush Clear = Make(Colors.Transparent);

        private readonly Brush _lit, _dim, _glow, _row;
        private readonly Action<NodeItemVM> _onEnableChanged;
        private bool _isLit;
        private int _token;

        public PlatformConfig Config { get; private set; }
        public GroupVM Group { get; private set; }

        public NodeItemVM(PlatformConfig cfg, GroupVM group, Color accent, Action<NodeItemVM> onEnableChanged)
        {
            Config = cfg;
            Group = group;
            _onEnableChanged = onEnableChanged;

            _lit = Make(accent);
            _dim = Make(Color.FromArgb(0x70, accent.R, accent.G, accent.B));

            var glow = new RadialGradientBrush();
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0xD0, accent.R, accent.G, accent.B), 0.0));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x50, accent.R, accent.G, accent.B), 0.45));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, accent.R, accent.G, accent.B), 1.0));
            glow.Freeze();
            _glow = glow;

            var row = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            row.GradientStops.Add(new GradientStop(Color.FromArgb(0x55, accent.R, accent.G, accent.B), 0.0));
            row.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, accent.R, accent.G, accent.B), 0.75));
            row.Freeze();
            _row = row;
        }

        private static Brush Make(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        // ---- data ----
        public string Name { get { return Config.Name; } }

        public string SubText
        {
            get { return string.IsNullOrEmpty(Config.Model) ? "" : Config.Model; }
        }
        public Visibility SubVisibility
        {
            get { return string.IsNullOrEmpty(Config.Model) ? Visibility.Collapsed : Visibility.Visible; }
        }

        public bool Enable
        {
            get { return Config.Enable; }
            set
            {
                if (Config.Enable == value) return;
                Config.Enable = value;
                if (!value) { _token++; _isLit = false; }
                RaiseAll();
                if (_onEnableChanged != null) _onEnableChanged(this);
            }
        }

        // ---- look ----
        public Brush LightBrush { get { return _isLit ? _lit : (Config.Enable ? _dim : OffLight); } }
        public Brush GlowBrush { get { return _isLit ? _glow : Clear; } }
        public Brush RowBrush { get { return _isLit ? _row : Clear; } }
        public Brush BarBrush { get { return Config.Enable ? _lit : BarOff; } }
        public Brush NameBrush { get { return Config.Enable ? NameOn : NameOff; } }

        private void RaiseAll()
        {
            Raise("Enable");
            Raise("LightBrush");
            Raise("GlowBrush");
            Raise("RowBrush");
            Raise("BarBrush");
            Raise("NameBrush");
        }

        private void RaiseLight()
        {
            Raise("LightBrush");
            Raise("GlowBrush");
            Raise("RowBrush");
        }

        /// <summary>Must be called on the UI thread. Re-triggering extends the lit time.</summary>
        public async void Blink(int ms)
        {
            if (!Config.Enable) return;

            int token = ++_token;
            _isLit = true;
            RaiseLight();

            await Task.Delay(ms);

            if (token == _token)
            {
                _isLit = false;
                RaiseLight();
            }
        }
    }

    public class GroupVM : NotifyBase
    {
        private bool _expanded = true;

        public int Type { get; private set; }
        public string Title { get; private set; }
        public Brush AccentBrush { get; private set; }
        public ObservableCollection<NodeItemVM> Nodes { get; private set; }

        public GroupVM(string title, int type, Color accent)
        {
            Title = title;
            Type = type;
            var b = new SolidColorBrush(accent);
            b.Freeze();
            AccentBrush = b;
            Nodes = new ObservableCollection<NodeItemVM>();
        }

        public bool IsExpanded
        {
            get { return _expanded; }
            set
            {
                if (_expanded == value) return;
                _expanded = value;
                Raise("IsExpanded");
                Raise("ChevronText");
                Raise("BodyVisibility");
            }
        }

        public string ChevronText { get { return _expanded ? "▾" : "▸"; } }
        public Visibility BodyVisibility { get { return _expanded ? Visibility.Visible : Visibility.Collapsed; } }

        public string CountText
        {
            get { return string.Format("{0} / {1} Enabled", Nodes.Count(n => n.Config.Enable), Nodes.Count); }
        }

        public void UpdateCount() { Raise("CountText"); }
    }

    // =====================================================================
    //  Control
    // =====================================================================
    public partial class NodeView : UserControl
    {
        /// <summary>Raised after the user flips a node's switch. Config.Enable is already updated; persist it here.</summary>
        public event EventHandler<NodeEventArgs> NodeEnableChanged;
        /// <summary>Raised when the user clicks a node row (not the switch).</summary>
        public event EventHandler<NodeEventArgs> NodeClicked;

        private struct GroupDef
        {
            public string Title; public int Type; public Color Accent;
        }

        private readonly ObservableCollection<GroupVM> _groups = new ObservableCollection<GroupVM>();
        private readonly Dictionary<string, NodeItemVM> _map = new Dictionary<string, NodeItemVM>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, bool> _expanded = new Dictionary<int, bool>();
        private List<PlatformConfig> _source = new List<PlatformConfig>();

        public NodeView()
        {
            InitializeComponent();
            GroupList.ItemsSource = _groups;
        }

        private static GroupDef[] Defs()
        {
            return new[]
            {
                new GroupDef { Title = "Cloud AI Nodes",    Type = P_Node.CloudAI,      Accent = Color.FromRgb(0x4F, 0xC3, 0xF7) },
                new GroupDef { Title = "Local AI Nodes",    Type = P_Node.LocalAI,      Accent = Color.FromRgb(0xFF, 0xE6, 0x00) },
                new GroupDef { Title = "Traditional Nodes", Type = P_Node.Traditional,  Accent = Color.FromRgb(0xE0, 0xE0, 0xE0) },
                new GroupDef { Title = "Interactive Nodes", Type = P_Node.Interactive,  Accent = Color.FromRgb(0xFF, 0x91, 0x00) },
            };
        }

        // ---- public API ---------------------------------------------------

        /// <summary>Show a platform list. Rebuilds the whole view (collapse states are kept).</summary>
        public void SetNodes(IEnumerable<PlatformConfig> configs)
        {
            List<PlatformConfig> ActiveNodes = new  List<PlatformConfig>();

            foreach (var GetNode in configs)
            {
                if (PlatformHub.IsNodeActive(GetNode.Name))
                {
                    ActiveNodes.Add(GetNode);
                }
            }

            _source = ActiveNodes;
            Rebuild();
        }

        /// <summary>Rebuild from the last list passed to SetNodes.</summary>
        public void Refresh()
        {
            Rebuild();
        }

        /// <summary>
        /// Light up a node's LED. Thread-safe. Returns false if the node does not exist.
        /// Disabled nodes do not light up.
        /// </summary>
        public bool BlinkNode(string name, int ms = 800)
        {
            NodeItemVM vm;
            if (name == null || !_map.TryGetValue(name, out vm)) return false;

            if (Dispatcher.CheckAccess()) vm.Blink(ms);
            else Dispatcher.BeginInvoke(new Action(() => vm.Blink(ms)));
            return true;
        }

        public void ScrollToTop() { Scroller.ScrollToTop(); }

        // ---- build --------------------------------------------------------

        private void Rebuild()
        {
            _groups.Clear();
            _map.Clear();

            foreach (var def in Defs())
            {
                var group = new GroupVM(def.Title, def.Type, def.Accent);

                bool exp;
                if (_expanded.TryGetValue(def.Type, out exp)) group.IsExpanded = exp;

                foreach (var cfg in _source.Where(c => c.Type == def.Type))
                {
                    var vm = new NodeItemVM(cfg, group, def.Accent, OnItemEnableChanged);
                    group.Nodes.Add(vm);
                    if (!string.IsNullOrEmpty(cfg.Name)) _map[cfg.Name] = vm;
                }

                _groups.Add(group);
            }
        }

        private void OnItemEnableChanged(NodeItemVM vm)
        {
            vm.Group.UpdateCount();
            var h = NodeEnableChanged;
            if (h != null) h(this, new NodeEventArgs(vm.Config, vm.Group.Type, vm.Config.Enable));
        }

        // ---- UI handlers --------------------------------------------------

        private void Row_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var vm = (sender as FrameworkElement).DataContext as NodeItemVM;
            if (vm == null) return;
            var h = NodeClicked;
            if (h != null) h(this, new NodeEventArgs(vm.Config, vm.Group.Type, vm.Config.Enable));
        }

        private void Group_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var g = (sender as FrameworkElement).DataContext as GroupVM;
            if (g == null) return;
            g.IsExpanded = !g.IsExpanded;
            _expanded[g.Type] = g.IsExpanded;
        }
    }
}