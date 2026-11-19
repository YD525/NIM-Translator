using System.Windows.Controls;
using System.Windows;
using NIM.UIManage;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using NIM.UIManagement;
using System.Threading;
using System.Collections.Generic;
using System;
using System.Linq;
using ICSharpCode.AvalonEdit;
using NIMEngine.Translate;
using NIMEngine.Additional;
using NIMEngine.Common;
using NIM.SkyrimManagement;

public class FakeGrid
{
    public double Height { get; set; } = 0;
    public string Type { get; set; } = "";
    public string Key { get; set; } = "";
    public string Source { get; set; } = "";

    public string RealSource { get; set; } = "";
    public string Translated { get; set; } = "";

    public double Score { get; set; } = 0;

    public FakeGrid(double Height, string Type, string Key, string Source, string Translated, double Score)
    {
        this.Height = Height;
        this.Type = Type;
        this.Key = Key;
        this.Source = Source;
        this.Translated = Translated;
        this.Score = Score;
    }

    public string GetRealSource()
    {
        if (this.RealSource.Length == 0)
        {
            return this.Source;
        }

        return this.RealSource;
    }
    public string GetSource()
    {
        return this.Source;
    }

    public void SyncUI(YDListView ListViewHandle)
    {
        for (int i = 0; i < ListViewHandle.VisibleRows.Count; i++)
        {
            bool CanExit = false;

            if (RowStyleWin.GetKey(ListViewHandle.VisibleRows[i].View).Equals(this.Key))
            {
                ListViewHandle.Parent.Dispatcher.Invoke(new Action(() =>
                {
                    RowStyleWin.SetOriginal(ListViewHandle.VisibleRows[i].View, this.Source);
                    RowStyleWin.SetTranslated(ListViewHandle.VisibleRows[i].View, this.Translated);

                    CanExit = true;
                }));
            }

            if (CanExit)
            {
                break;
            }
        }
    }

    public void SyncData(ModFile Mod, ref bool IsCloud)
    {
        bool FromDictionary = false;

        var FindDictionary = Mod.OriginalDictionaryReader.CheckDictionary(this.Key);

        if (FindDictionary != null)
        {
            if (!string.IsNullOrEmpty(FindDictionary.OriginalText))
            {
                if (this.Source != FindDictionary.OriginalText)
                {
                    this.RealSource = this.Source;
                    this.Source = FindDictionary.OriginalText;
                }

                FromDictionary = true;
            }
        }

        if (!FromDictionary)
        {
            if (!string.IsNullOrEmpty(this.RealSource))
            {
                if (this.Source != this.RealSource)
                {
                    this.Source = this.RealSource;
                }
            }
        }

        IsCloud = false;
        var QueryResult = Mod.P_Translator.QueryTransData(this.Key, this.Type, this.Source, false);

        if (QueryResult != null)
        {
            this.Translated = QueryResult.TransText;
            IsCloud = QueryResult.FromCloud;
        }
    }

    public void SetFontColor(YDListView ListViewHandle, int R, int G, int B)
    {
        for (int i = 0; i < ListViewHandle.VisibleRows.Count; i++)
        {
            if (RowStyleWin.GetKey(ListViewHandle.VisibleRows[i].View).Equals(this.Key))
            {
                RowStyleWin.SetColor(ListViewHandle.VisibleRows[i].View, R, G, B);
                break;
            }
        }
    }
}

public class VisibleGrid
{
    public FakeGrid Data;
    public Grid View;

    public VisibleGrid(FakeGrid Data, Grid View)
    {
        this.Data = Data;
        this.View = View;
    }
}
public class YDListView
{
    public Grid Parent = null;
    private ScrollViewer Scroll = null;
    public Canvas MainCanvas;

    public Thread UpdateTrd = null;

    public List<FakeGrid> RealLines = new List<FakeGrid>();

    // NOTE: This list is never mutated in place anymore, it is always replaced
    // by a fresh list, so other threads enumerating the old one stay safe.
    public List<VisibleGrid> VisibleRows = new List<VisibleGrid>();

    public int Rows { get { return GetRows(); } }
    public int BufferRows = 3;
    public bool CanSet = true;

    public int SelectLineID = 0;
    public Border LastSelectBorder = null;
    public ColumnDefinition LastSetColorCol = null;

    public bool IsSearchBox = false;

    public Thread SelectLineThread = null;
    private CancellationTokenSource CancelSelectLineThread = null;
    private CancellationToken? CancelToken = null;

