using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DrakeAssetForge.Models;
using DrakeAssetForge.ViewModels;

namespace DrakeAssetForge.Views;

public partial class ProjectBrowserView : UserControl
{
    private Point? _pressPoint;
    private ProjectTreeNode? _pressNode;
    private bool _dragging;
    private bool _syncingFromTree;
    private readonly List<OwnedItemDocument> _dragItems = new();
    private ProjectTreeNode? _dropHighlight;
    private DispatcherTimer? _autoScrollTimer;
    private double _autoScrollDirection;

    public ProjectBrowserView()
    {
        InitializeComponent();

        // Multiple = Ctrl/Shift multi-select. Do not use Toggle — that fights normal click.
        ProjectTree.SelectionMode = SelectionMode.Multiple;

        // Tunnel so TreeViewItem selection/expand does not swallow the gesture.
        ProjectTree.AddHandler(InputElement.PointerPressedEvent, OnTreePointerPressed, RoutingStrategies.Tunnel);
        ProjectTree.AddHandler(InputElement.PointerMovedEvent, OnTreePointerMoved, RoutingStrategies.Tunnel);
        ProjectTree.AddHandler(InputElement.PointerReleasedEvent, OnTreePointerReleased, RoutingStrategies.Tunnel);
        ProjectTree.AddHandler(InputElement.PointerCaptureLostEvent, OnTreePointerCaptureLost, RoutingStrategies.Tunnel);
        ProjectTree.SelectionChanged += OnTreeSelectionChanged;
        DataContextChanged += OnDataContextChanged;
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_syncingFromTree || e.PropertyName != nameof(MainViewModel.SelectedProjectNode))
            return;
        if (Vm?.SelectedProjectNode is not { } node)
            return;

        // External selection (reload / paste) — select that node without fighting multi-select from the tree.
        if (ProjectTree.SelectedItems.OfType<ProjectTreeNode>().Any(n => ReferenceEquals(n, node)))
            return;

