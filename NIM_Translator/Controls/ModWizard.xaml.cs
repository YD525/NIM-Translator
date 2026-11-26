using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NIM.YDControls
{
    public partial class ModWizard : UserControl
    {
        #region Data

        public class WizardFile : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler PropertyChanged;

            public string FullPath { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public string TypeTag { get; set; } = "";     // ESP / PEX / MCM / plugin tag
            public string ReaderName { get; set; } = "";  // built-in or plugin name
            public bool CanRead { get; set; } = true;

            private bool _IsChecked;
            public bool IsChecked
            {
                get { return _IsChecked; }
                set { _IsChecked = value && CanRead; Raise(nameof(IsChecked)); }
            }

            private bool _IsHit;
            public bool IsHit
            {
                get { return _IsHit; }
                set { _IsHit = value; Raise(nameof(HitVisibility)); }
            }

            public Visibility HitVisibility { get { return IsHit ? Visibility.Visible : Visibility.Collapsed; } }

            private void Raise(string Name)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Name));
            }
        }

        public class WizardEntry : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler PropertyChanged;

            public string Name { get; set; } = "";
            public string FullPath { get; set; } = "";
            public bool IsMod { get; set; }                  // false: plain folder
            public bool CanReadDirectory { get; set; }       // a plugin can take over the whole folder
            public string DirectoryReaderName { get; set; } = "";
            public List<WizardFile> Files { get; set; } = new List<WizardFile>();
            public object Tag { get; set; }                  // your own object, e.g. SkyrimEntry

            internal bool? UseDirectory;                     // remembered UI choice

            private int _HitCount;
            public int HitCount
            {
                get { return _HitCount; }
                set
                {
                    _HitCount = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HitCount)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HitVisibility)));
                }
            }

            public bool IsSelectable { get { return IsMod && (CanReadDirectory || Files.Any(X => X.CanRead)); } }

            public string KindState { get { return !IsMod ? "Folder" : IsSelectable ? "Mod" : "NoReader"; } }
            public Visibility HitVisibility { get { return HitCount > 0 ? Visibility.Visible : Visibility.Collapsed; } }
        }

        #endregion

        /// <summary>
        /// Check the path and recognize mods (see ScanMods / IsMod).
        /// Fill Entries. Return false when the path cannot be read.
        /// </summary>
        private bool OnScan(string TargetPath, List<WizardEntry> Entries, CancellationToken Token)
        {
            return Directory.Exists(TargetPath);
        }

        /// <summary>
        /// Search by mod name or record content (see SearchStr).
        /// Key: an instance taken from Mods. Value: matched WizardFile.FullPath list, empty for name search.
        /// Check Token inside loops so the Stop button works.
        /// </summary>
        private Dictionary<WizardEntry, List<string>> OnSearch(string Keyword, bool SearchRecords, List<WizardEntry> Mods, CancellationToken Token)
        {
            return new Dictionary<WizardEntry, List<string>>();
        }

        /// <summary>
        /// Load the selection. Return true on success.
        /// UseDirectory: Paths[0] is the mod folder, pass it to the plugin directly.
        /// Otherwise Paths are the checked files.
        /// </summary>
        private bool OnLoad(WizardEntry Mod, bool UseDirectory, List<string> Paths)
        {
            return false;
        }


        /// <summary>Raised after OnLoad returned true: mod, use directory, paths.</summary>
        public event Action<WizardEntry, bool, List<string>> Completed;

        /// <summary>Raised when the user presses Cancel.</summary>
        public event Action Cancelled;

        // Localized text and theme brushes both come from XAML resources
        private string L(string Key)
        {
            return TryFindResource("S." + Key) as string ?? Key;
        }

        private Brush ThemeBrush(string Key)
        {
            return (Brush)FindResource(Key + "Brush");
        }

        private int _Step = 1;
        private bool _IsBusy;
        private bool _CanContinue;
        private List<WizardEntry> _Mods = new List<WizardEntry>();
        private WizardEntry _Current;
        private CancellationTokenSource _Cancel;

        public ModWizard()
        {
            InitializeComponent();
            ShowStep(1);
        }

        public string ModPath
        {
            get { return PathBox.Text.Trim().Trim('"'); }
            set { PathBox.Text = value ?? ""; }
        }

        /// <summary>Back to step 1 with all results cleared.</summary>
        public void Reset()
        {
            if (_IsBusy) return;

            PathBox.Text = "";
            InvalidateScan();
            ShowStep(1);
        }

        #region Navigation

        private void ShowStep(int Step)
        {
            _Step = Step;

            Page1.Visibility = Step == 1 ? Visibility.Visible : Visibility.Collapsed;
            Page2.Visibility = Step == 2 ? Visibility.Visible : Visibility.Collapsed;
            Page3.Visibility = Step == 3 ? Visibility.Visible : Visibility.Collapsed;

            HeaderedContentControl[] Items = { Step1Item, Step2Item, Step3Item };

            for (int i = 0; i < Items.Length; i++)
            {
                Items[i].Tag = i + 1 == Step ? "Active" : i + 1 < Step ? "Done" : "Pending";
            }

            UpdateFooter();
        }

        private void UpdateFooter()
        {
            BackButton.Visibility = _Step > 1 ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = "";

            if (_Step == 1)
            {
                NextButton.Content = L("Detect");
                NextButton.IsEnabled = ModPath.Length > 0;
            }
            else if (_Step == 2)
            {
                NextButton.Content = L("Next");
                NextButton.IsEnabled = _CanContinue;
            }
            else
            {
                NextButton.Content = L("Load");

                int Count = GetSelectedPaths().Count;
                NextButton.IsEnabled = Count > 0;

                if (_Current == null) StatusText.Text = L("SelectMod");
                else if (Count == 0) StatusText.Text = L("CheckFiles");
                else if (IsDirectoryMode()) StatusText.Text = L("FolderWillBeRead");
                else StatusText.Text = string.Format(L("FilesSelected"), Count);
            }
        }

        private async void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_IsBusy) return;

            if (_Step == 1)
            {
                await RunScan(ModPath);
            }
            else if (_Step == 2)
            {
                EnterSelect();
            }
            else
            {
                await RunLoad();
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (_Step > 1) ShowStep(_Step - 1);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Cancelled?.Invoke();
        }

        #endregion

        #region Step 1

        private void PathBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (NextButton == null) return;

            InvalidateScan();
            UpdateFooter();
        }

        private async void PathBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || _IsBusy || ModPath.Length == 0) return;

            e.Handled = true;
            await RunScan(ModPath);
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            using (var Dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                if (Directory.Exists(ModPath)) Dialog.SelectedPath = ModPath;

                if (Dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    ModPath = Dialog.SelectedPath;
                }
            }
        }

        private void Page1_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Page1_Drop(object sender, DragEventArgs e)
        {
            string[] Items = e.Data.GetData(DataFormats.FileDrop) as string[];

            if (Items != null && Items.Length > 0)
            {
                ModPath = Items[0];
            }

            e.Handled = true;
        }

        #endregion

        #region Step 2

        private void InvalidateScan()
        {
            _CanContinue = false;
            _Current = null;
            _Mods = new List<WizardEntry>();

            ScanList.ItemsSource = null;
            ModList.ItemsSource = null;
            SearchBox.Text = "";
            SearchStatus.Text = "";
            ClearSearchButton.Visibility = Visibility.Collapsed;
            ShowMod(null);
        }

        private async void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            WizardEntry Entry = (sender as FrameworkElement)?.Tag as WizardEntry;
            if (Entry == null || _IsBusy) return;

            ModPath = Entry.FullPath;
            await RunScan(Entry.FullPath);
        }

        private async Task RunScan(string TargetPath)
        {
            InvalidateScan();
            ShowStep(2);

            CurrentPathText.Text = TargetPath;
            SetCheck(ReadLed, ReadText, ThemeBrush("Idle"), L("CheckingPath"));
            SetCheck(ModLed, ModText, ThemeBrush("Idle"), L("WaitingDetection"));

            List<WizardEntry> Entries = new List<WizardEntry>();
            bool IsReadable = false;
            bool Stopped = false;
            string Error = null;

            using (CancellationTokenSource Source = BeginBusy(L("DetectingMods"), true))
            {
                try
                {
                    CancellationToken Token = Source.Token;
                    IsReadable = await Task.Run(() => OnScan(TargetPath, Entries, Token), Token);
                    Stopped = Token.IsCancellationRequested;
                }
                catch (OperationCanceledException) { Stopped = true; }
                catch (Exception Ex) { Error = Ex.Message; }
                finally { EndBusy(); }
            }

            if (Stopped)
            {
                SetCheck(ReadLed, ReadText, ThemeBrush("Idle"), L("DetectionStopped"));
            }
            else if (!IsReadable)
            {
                SetCheck(ReadLed, ReadText, ThemeBrush("Failed"), L("CannotRead") + (Error != null ? ": " + Error : ""));
            }
            else
            {
                _Mods = Entries.Where(X => X.IsMod).ToList();
                ScanList.ItemsSource = Entries;

                int Usable = _Mods.Count(X => X.IsSelectable);
                _CanContinue = Usable > 0;

                SetCheck(ReadLed, ReadText, ThemeBrush("Success"), L("PathReadable"));

                if (Usable > 0)
                {
                    SetCheck(ModLed, ModText, ThemeBrush("Success"), string.Format(L("ModsFound"), _Mods.Count, Usable));
                }
                else
                {
                    SetCheck(ModLed, ModText, ThemeBrush("Failed"),
                        L(_Mods.Count > 0 ? "NoAvailableReader" : "NoModRecognized"));
                }
            }

            UpdateFooter();
        }

        private void SetCheck(System.Windows.Shapes.Ellipse Led, TextBlock Label, Brush Fill, string Message)
        {
            Led.Fill = Fill;
            Label.Text = Message;
        }

        #endregion

        #region Step 3 - search

        private void EnterSelect()
        {
            if (ModList.ItemsSource == null)
            {
                ModList.ItemsSource = _Mods;
            }

            ShowStep(3);

            if (ModList.SelectedItem == null)
            {
                ModList.SelectedItem = ModList.Items.Cast<WizardEntry>().FirstOrDefault(X => X.IsSelectable);
            }
        }

        private async void Search_Click(object sender, RoutedEventArgs e)
        {
            await RunSearch();
        }

        private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            e.Handled = true;
            await RunSearch();
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = "";
            ClearSearch();
        }

        private void ClearSearch()
        {
            ClearHits();

            SearchStatus.Text = "";
            ClearSearchButton.Visibility = Visibility.Collapsed;
            ShowModList(_Mods);
        }

        private void ClearHits()
        {
            foreach (WizardEntry Entry in _Mods)
            {
                Entry.HitCount = 0;

                foreach (WizardFile Item in Entry.Files)
                {
                    Item.IsHit = false;
                }
            }

            CheckHitsButton.IsEnabled = false;
        }

        private async Task RunSearch()
        {
            if (_IsBusy) return;

            if (string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                ClearSearch();
                return;
            }

            bool SearchRecords = SearchByRecord.IsChecked == true;
            // Record text may start or end with spaces, only mod names are trimmed
            string Keyword = SearchRecords ? SearchBox.Text : SearchBox.Text.Trim();
            List<WizardEntry> Mods = _Mods.ToList();

            Dictionary<WizardEntry, List<string>> Result = null;
            string Error = null;
            bool Stopped = false;

            using (CancellationTokenSource Source = BeginBusy(L("Searching"), true))
            {
                try
                {
                    CancellationToken Token = Source.Token;
                    Result = await Task.Run(() => OnSearch(Keyword, SearchRecords, Mods, Token), Token);
                    Stopped = Token.IsCancellationRequested;
                }
                catch (OperationCanceledException) { Stopped = true; }
                catch (Exception Ex) { Error = Ex.Message; }
                finally { EndBusy(); }
            }

            if (Stopped)
            {
                SearchStatus.Text = L("SearchStopped");
                return;
            }

            if (Result == null)
            {
                SearchStatus.Text = L("SearchFailed") + (Error != null ? ": " + Error : "");
                return;
            }

            ClearHits();

            List<WizardEntry> Matched = _Mods.Where(X => Result.ContainsKey(X)).ToList();

            foreach (WizardEntry Entry in Matched)
            {
                HashSet<string> HitPaths = new HashSet<string>(
                    Result[Entry] ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

                foreach (WizardFile Item in Entry.Files)
                {
                    Item.IsHit = HitPaths.Contains(Item.FullPath);
                }

                Entry.HitCount = HitPaths.Count;
            }

            SearchStatus.Text = Matched.Count > 0 ? string.Format(L("ModsMatched"), Matched.Count) : L("NoMatches");
            ClearSearchButton.Visibility = Visibility.Visible;
            ShowModList(Matched);
        }

        private void ShowModList(List<WizardEntry> Items)
        {
            WizardEntry Previous = _Current;

            ModList.ItemsSource = null;
            ModList.ItemsSource = Items;

            WizardEntry Next = Previous != null && Items.Contains(Previous)
                ? Previous
                : Items.FirstOrDefault(X => X.IsSelectable);

            ModList.SelectedItem = Next;
            ShowMod(Next);

            if (Next != null) ModList.ScrollIntoView(Next);

            UpdateFooter();
        }

        #endregion

        #region Step 3 - selection

        private void ModList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ShowMod(ModList.SelectedItem as WizardEntry);

            if (_Step == 3) UpdateFooter();
        }

        private void ShowMod(WizardEntry Entry)
        {
            _Current = Entry;

            FileList.ItemsSource = null;

            if (Entry == null)
            {
                ModTitle.Text = L("NoModSelected");
                ModeHint.Text = "";
                ModeDirectory.IsEnabled = false;
                ModeFiles.IsEnabled = false;
                ModeDirectory.IsChecked = false;
                ModeFiles.IsChecked = false;
                FileList.IsEnabled = false;
                FileTools.IsEnabled = false;
                return;
            }

            ModTitle.Text = Entry.Name;
            FileList.ItemsSource = Entry.Files;

            bool HasFiles = Entry.Files.Any(X => X.CanRead);

            ModeDirectory.IsEnabled = Entry.CanReadDirectory;
            ModeFiles.IsEnabled = HasFiles;

            // Folder mode takes priority when a plugin supports it; otherwise the user picks files
            if (Entry.UseDirectory == null && Entry.IsSelectable)
            {
                Entry.UseDirectory = Entry.CanReadDirectory;

                // A single readable file needs no manual check
                List<WizardFile> Readable = Entry.Files.Where(X => X.CanRead).ToList();
                if (Readable.Count == 1) Readable[0].IsChecked = true;
            }

            ModeDirectory.IsChecked = Entry.UseDirectory == true;
            ModeFiles.IsChecked = Entry.UseDirectory == false;

            ApplyModeState();
        }

        private void ApplyModeState()
        {
            if (_Current == null) return;

            bool IsDirectory = IsDirectoryMode();

            FileList.IsEnabled = !IsDirectory;
            FileList.Opacity = IsDirectory ? 0.45 : 1;
            FileTools.IsEnabled = !IsDirectory;
            CheckHitsButton.IsEnabled = _Current.Files.Any(X => X.IsHit && X.CanRead);

            if (!_Current.IsSelectable) ModeHint.Text = L("NoReaderForMod");
            else if (IsDirectory) ModeHint.Text = L("PassToPlugin") + " " + _Current.DirectoryReaderName;
            else if (_Current.CanReadDirectory) ModeHint.Text = L("OnlyChecked");
            else ModeHint.Text = L("FolderNotSupported");
        }

        private bool IsDirectoryMode()
        {
            return _Current?.UseDirectory == true;
        }

        private void ReadMode_Click(object sender, RoutedEventArgs e)
        {
            if (_Current == null) return;

            _Current.UseDirectory = ModeDirectory.IsChecked == true;

            ApplyModeState();
            UpdateFooter();
        }

        private void FileCheck_Click(object sender, RoutedEventArgs e)
        {
            UpdateFooter();
        }

        private void SetChecks(Func<WizardFile, bool> Selector)
        {
            if (_Current == null) return;

            foreach (WizardFile Item in _Current.Files)
            {
                Item.IsChecked = Selector(Item);
            }

            UpdateFooter();
        }

        private void CheckAll_Click(object sender, RoutedEventArgs e)
        {
            SetChecks(X => true);
        }

        private void CheckNone_Click(object sender, RoutedEventArgs e)
        {
            SetChecks(X => false);
        }

        private void CheckHits_Click(object sender, RoutedEventArgs e)
        {
            SetChecks(X => X.IsHit);
        }

        private List<string> GetSelectedPaths()
        {
            if (_Current == null || !_Current.IsSelectable) return new List<string>();

            if (IsDirectoryMode())
            {
                return new List<string> { _Current.FullPath };
            }

            return _Current.Files.Where(X => X.IsChecked && X.CanRead).Select(X => X.FullPath).ToList();
        }

        #endregion

        #region Load

        private async Task RunLoad()
        {
            List<string> Paths = GetSelectedPaths();
            if (Paths.Count == 0) return;

            WizardEntry Mod = _Current;
            bool UseDirectory = IsDirectoryMode();
            bool Success = false;
            string Error = null;

            using (BeginBusy(L("Loading"), false))
            {
                try
                {
                    Success = await Task.Run(() => OnLoad(Mod, UseDirectory, Paths));
                }
                catch (Exception Ex) { Error = Ex.Message; }
                finally { EndBusy(); }
            }

            if (Success)
            {
                Completed?.Invoke(Mod, UseDirectory, Paths);
            }
            else
            {
                StatusText.Text = L("LoadFailed") + (Error != null ? ": " + Error : "");
            }
        }

        #endregion

        #region Busy

        private CancellationTokenSource BeginBusy(string Message, bool CanStop)
        {
            _IsBusy = true;
            _Cancel = new CancellationTokenSource();

            BusyText.Text = Message;
            BusyCancelButton.Visibility = CanStop ? Visibility.Visible : Visibility.Collapsed;
            BusyCancelButton.IsEnabled = true;
            BusyBar.IsIndeterminate = true;
            BusyLayer.Visibility = Visibility.Visible;
            MainBody.IsEnabled = false;

            return _Cancel;
        }

        private void EndBusy()
        {
            _IsBusy = false;
            _Cancel = null;

            BusyBar.IsIndeterminate = false;
            BusyLayer.Visibility = Visibility.Collapsed;
            MainBody.IsEnabled = true;
        }

        private void BusyCancel_Click(object sender, RoutedEventArgs e)
        {
            BusyCancelButton.IsEnabled = false;
            _Cancel?.Cancel();
        }

        #endregion
    }
}