    // Row top positions: Tops[i] = sum of heights of rows [0, i), Tops[Count] = total height.
    private double[] Tops = new double[1];
    private int TopsCount = 0;
    private volatile bool Disposed = false;

    public delegate void LineSelected(string Key);
    public LineSelected LineSelectedEvent = null;
    public void SetSelectLine(Grid MainGrid, bool UPDate)
    {
        if (LastSelectBorder != null)
        {
            LastSelectBorder.BorderBrush = new SolidColorBrush((Color)Application.Current.Resources["LineANormal"]);
        }
        if (LastSetColorCol != null)
        {
            LastSetColorCol.Width = new GridLength(0, GridUnitType.Pixel);
        }

        SelectLineID = P_Convert.ObjToInt(MainGrid.Tag);

        Border MainBorder = (Border)MainGrid.Children[0];

        Grid GetChildGrid = (Grid)MainBorder.Child;

        Grid GetTranslatedGrid = (Grid)GetChildGrid.Children[2];
        TextEditor GetTranslated = (TextEditor)(((Border)GetTranslatedGrid.Children[0]).Child);

        GetTranslatedGrid.ColumnDefinitions[1].Width = new GridLength(15, GridUnitType.Pixel);

        if (UPDate)
        {
            GetTranslated.Focus();
            string GetKey = RowStyleWin.GetKey(MainGrid);

            LineSelectedEvent?.Invoke(GetKey);

            //if (DeFine.GlobalLocalSetting.ShowCode && DeFine.WorkingWin.CurrentTransType == 3)
            //{
            //    string AutoValue = KeyToFakeGrid(GetKey).RealSource;

            //    if (DeFine.WorkingWin.PexLinks.ContainsKey(GetKey))
            //    {
            //        if (AutoValue == "")
            //        {
            //            AutoValue = RowStyleWin.GetOriginal(MainGrid);
            //        }
            //    }

            //    RowStyleWin.SelectLineFromIDE(DeFine.WorkingWin.PexLinks[GetKey], AutoValue);
            //}
        }

        MainBorder.BorderBrush = new SolidColorBrush((Color)Application.Current.Resources["LineASelected"]);
        LastSelectBorder = MainBorder;
        LastSetColorCol = GetTranslatedGrid.ColumnDefinitions[1];
    }

    private bool IsGridInViewport(Grid Grid)
    {
        double GridTop = Canvas.GetTop(Grid);
        double GridBottom = GridTop + Grid.ActualHeight;

        double ViewTop = Scroll.VerticalOffset;
        double ViewBottom = ViewTop + Scroll.ViewportHeight;

        return GridTop >= ViewTop && GridBottom <= ViewBottom;
    }

    // ---------------------------------------------------------------
    //  Position helpers
    // ---------------------------------------------------------------

    private void RebuildTops()
    {
        int Count = RealLines.Count;

        if (Tops.Length < Count + 1)
        {
            Tops = new double[Math.Max(Count + 1, Tops.Length * 2)];
        }

        double Acc = 0;
        for (int I = 0; I < Count; I++)
        {
            Tops[I] = Acc;
            Acc += RealLines[I].Height;
        }
        Tops[Count] = Acc;

        TopsCount = Count;
    }

    // Same result as summing the heights of all rows before Index.
    private double GetOffset(int Index)
    {
        RebuildTops();

        if (Index < 0) Index = 0;
        if (Index > TopsCount) Index = TopsCount;

        return Tops[Index];
    }

    // Row that contains vertical position Y (binary search). Requires TopsCount > 0.
    private int FindRowAt(double Y)
    {
        int Lo = 0;
        int Hi = TopsCount - 1;

        while (Lo < Hi)
        {
            int Mid = (Lo + Hi + 1) >> 1;
            if (Tops[Mid] <= Y)
                Lo = Mid;
            else
                Hi = Mid - 1;
        }

        return Lo;
    }

    // ---------------------------------------------------------------
    //  Keyboard navigation
    // ---------------------------------------------------------------

