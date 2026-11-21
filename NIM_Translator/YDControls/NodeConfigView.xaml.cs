using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NIMEngine;
using NIMEngine.Platform;
using NIMEngine.Platform.Nodes;

namespace NIM.YDControls
{
    // =====================================================================
    //  Events
    // =====================================================================
    public enum NodeConfigChangeKind
    {
        None,
        ApiKeyAdded,
        ApiKeyRemoved,
        Model,
        Port
    }

    public class NodeConfigEventArgs : EventArgs
    {
        /// <summary>The config that was edited (already updated).</summary>
        public PlatformConfig Config { get; private set; }
        public NodeConfigChangeKind Kind { get; private set; }
        /// <summary>The key / model / port text. For DocumentClicked: the url.</summary>
        public string Value { get; private set; }

        public NodeConfigEventArgs(PlatformConfig config, NodeConfigChangeKind kind, string value)
        {
            Config = config; Kind = kind; Value = value;
        }
    }

    // =====================================================================
    //  Helpers
    // =====================================================================
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(value is bool && (bool)value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(value is bool && (bool)value);
        }
    }

    public class ApiKeyItem
    {
        public string Key { get; private set; }
        public string Masked { get; private set; }

        public ApiKeyItem(string key)
        {
            Key = key ?? "";
            Masked = Mask(Key);
        }

        private static string Mask(string key)
        {
            if (key.Length <= 8) return new string('•', key.Length);
            return key.Substring(0, 4) + "••••••••" + key.Substring(key.Length - 4);
        }
    }

    public abstract class ConfigNotifyBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void Raise([CallerMemberName] string name = null)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }
    }

    // =====================================================================
    //  Card view model (one per PlatformConfig)
    // =====================================================================
    public class NodeConfigItemVM : ConfigNotifyBase
    {
        private static readonly Brush OffBrush = Make(Color.FromRgb(0x3A, 0x3A, 0x3A));
        private static readonly Brush Clear = Make(Colors.Transparent);

        private readonly Action<NodeConfigItemVM, NodeConfigChangeKind, string> _changed;
        private readonly Brush _accent, _glow;
        private bool _expanded;
        private bool _modelsOpen;

        public PlatformConfig Config { get; private set; }
        public ObservableCollection<ApiKeyItem> Keys { get; private set; }
        public string[] Models { get; private set; }
        public string DocumentUrl { get; private set; }
        public bool CanDelete { get; private set; }
        public bool IsCloud { get; private set; }
        public bool IsLocal { get; private set; }
        public bool IsTraditional { get; private set; }

        public NodeConfigItemVM(PlatformConfig cfg, bool expanded, string documentUrl, bool? canDelete,
                                Action<NodeConfigItemVM, NodeConfigChangeKind, string> changed)
        {
            Config = cfg;
            _expanded = expanded;
            _changed = changed;
            DocumentUrl = documentUrl;

            IsCloud = cfg.Type == P_Node.CloudAI;
            IsLocal = cfg.Type == P_Node.LocalAI;
            IsTraditional = cfg.Type == P_Node.Traditional;

            Color accent = AccentOf(cfg.Type);
            _accent = Make(accent);

            var glow = new RadialGradientBrush();
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0xD0, accent.R, accent.G, accent.B), 0.0));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x50, accent.R, accent.G, accent.B), 0.45));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, accent.R, accent.G, accent.B), 1.0));
            glow.Freeze();
            _glow = glow;

            Keys = new ObservableCollection<ApiKeyItem>();
            if (cfg.ApiKeys != null)
            {
                foreach (var k in cfg.ApiKeys) Keys.Add(new ApiKeyItem(k));
            }

            bool isSystem = false;
            if (IsCloud)
            {
                Models = PlatformHub.QueryModels(cfg.Name??"").ToArray();
                //isSystem = node.IsSystemNode;
            }

            CanDelete = canDelete ?? !isSystem;
        }

        // ---- static look helpers ----
        private static Brush Make(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public static Color AccentOf(int type)
        {
            if (type == P_Node.CloudAI) return Color.FromRgb(0x4F, 0xC3, 0xF7);
            if (type == P_Node.LocalAI) return Color.FromRgb(0xFF, 0xE6, 0x00);
            if (type == P_Node.Traditional) return Color.FromRgb(0xE0, 0xE0, 0xE0);
            return Color.FromRgb(0xFF, 0x91, 0x00);
        }

        public static int GroupIndex(int type)
        {
            if (type == P_Node.CloudAI) return 0;
            if (type == P_Node.LocalAI) return 1;
            if (type == P_Node.Traditional) return 2;
            return 3;
        }

        // ---- header ----
        public string Name { get { return Config.Name ?? ""; } }

        public string TypeLabel
        {
            get
            {
                if (IsCloud) return "CLOUD AI";
                if (IsLocal) return "LOCAL AI";
                if (IsTraditional) return "TRADITIONAL";
                return "INTERACTIVE";
            }
        }

        public Brush AccentBrush { get { return _accent; } }
        public Brush LedBrush { get { return Config.Enable ? _accent : OffBrush; } }
        public Brush GlowBrush { get { return Config.Enable ? _glow : Clear; } }

        public void RefreshEnable()
        {
            Raise("LedBrush");
            Raise("GlowBrush");
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
                Raise("HeaderCorner");
                Raise("BarCorner");
            }
        }

        public string ChevronText { get { return _expanded ? "▾" : "▸"; } }
        public Visibility BodyVisibility { get { return _expanded ? Visibility.Visible : Visibility.Collapsed; } }
        public CornerRadius HeaderCorner { get { return _expanded ? new CornerRadius(5, 5, 0, 0) : new CornerRadius(5); } }
        public CornerRadius BarCorner { get { return _expanded ? new CornerRadius(5, 0, 0, 0) : new CornerRadius(5, 0, 0, 5); } }

        public Visibility DocVisibility { get { return string.IsNullOrEmpty(DocumentUrl) ? Visibility.Collapsed : Visibility.Visible; } }
        public Visibility DeleteVisibility { get { return CanDelete ? Visibility.Visible : Visibility.Collapsed; } }

        // ---- sections ----
        public Visibility KeyVisibility { get { return (IsCloud || IsTraditional) ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility ModelVisibility { get { return IsCloud ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility PortVisibility { get { return IsLocal ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility EmptyVisibility { get { return (IsCloud || IsLocal || IsTraditional) ? Visibility.Collapsed : Visibility.Visible; } }
        public Visibility ModelsToggleVisibility
        {
            get { return (Models != null && Models.Length > 0) ? Visibility.Visible : Visibility.Collapsed; }
        }

        public bool IsModelsOpen
        {
            get { return _modelsOpen; }
            set
            {
                if (_modelsOpen == value) return;
                _modelsOpen = value;
                Raise("IsModelsOpen");
            }
        }

        // ---- editable values ----
        public string Model
        {
            get { return Config.Model ?? ""; }
            set
            {
                string t = (value ?? "").Trim();
                if (t.Length == 0 || t == Config.Model) return;

                Config.Model = t;
                Raise("Model");
                Raise("ModelDisplay");
                if (_changed != null) _changed(this, NodeConfigChangeKind.Model, t);
            }
        }

        public string ModelDisplay { get { return string.IsNullOrEmpty(Config.Model) ? "—" : Config.Model; } }

        public string PortText
        {
            get { return Config.LocalPort > 0 ? Config.LocalPort.ToString() : ""; }
            set
            {
                int port;
                if (!int.TryParse(value, out port) || port <= 0 || port > 65535 || port == Config.LocalPort) return;

                Config.LocalPort = port;
                Raise("PortText");
                if (_changed != null) _changed(this, NodeConfigChangeKind.Port, port.ToString());
            }
        }

        // ---- api keys ----
        public bool AddKey(string key)
        {
            key = (key ?? "").Trim();
            if (key.Length == 0) return false;

            if (Config.ApiKeys == null) Config.ApiKeys = new List<string>();
            if (Config.ApiKeys.Contains(key)) return false;

            Config.ApiKeys.Add(key);
            Keys.Add(new ApiKeyItem(key));
            if (_changed != null) _changed(this, NodeConfigChangeKind.ApiKeyAdded, key);
            return true;
        }

        public void RemoveKey(ApiKeyItem item)
        {
            if (item == null) return;

            if (Config.ApiKeys != null) Config.ApiKeys.Remove(item.Key);
            Keys.Remove(item);
            if (_changed != null) _changed(this, NodeConfigChangeKind.ApiKeyRemoved, item.Key);
        }
    }

    // =====================================================================
    //  Control
    // =====================================================================
    public partial class NodeConfigView : UserControl
    {
        /// <summary>
        /// Raised after the user commits an edit (add/remove key, model, port).
        /// e.Config is already updated; persist it here (SaveConfig / ReSetKeyData ...).
        /// </summary>
        public event EventHandler<NodeConfigEventArgs> ConfigChanged;
        /// <summary>User clicked Delete. Nothing is removed by the control; confirm + delete on your side, then call SetConfigs again.</summary>
        public event EventHandler<NodeConfigEventArgs> DeleteRequested;
        /// <summary>User clicked the Document link (e.Value = url).</summary>
        public event EventHandler<NodeConfigEventArgs> DocumentClicked;

        /// <summary>Optional: returns the document url of a node (null/empty = no link). Set before SetConfigs.</summary>
        public Func<PlatformConfig, string> DocumentResolver { get; set; }
        /// <summary>Optional: whether the Delete button is shown. Default: cloud AI nodes hide it when IsSystemNode, others show it.</summary>
        public Func<PlatformConfig, bool> CanDeleteResolver { get; set; }
        /// <summary>Whether cards start expanded the first time they are shown.</summary>
        public bool StartExpanded { get; set; }

        private readonly ObservableCollection<NodeConfigItemVM> _view = new ObservableCollection<NodeConfigItemVM>();
        private readonly Dictionary<string, bool> _expanded = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private List<NodeConfigItemVM> _all = new List<NodeConfigItemVM>();
        private List<PlatformConfig> _source = new List<PlatformConfig>();
        private int _typeFilter = -1;
        private bool _ready;

        public NodeConfigView()
        {
            StartExpanded = true;
            InitializeComponent();
            CardList.ItemsSource = _view;
            _ready = true;
            ApplyFilter();
        }

        // ---- public API ---------------------------------------------------

        /// <summary>Show all node configs. Cards are built from this list; edits are written straight into these objects.</summary>
        public void SetConfigs(IEnumerable<PlatformConfig> configs)
        {
            List<PlatformConfig> ActiveNodes = new List<PlatformConfig>();

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

        /// <summary>Rebuild from the last list (e.g. after AvailableModels were loaded). Collapse states are kept.</summary>
        public void Refresh()
        {
            Rebuild();
        }

        /// <summary>Update header LEDs from cfg.Enable (call after nodes were enabled/disabled elsewhere).</summary>
        public void RefreshState()
        {
            foreach (var vm in _all) vm.RefreshEnable();
        }

        public string SearchText
        {
            get { return SearchBox.Text; }
            set { SearchBox.Text = value ?? ""; }
        }

        public void ExpandAll() { SetAllExpanded(true); }
        public void CollapseAll() { SetAllExpanded(false); }

        /// <summary>Clear filters, expand the node's card and scroll to it. Returns false if not found.</summary>
        public bool FocusNode(string name)
        {
            var vm = _all.FirstOrDefault(v => string.Equals(v.Config.Name, name, StringComparison.OrdinalIgnoreCase));
            if (vm == null) return false;

            SearchBox.Text = "";
            AllChip.IsChecked = true;
            _typeFilter = -1;

            vm.IsExpanded = true;
            _expanded[vm.Name] = true;
            ApplyFilter();

            Dispatcher.BeginInvoke(new Action(() =>
            {
                var container = CardList.ItemContainerGenerator.ContainerFromItem(vm) as FrameworkElement;
                if (container != null) container.BringIntoView();
            }), DispatcherPriority.Loaded);

            return true;
        }

        // ---- build / filter -----------------------------------------------

        private void Rebuild()
        {
            foreach (var vm in _all) _expanded[vm.Name] = vm.IsExpanded;

            _all = _source
                .OrderBy(c => NodeConfigItemVM.GroupIndex(c.Type))
                .Select(c =>
                {
                    bool exp;
                    if (!_expanded.TryGetValue(c.Name ?? "", out exp)) exp = StartExpanded;

                    string doc = DocumentResolver != null ? DocumentResolver(c) : null;
                    bool? canDelete = CanDeleteResolver != null ? (bool?)CanDeleteResolver(c) : null;

                    return new NodeConfigItemVM(c, exp, doc, canDelete, OnItemChanged);
                })
                .ToList();

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (!_ready) return;

            string q = SearchBox.Text.Trim().ToLowerInvariant();

            _view.Clear();
            foreach (var vm in _all)
            {
                if (_typeFilter != -1 && vm.Config.Type != _typeFilter) continue;

                if (q.Length > 0)
                {
                    bool hit = vm.Name.ToLowerInvariant().Contains(q)
                            || (vm.Config.Model ?? "").ToLowerInvariant().Contains(q);
                    if (!hit) continue;
                }

                _view.Add(vm);
            }

            CountText.Text = string.Format("{0} / {1}", _view.Count, _all.Count);
            NoResult.Visibility = (_view.Count == 0 && _all.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetAllExpanded(bool value)
        {
            foreach (var vm in _all)
            {
                vm.IsExpanded = value;
                _expanded[vm.Name] = value;
            }
        }

        private void OnItemChanged(NodeConfigItemVM vm, NodeConfigChangeKind kind, string value)
        {
            Raise(ConfigChanged, vm.Config, kind, value);
        }

        private void Raise(EventHandler<NodeConfigEventArgs> handler, PlatformConfig cfg, NodeConfigChangeKind kind, string value)
        {
            if (handler != null && cfg != null) handler(this, new NodeConfigEventArgs(cfg, kind, value));
        }

        private static NodeConfigItemVM VmOf(object sender)
        {
            var fe = sender as FrameworkElement;
            return fe != null ? fe.DataContext as NodeConfigItemVM : null;
        }

        // ---- toolbar handlers ---------------------------------------------

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            bool empty = SearchBox.Text.Length == 0;
            PlaceholderText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            ClearBtn.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            ApplyFilter();
        }

        private void ClearSearch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            SearchBox.Text = "";
            SearchBox.Focus();
        }

        private void Chip_Checked(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;

            string tag = (sender as FrameworkElement).Tag as string;
            if (tag == "cloud") _typeFilter = P_Node.CloudAI;
            else if (tag == "local") _typeFilter = P_Node.LocalAI;
            else if (tag == "trad") _typeFilter = P_Node.Traditional;
            else if (tag == "human") _typeFilter = P_Node.Interactive;
            else _typeFilter = -1;

            ApplyFilter();
        }

        private void ExpandAll_Click(object sender, RoutedEventArgs e) { SetAllExpanded(true); }
        private void CollapseAll_Click(object sender, RoutedEventArgs e) { SetAllExpanded(false); }

        // ---- card handlers ------------------------------------------------

        private void Header_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var vm = VmOf(sender);
            if (vm == null) return;

            vm.IsExpanded = !vm.IsExpanded;
            _expanded[vm.Name] = vm.IsExpanded;
        }

        private void DocLink_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true; // do not toggle the card
            var vm = VmOf(sender);
            if (vm != null) Raise(DocumentClicked, vm.Config, NodeConfigChangeKind.None, vm.DocumentUrl);
        }

        // -- api keys

        private void AddKeyFrom(NodeConfigItemVM vm, TextBox box)
        {
            if (vm == null || box == null) return;
            if (box.Text.Trim().Length == 0) return;

            if (vm.AddKey(box.Text)) box.Clear();
            else box.SelectAll(); // duplicate
        }

        private void AddKey_Click(object sender, RoutedEventArgs e)
        {
            AddKeyFrom(VmOf(sender), (sender as FrameworkElement).Tag as TextBox);
        }

        private void KeyBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            AddKeyFrom(VmOf(sender), sender as TextBox);
            e.Handled = true;
        }

        private void RemoveKey_Click(object sender, RoutedEventArgs e)
        {
            var vm = VmOf(sender);
            var list = (sender as FrameworkElement).Tag as ListBox;
            if (vm == null || list == null) return;

            vm.RemoveKey(list.SelectedItem as ApiKeyItem);
        }

        // -- model / port (explicit commit: Enter or lost focus)

        private static void Commit(TextBox box)
        {
            var be = box.GetBindingExpression(TextBox.TextProperty);
            if (be == null) return;

            be.UpdateSource();
            be.UpdateTarget(); // normalise / revert invalid input
        }

        private void CommitBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            Commit(sender as TextBox);
            e.Handled = true;
        }

        private void CommitBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            Commit(sender as TextBox);
        }

        private void PortBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = e.Text.Any(c => !char.IsDigit(c));
        }

        private void ModelsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var lb = sender as ListBox;
            if (lb == null) return;

            var vm = lb.DataContext as NodeConfigItemVM;
            string model = lb.SelectedItem as string;
            if (vm == null || model == null) return;

            vm.IsModelsOpen = false;
            vm.Model = model;
            lb.SelectedIndex = -1;
        }

        // -- let the outer list scroll when the inner list hits its end

        private void Inner_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var sv = FindChild<ScrollViewer>(sender as DependencyObject);
            if (sv == null) return;

            bool atTop = sv.VerticalOffset <= 0;
            bool atBottom = sv.VerticalOffset >= sv.ScrollableHeight;

            if ((e.Delta > 0 && atTop) || (e.Delta < 0 && atBottom))
            {
                e.Handled = true;
                MainScroller.ScrollToVerticalOffset(MainScroller.VerticalOffset - e.Delta);
            }
        }

        private static T FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                var hit = child as T;
                if (hit != null) return hit;

                var deep = FindChild<T>(child);
                if (deep != null) return deep;
            }
            return null;
        }

        private void BackendBox_Loaded(object sender, RoutedEventArgs e)
        {
            var cb = (ComboBox)sender;
            var vm = cb.DataContext as NodeConfigItemVM;
            bool show = vm != null && vm.IsLocal
                        && string.Equals(vm.Config.Name, "LocalAI", StringComparison.OrdinalIgnoreCase);
            cb.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BackendBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var cb = (ComboBox)sender;
            var item = cb.SelectedItem as ComboBoxItem;
            if (item == null) return;

            var portBox = FindChild<TextBox>(cb.Parent as DependencyObject);
            if (portBox != null) portBox.Text = item.Tag as string;
        }
    }
}