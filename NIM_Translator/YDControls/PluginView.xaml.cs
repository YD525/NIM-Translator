using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace NIM.YDControls
{
    public class PluginItem : INotifyPropertyChanged
    {
        public string FilePath { get; set; }
        public string Name { get; set; }
        public string Author { get; set; }
        public string Description { get; set; }
        public string Version { get; set; }
        public string Hash { get; set; }

        private bool _IsEnabled;
        public bool IsEnabled
        {
            get { return _IsEnabled; }
            set
            {
                if (_IsEnabled == value) return;
                _IsEnabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
            }
        }

        public string VersionText { get { return string.IsNullOrWhiteSpace(Version) ? "v?" : "v" + Version.TrimStart('v', 'V'); } }
        public string AuthorText { get { return string.IsNullOrWhiteSpace(Author) ? "Unknown" : Author; } }
        public string FileName { get { try { return Path.GetFileName(FilePath ?? ""); } catch { return FilePath; } } }
        public string ShortHash
        {
            get
            {
                if (string.IsNullOrEmpty(Hash)) return "-";
                return Hash.Length > 20 ? Hash.Substring(0, 12) + "…" + Hash.Substring(Hash.Length - 8) : Hash;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public partial class PluginView : UserControl
    {
        /// <summary>
        /// Bind to PluginHub.Instance.GetPlugins.
        /// string[0..5] = FilePath, Name, Author, Description, Version, SHA256.
        /// Optional string[6] = "1"/"true" if currently enabled (otherwise treated as disabled).
        /// </summary>
        public Func<List<string[]>> PluginProvider { get; set; }

        /// <summary>Bind to PluginHub.Instance.EnablePlugin.</summary>
        public Func<string, bool> EnableHandler { get; set; }

        /// <summary>Bind to PluginHub.Instance.DisablePlugin.</summary>
        public Func<string, bool> DisableHandler { get; set; }

        private readonly ObservableCollection<PluginItem> _Items = new ObservableCollection<PluginItem>();
        private ICollectionView _View;
        private string _Filter = "all";

        public PluginView()
        {
            InitializeComponent();

            _View = CollectionViewSource.GetDefaultView(_Items);
            _View.Filter = FilterItem;
            CardList.ItemsSource = _View;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_Items.Count == 0) Refresh();
        }

        public void Refresh()
        {
            _Items.Clear();
            if (PluginProvider == null) { UpdateCount(); return; }

            List<string[]> Data;
            try { Data = PluginProvider() ?? new List<string[]>(); }
            catch { Data = new List<string[]>(); }

            foreach (var P in Data.Where(x => x != null && x.Length >= 6))
            {
                bool On = false;
                if (P.Length >= 7 && !string.IsNullOrEmpty(P[6]))
                    On = P[6] == "1" || P[6].Equals("true", StringComparison.OrdinalIgnoreCase);

                _Items.Add(new PluginItem
                {
                    FilePath = P[0],
                    Name = string.IsNullOrWhiteSpace(P[1]) ? Path.GetFileNameWithoutExtension(P[0] ?? "") : P[1],
                    Author = P[2],
                    Description = string.IsNullOrWhiteSpace(P[3]) ? "No description." : P[3],
                    Version = P[4],
                    Hash = P[5],
                    IsEnabled = On
                });
            }

            _View.Refresh();
            UpdateCount();
        }

        private bool FilterItem(object O)
        {
            var I = O as PluginItem;
            if (I == null) return false;

            if (_Filter == "on" && !I.IsEnabled) return false;
            if (_Filter == "off" && I.IsEnabled) return false;

            string Q = SearchBox == null ? "" : SearchBox.Text.Trim();
            if (Q.Length == 0) return true;

            return Contains(I.Name, Q) || Contains(I.Author, Q) || Contains(I.Description, Q)
                || Contains(I.Hash, Q) || Contains(I.FileName, Q);
        }

        private static bool Contains(string S, string Q)
        {
            return !string.IsNullOrEmpty(S) && S.IndexOf(Q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void UpdateCount()
        {
            if (CountText == null) return;
            int Shown = _View.Cast<object>().Count();
            int On = _Items.Count(x => x.IsEnabled);
            CountText.Text = Shown + " / " + _Items.Count + "  ·  " + On + " on";
            NoResult.Visibility = Shown == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ApplyFilter()
        {
            if (_View == null) return;
            _View.Refresh();
            UpdateCount();
        }

        // ===== toolbar =====

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            bool Empty = SearchBox.Text.Length == 0;
            PlaceholderText.Visibility = Empty ? Visibility.Visible : Visibility.Collapsed;
            ClearBtn.Visibility = Empty ? Visibility.Collapsed : Visibility.Visible;
            ApplyFilter();
        }

        private void ClearSearch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            SearchBox.Clear();
            SearchBox.Focus();
        }

        private void Chip_Checked(object sender, RoutedEventArgs e)
        {
            var R = sender as RadioButton;
            _Filter = R == null ? "all" : (R.Tag as string ?? "all");
            ApplyFilter();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            Refresh();
        }

        private void DisableAll_Click(object sender, RoutedEventArgs e)
        {
            SetMany(false);
        }

        private void SetMany(bool On)
        {
            // only touches what is currently visible (respects search / filter)
            foreach (var I in _View.Cast<PluginItem>().ToList())
                Apply(I, On);
            ApplyFilter();
        }

        // ===== card =====

        private void Toggle_Click(object sender, RoutedEventArgs e)
        {
            var T = sender as ToggleButton;
            var I = T == null ? null : T.DataContext as PluginItem;
            if (I == null) return;

            bool Want = T.IsChecked == true;
            if (!Apply(I, Want))
            {
                // SetCurrentValue keeps the OneWay binding alive; a plain assignment would destroy it.
                T.SetCurrentValue(ToggleButton.IsCheckedProperty, I.IsEnabled);
            }

            ApplyFilter();
        }

        private bool Apply(PluginItem I, bool On)
        {
            if (string.IsNullOrEmpty(I.Hash)) return false;

            var Handler = On ? EnableHandler : DisableHandler;
            if (Handler == null) return false;

            bool Ok;
            try { Ok = Handler(I.Hash); }
            catch { Ok = false; }

            if (Ok) I.IsEnabled = On;
            return Ok;
        }

        private void Hash_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var T = sender as FrameworkElement;
            var I = T == null ? null : T.DataContext as PluginItem;
            if (I == null || string.IsNullOrEmpty(I.Hash)) return;
            try { Clipboard.SetText(I.Hash); } catch { }
        }
    }
}