        ProjectTree.SelectedItems.Clear();
        ProjectTree.SelectedItems.Add(node);
    }

    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Vm == null)
            return;

        var selected = ProjectTree.SelectedItems.OfType<ProjectTreeNode>().ToList();
        _syncingFromTree = true;
        try
        {
            Vm.SyncProjectTreeSelection(selected);
        }
        finally
        {
            _syncingFromTree = false;
        }
    }

    private void OnBrowserKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm == null)
            return;

        if (Vm.SelectedProjectNode?.IsEditing == true)
            return;

        if (e.Key == Key.F2)
        {
            Vm.BeginRenameProjectNodeCommand.Execute(null);
            FocusRenameBoxSoon();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            Vm.CopySelectedProjectItemsCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.X && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            Vm.CutSelectedProjectItemsCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            Vm.PasteProjectItemsCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.D && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            Vm.DuplicateOwnedItemCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            SelectAllItems();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            Vm.DeleteProjectSelectionCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void SelectAllItems()
    {
        ProjectTree.SelectedItems.Clear();
        foreach (var node in EnumerateItemNodes(ProjectTree.Items?.OfType<ProjectTreeNode>() ?? Enumerable.Empty<ProjectTreeNode>()))
            ProjectTree.SelectedItems.Add(node);
        if (Vm != null)
        {
            _syncingFromTree = true;
            try
            {
                Vm.SyncProjectTreeSelection(ProjectTree.SelectedItems.OfType<ProjectTreeNode>().ToList());
            }
            finally
            {
                _syncingFromTree = false;
            }
        }
    }

    private static IEnumerable<ProjectTreeNode> EnumerateItemNodes(IEnumerable<ProjectTreeNode> roots)
    {
        foreach (var node in roots)
        {
            if (node.IsItem)
                yield return node;
            foreach (var child in EnumerateItemNodes(node.Children))
                yield return child;
        }
    }

    private void OnRenameBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm == null)
            return;
        if (e.Key == Key.Enter)
        {
            Vm.CommitRenameProjectNodeCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Vm.CancelRenameProjectNodeCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnRenameBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (Vm?.SelectedProjectNode?.IsEditing == true)
            Vm.CommitRenameProjectNodeCommand.Execute(null);
    }

    private void OnContextMenuOpening(object? sender, CancelEventArgs e)
    {
        if (Vm == null)
            return;

        if (!Vm.HasProjectItemSelection &&
            Vm.SelectedProjectNode?.Item != null &&
            !ReferenceEquals(Vm.SelectedOwnedItem, Vm.SelectedProjectNode.Item))
            Vm.SelectedOwnedItem = Vm.SelectedProjectNode.Item;

        if (DuplicateMenu != null)
            DuplicateMenu.IsEnabled = Vm.HasProjectItemSelection;
        if (CopyMenu != null)
            CopyMenu.IsEnabled = Vm.HasProjectItemSelection;
        if (CutMenu != null)
            CutMenu.IsEnabled = Vm.HasProjectItemSelection;
        if (PasteMenu != null)
            PasteMenu.IsEnabled = Vm.HasProjectClipboard;
        if (ExtractMenu != null)
            ExtractMenu.IsEnabled = Vm.CanExtractSelected;

        RebuildMoveToMenu();
    }

    private void RebuildMoveToMenu()
    {
        if (MoveToMenu == null || Vm == null)
            return;

        MoveToMenu.Items.Clear();
        MoveToMenu.IsEnabled = Vm.HasProjectItemSelection;
        if (!Vm.HasProjectItemSelection)
            return;

        void AddTarget(string header, string? groupLabel)
        {
            var item = new MenuItem { Header = header };
            var label = groupLabel;
            item.Click += (_, _) => Vm.MoveSelectedItemsIntoGroup(label);
            MoveToMenu.Items.Add(item);
        }

        AddTarget("(project root)", "(project root)");
        foreach (var group in Vm.ProjectGroups)
        {
            if (string.IsNullOrWhiteSpace(group) || group == "(project root)")
                continue;
            AddTarget(group, group);
        }
    }

    private static bool IsMultiSelectModifier(KeyModifiers mods) =>
        mods.HasFlag(KeyModifiers.Control) || mods.HasFlag(KeyModifiers.Shift);

    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        CancelDragVisual();
        StopAutoScroll();
        _pressPoint = null;
        _pressNode = null;
        _dragging = false;
        _dragItems.Clear();

        if (Vm?.SelectedProjectNode?.IsEditing == true)
            return;
        if (!e.GetCurrentPoint(ProjectTree).Properties.IsLeftButtonPressed)
            return;
        if (IsOverScrollBar(e.Source as Visual))
            return;

        // Ctrl/Shift are for multi-select — never arm move-drag on those clicks.
        if (IsMultiSelectModifier(e.KeyModifiers))
            return;

        var node = FindNode(e.Source as Visual);
        if (node?.Item == null)
            return;

        _pressPoint = e.GetPosition(ProjectTree);
        _pressNode = node;
    }

    private void OnTreePointerMoved(object? sender, PointerEventArgs e)
    {
        if (Vm == null || _pressPoint == null || _pressNode?.Item == null)
            return;
        if (!e.GetCurrentPoint(ProjectTree).Properties.IsLeftButtonPressed)
        {
            ClearPress();
            CancelDragVisual();
            StopAutoScroll();
            return;
        }

        // Modifier held mid-gesture = selection, not move.
        if (IsMultiSelectModifier(e.KeyModifiers))
        {
            ClearPress();
            CancelDragVisual();
            StopAutoScroll();
            return;
        }

        // Let the scrollbar work while a drag is armed / active.
        if (IsOverScrollBar(e.Source as Visual))
        {
            StopAutoScroll();
            return;
        }

        var pos = e.GetPosition(ProjectTree);
        if (!_dragging)
        {
            var delta = pos - _pressPoint.Value;
            // Slightly higher threshold so tiny clicks don't start a move.
            if (Math.Abs(delta.X) < 12 && Math.Abs(delta.Y) < 12)
                return;

            _dragging = true;
            _dragItems.Clear();
            _dragItems.AddRange(CollectDragItems(_pressNode));
            ProjectTree.Cursor = new Cursor(StandardCursorType.DragMove);
            var names = string.Join(", ", _dragItems.Take(3).Select(i => i.DisplayName));
            if (_dragItems.Count > 3)
                names += $" (+{_dragItems.Count - 3})";
            Vm.StatusText = $"Moving {_dragItems.Count} item(s) ({names}) — drop on a group, or use Move to…";
        }

        UpdateAutoScroll(pos.Y);
        var over = FindNodeAt(pos);
        SetDropHighlight(over);
    }

    private IEnumerable<OwnedItemDocument> CollectDragItems(ProjectTreeNode pressed)
    {
        var selected = ProjectTree.SelectedItems.OfType<ProjectTreeNode>()
            .Where(n => n.Item != null)
            .Select(n => n.Item!)
            .ToList();

        if (selected.Count > 0 && selected.Any(i => i.Id.Equals(pressed.Item!.Id, StringComparison.OrdinalIgnoreCase)))
            return selected.GroupBy(i => i.Id, StringComparer.OrdinalIgnoreCase).Select(g => g.First());

        return new[] { pressed.Item! };
    }

    private void OnTreePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        try
        {
            if (_dragging && _dragItems.Count > 0 && Vm != null &&
                !IsOverScrollBar(e.Source as Visual) &&
                !IsMultiSelectModifier(e.KeyModifiers))
            {
                var pos = e.GetPosition(ProjectTree);
                var target = FindNodeAt(pos);
                var group = ResolveDropGroup(target);
                Vm.MoveItemsIntoGroup(_dragItems.ToList(), group);
                e.Handled = true;
            }
        }
        finally
        {
            ClearPress();
            CancelDragVisual();
            StopAutoScroll();
        }
    }

    private void OnTreePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        ClearPress();
        CancelDragVisual();
        StopAutoScroll();
    }

    private void ClearPress()
    {
        _pressPoint = null;
        _pressNode = null;
        _dragging = false;
        _dragItems.Clear();
    }

    private void CancelDragVisual()
    {
        SetDropHighlight(null);
        if (ProjectTree != null)
            ProjectTree.Cursor = Cursor.Default;
    }

    private void UpdateAutoScroll(double localY)
    {
        var height = ProjectTree.Bounds.Height;
        if (height <= 0)
        {
            StopAutoScroll();
            return;
        }

        const double edge = 40;
        if (localY < edge)
            _autoScrollDirection = -1;
        else if (localY > height - edge)
            _autoScrollDirection = 1;
        else
        {
            StopAutoScroll();
            return;
        }

        EnsureAutoScrollTimer();
        if (_autoScrollTimer is { IsEnabled: false })
            _autoScrollTimer.Start();
    }

    private void EnsureAutoScrollTimer()
    {
        if (_autoScrollTimer != null)
            return;

        _autoScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _autoScrollTimer.Tick += (_, _) =>
        {
            if (_autoScrollDirection == 0 || !_dragging)
            {
                StopAutoScroll();
                return;
            }

            var sv = FindScrollViewer(ProjectTree);
            if (sv == null)
                return;

            var max = Math.Max(0, sv.Extent.Height - sv.Viewport.Height);
            var next = Math.Clamp(sv.Offset.Y + _autoScrollDirection * 22, 0, max);
            sv.Offset = new Vector(sv.Offset.X, next);
        };
    }

    private void StopAutoScroll()
    {
        _autoScrollDirection = 0;
        _autoScrollTimer?.Stop();
    }

    private static ScrollViewer? FindScrollViewer(Visual root) =>
        root.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    private static bool IsOverScrollBar(Visual? source)
    {
        for (var visual = source; visual != null; visual = visual.GetVisualParent())
        {
            if (visual is ScrollBar)
                return true;
        }

        return false;
    }

    private void SetDropHighlight(ProjectTreeNode? node)
    {
        if (ReferenceEquals(_dropHighlight, node))
            return;

        if (_dropHighlight != null)
            _dropHighlight.IsDropTarget = false;
        _dropHighlight = node;
        if (_dropHighlight != null)
            _dropHighlight.IsDropTarget = true;
    }

    private static string ResolveDropGroup(ProjectTreeNode? target)
    {
        if (target == null)
            return "(project root)";
        if (target.IsFolder)
            return string.IsNullOrEmpty(target.GroupPath) ? "(project root)" : target.GroupPath;
        return string.IsNullOrEmpty(target.Item?.GroupPath) ? "(project root)" : target.Item!.GroupPath;
    }

    private ProjectTreeNode? FindNodeAt(Point treeLocal)
    {
        var hit = ProjectTree.InputHitTest(treeLocal);
        return FindNode(hit as Visual);
    }

    private static ProjectTreeNode? FindNode(Visual? source)
    {
        for (var visual = source; visual != null; visual = visual.GetVisualParent())
        {
            if (visual is Control control && control.DataContext is ProjectTreeNode node)
                return node;
        }

        return null;
    }

    private void FocusRenameBoxSoon()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var box = this.GetVisualDescendants()
                .OfType<TextBox>()
                .FirstOrDefault(t => t.Classes.Contains("rename-box") && t.IsVisible);
            if (box == null)
                return;
            box.Focus();
            box.SelectAll();
        }, DispatcherPriority.Background);
    }
}