    private void RevealSelectedLine()
    {
        var TargetLogicItem = RealLines[SelectLineID];

        Grid MatchGrid = null;
        foreach (var G in VisibleRows)
        {
            if (RowStyleWin.GetKey(G.View).Equals(TargetLogicItem.Key))
            {
                MatchGrid = G.View;
                break;
            }
        }

        if (MatchGrid != null && IsGridInViewport(MatchGrid))
        {
            SetSelectLine(MatchGrid, true);
        }
        else
        {
            Scroll.ScrollToVerticalOffset(GetOffset(SelectLineID));

            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                UpdateVisibleRows(true);

                foreach (var G in VisibleRows)
                {
                    if (RowStyleWin.GetKey(G.View).Equals(TargetLogicItem.Key))
                    {
                        SetSelectLine(G.View, true);
                        break;
                    }
                }
            }));
        }
    }

    public void UP()
    {
        if (RealLines.Count == 0)
            return;

        SelectLineID--;
        if (SelectLineID < 0)
            SelectLineID = RealLines.Count - 1;

        RevealSelectedLine();
    }

    public void Down()
    {
        if (RealLines.Count == 0)
            return;

        SelectLineID++;
        if (SelectLineID >= RealLines.Count)
        {
            SelectLineID = 0;
        }

        RevealSelectedLine();
    }

    private int GetRows()
    {
        return this.RealLines.Count;
    }

    public ModFile FileRef = null;
    public YDListView(ModFile File, Grid Parent)
    {
        this.FileRef = File;
        Style ScrollBarStyle = new Style(typeof(ScrollBar))
        {
            BasedOn = (Style)Application.Current.FindResource("for_scrollbar")
        };
        Parent.Resources.Add(typeof(ScrollBar), ScrollBarStyle);

        this.MainCanvas = new Canvas();
        this.MainCanvas.Background = null;
        this.MainCanvas.VerticalAlignment = VerticalAlignment.Top;
        this.MainCanvas.SnapsToDevicePixels = true;
        this.MainCanvas.UseLayoutRounding = true;
        this.MainCanvas.IsItemsHost = false;
        ScrollViewer OneScroll = new ScrollViewer();
        OneScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        OneScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        OneScroll.Content = MainCanvas;
        OneScroll.ScrollChanged += OnScrollChanged;

        this.Scroll = OneScroll;

        Parent.Children.Add(OneScroll);
        this.Parent = Parent;

        UpdateTrd = new Thread(() =>
        {
            while (!Disposed)
            {
                Thread.Sleep(100);

                if (CanSet && CanUpDate)
                {
                    try
                    {
                        // UpdateVisibleRows clears CanUpDate itself before it starts working,
                        // so a scroll event arriving during the update is not lost.
                        this.MainCanvas.Dispatcher.Invoke(new Action(() =>
                        {
                            UpdateVisibleRows();
                        }));
                    }
                    catch
                    {
                        CanUpDate = false;
                    }
                }
            }

        });
        UpdateTrd.IsBackground = true;
        UpdateTrd.Start();
    }

    // Stops the background polling thread. Optional, the thread is a background
    // thread and will not keep the process alive anyway.
    public void Dispose()
    {
        Disposed = true;
    }

    public Canvas GetMainCanvas()
    {
        return this.MainCanvas;
    }

    public void HotReload()
    {
        CanSet = false;

        MainCanvas.Children.Clear();
        VisibleRows = new List<VisibleGrid>();

        MainCanvas.Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() =>
            {
                UpdateVisibleRows(true);
                CanSet = true;
            })
        );
    }

    public void Clear()
    {
        this.SelectLineID = 0;
        this.CanSet = false;
        this.VisibleRows = new List<VisibleGrid>();
        this.RealLines.Clear();
        this.MainCanvas.Children.Clear();
        this.MainCanvas.Height = 0;
        this.Scroll.ScrollToTop();
        GC.Collect();
        this.CanSet = true;
    }

    private volatile bool CanUpDate = false;
    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        CanUpDate = true;
    }


    public void UpdateVisibleRows(bool ForceUPDate = false)
    {
        if (ForceUPDate)
        {
            CanUpDate = true;
        }

        if (!CanUpDate)
            return;

        CanUpDate = false;

        RebuildTops();
        int Count = TopsCount;

        double ScrollTop = this.Scroll.VerticalOffset;
        double ScrollHeight = this.Scroll.ExtentHeight;
        double ViewHeight = this.Scroll.ViewportHeight;

        int FirstIndex = 0;
        int LastIndex = -1;

        if (Count > 0)
        {
            int FirstVisibleRow = FindRowAt(ScrollTop);
            int LastVisibleRow = FindRowAt(ScrollTop + ViewHeight);

            FirstIndex = Math.Max(0, FirstVisibleRow - BufferRows);
            LastIndex = Math.Min(Count - 1, LastVisibleRow + BufferRows);

            bool IsNearBottom = ScrollTop + ViewHeight >= ScrollHeight - 150;
            if (IsNearBottom)
            {
                LastIndex = Count - 1;
            }
        }

        for (int I = MainCanvas.Children.Count - 1; I >= 0; I--)
        {
            FrameworkElement Fe = MainCanvas.Children[I] as FrameworkElement;
            if (Fe != null && Fe.Tag is int)
            {
                int RowIndex = (int)Fe.Tag;
                if (RowIndex < FirstIndex || RowIndex > LastIndex)
                {
                    Fe.PreviewMouseLeftButtonDown -= MainGrid_PreviewMouseLeftButtonDown;
                    MainCanvas.Children.RemoveAt(I);
                }
            }
        }

        Dictionary<int, FrameworkElement> Existing = new Dictionary<int, FrameworkElement>();
        foreach (UIElement Child in MainCanvas.Children)
        {
            FrameworkElement Fe = Child as FrameworkElement;
            if (Fe != null && Fe.Tag is int)
            {
                Existing[(int)Fe.Tag] = Fe;
            }
        }

        List<VisibleGrid> NewVisible = new List<VisibleGrid>(Math.Max(0, LastIndex - FirstIndex + 1));

        for (int I = FirstIndex; I <= LastIndex; I++)
        {
            FakeGrid Row = RealLines[I];
            Grid RowGrid = null;
            FrameworkElement Found;

            if (Existing.TryGetValue(I, out Found))
            {
                double Top = Tops[I];
                if (!(Math.Abs(Canvas.GetTop(Found) - Top) < 0.01))
                {
                    Canvas.SetTop(Found, Top);
                }

                RowGrid = Found as Grid;
            }
            else
            {
                RowGrid = UIHelper.CreateLine(FileRef, Row);
                RowGrid.Tag = I;
                try
                {
                    RowGrid.Width = this.Parent.ActualWidth - 15;
                }
                catch { }
                RowGrid.PreviewMouseLeftButtonDown += MainGrid_PreviewMouseLeftButtonDown;
                Canvas.SetTop(RowGrid, Tops[I]);
                Canvas.SetLeft(RowGrid, 0);
                MainCanvas.Children.Add(RowGrid);
                if (I.Equals(SelectLineID))
                {
                    SetSelectLine(RowGrid, false);
                }
            }

            if (RowGrid != null)
            {
                NewVisible.Add(new VisibleGrid(Row, RowGrid));
            }
        }

        VisibleRows = NewVisible;
    }


    public void QuickRefresh(ModFile Mod)
    {
        this.Parent.Dispatcher.BeginInvoke(new Action(() => {
            var Snapshot = this.VisibleRows;

            for (int i = 0; i < Snapshot.Count; i++)
            {
                bool RefCache = false;
                Snapshot[i].Data.SyncData(Mod, ref RefCache);

                Grid GetGrid = Snapshot[i].View;

                RowStyleWin.SetOriginal(GetGrid, Snapshot[i].Data.Source);
                RowStyleWin.SetTranslated(GetGrid, Snapshot[i].Data.Translated);
            }
        }));
    }

    public object AddLocker = new object();
    public int AddRowR(FakeGrid Item)
    {
        lock (AddLocker)
        {
            if (Item == null) return 0;

            this.RealLines.Add(Item);

            MainCanvas.Height += Item.Height;

            return this.RealLines.Count;
        }
    }

    public object DeleteLocker = new object();
    public void DeleteRow(int Offset)
    {
        lock (DeleteLocker)
        {
            if (Offset < 0 || Offset >= RealLines.Count)
                return;

            // Record The Row To Be Deleted
            var deletedRow = RealLines[Offset];
            double removedHeight = deletedRow.Height;

            // Remove Data Row
            RealLines.RemoveAt(Offset);

            // Find The Corresponding UI Element In Canvas
            FrameworkElement deletedElement = null;
            for (int i = 0; i < MainCanvas.Children.Count; i++)
            {
                if (MainCanvas.Children[i] is FrameworkElement fe && fe.Tag is int tag && tag == Offset)
                {
                    deletedElement = fe;
                    break;
                }
            }

            if (deletedElement != null)
            {
                // Unsubscribe Events And Clear Child Elements
                try { deletedElement.PreviewMouseLeftButtonDown -= MainGrid_PreviewMouseLeftButtonDown; } catch { }
                if (deletedElement is Panel p) p.Children.Clear();

                // Remove The Element From Canvas
                MainCanvas.Children.Remove(deletedElement);
            }

            // Adjust Remaining Elements' Tag And Position
            foreach (UIElement child in MainCanvas.Children)
            {
                if (child is FrameworkElement fe && fe.Tag is int tag)
                {
                    if (tag > Offset)
                    {
                        // Update Tag Index
                        fe.Tag = tag - 1;

                        // Move Element Up To Fill The Gap
                        double top = Canvas.GetTop(fe);
                        Canvas.SetTop(fe, top - removedHeight);
                    }
                }
            }

            // Drop The Deleted Row From VisibleRows (tags were already shifted above)
            VisibleRows = VisibleRows.Where(v => v.Data != deletedRow).ToList();

            // Recalculate Canvas Height
            MainCanvas.Height = RealLines.Sum(r => r.Height);

            // Refresh Visible Area Without Clearing The Whole Canvas
            UpdateVisibleRows(true);
        }
    }

    private void MainGrid_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SetSelectLine((Grid)sender, true);
    }

    public void ChangeFontColor(int FileUniqueKey, int R, int G, int B)
    {
        var GetLine = this.RealLines[SelectLineID];
        FontColorFinder.SetColor(FileUniqueKey, GetLine.Key, R, G, B);
        GetLine.SetFontColor(this, R, G, B);
    }

    public string GetSelectedKey()
    {
        if (this.RealLines.Count > this.SelectLineID)
        {
            return this.RealLines[this.SelectLineID].Key;
        }

        return string.Empty;
    }

    public FakeGrid GetSelectedGrid()
    {
        if (this.RealLines.Count > this.SelectLineID)
        {
            return this.RealLines[this.SelectLineID];
        }

        return null;
    }

    public void ScrollTo(int SelectID)
    {
        Scroll.ScrollToVerticalOffset(GetOffset(SelectID));

        UpdateVisibleRows(true);
    }

    public bool IsKeyInViewport(string Key)
    {
        foreach (var Get in this.VisibleRows)
        {
            if (RowStyleWin.GetKey(Get.View).Equals(Key))
            {
                if (IsGridInViewport(Get.View))
                {
                    return true;
                }

                break;
            }
        }

        return false;
    }

    public void SetSelectLineByKey(string Key, bool UPDate)
    {
        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            foreach (var Get in this.VisibleRows)
            {
                if (RowStyleWin.GetKey(Get.View).Equals(Key))
                {
                    SetSelectLine(Get.View, UPDate);
                    break;
                }
            }
        }));
    }

    public FakeGrid KeyToFakeGrid(string Key)
    {
        for (int i = 0; i < this.RealLines.Count; i++)
        {
            if (this.RealLines[i].Key.Equals(Key))
            {
                return this.RealLines[i];
            }
        }

        return null;
    }

    public int KeyToSelectID(string Key)
    {
        int TempSelectLineID = 0;

        for (int i = 0; i < this.RealLines.Count; i++)
        {
            if (this.RealLines[i].Key.Equals(Key))
            {
                TempSelectLineID = i;
                break;
            }
        }

        return TempSelectLineID;
    }

    public List<FakeGrid> QuickSearch(string Keyword)
    {
        if (string.IsNullOrEmpty(Keyword))
            return new List<FakeGrid>();

        return RealLines
            .Where(Line =>
                (!string.IsNullOrEmpty(Line.Source) &&
                 Line.Source.IndexOf(Keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                ||
                (!string.IsNullOrEmpty(Line.Translated) &&
                 Line.Translated.IndexOf(Keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                ||
                (!string.IsNullOrEmpty(Line.Key) &&
                Line.Key.IndexOf(Keyword, StringComparison.OrdinalIgnoreCase) >= 0)
            )
            .ToList();
    }

    public bool Goto(string Key)
    {
        if (string.IsNullOrEmpty(Key))
            return false;

        int TargetIndex = -1;
        for (int i = 0; i < RealLines.Count; i++)
        {
            if (RealLines[i].Key.Equals(Key))
            {
                TargetIndex = i;
                break;
            }
        }

        if (TargetIndex == -1)
            return false;

        Scroll.ScrollToVerticalOffset(GetOffset(TargetIndex));

        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            UpdateVisibleRows(true);

            foreach (var vg in VisibleRows)
            {
                if (RowStyleWin.GetKey(vg.View).Equals(Key))
                {
                    SetSelectLine(vg.View, true);
                    break;
                }
            }

            SelectLineID = TargetIndex;
        }));

        return true;
    }
}