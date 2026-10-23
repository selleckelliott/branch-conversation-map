using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Branch.Desktop;

public sealed class MapWindow : Window
{
    public const string WindowTitle = "Branch — Codex conversation map";
    private readonly CodexClient client = new();
    private DesktopState state = new();
    private readonly ComboBox project = new() { MinWidth = 190, MaxWidth = 350, Margin = new(6), Padding = new(7) };
    private readonly ComboBox host = new() { MinWidth = 150, MaxWidth = 260, Margin = new(6), Padding = new(7) };
    private readonly CheckBox attach = new() { Content = "Follow Codex", Margin = new(9), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox search = new() { Width = 170, Padding = new(7), Margin = new(6), ToolTip = "Find a conversation" };
    private readonly TextBlock status = new() { Margin = new(16, 7, 16, 7), FontSize = 11, Foreground = Brush("#859078"), Text = "Connecting to Codex…" };
    private readonly Canvas canvas = new() { Background = Brush("#F4F7EF") };
    private readonly ScrollViewer mapScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly StackPanel preview = new() { Margin = new(20) };
    private readonly ColumnDefinition previewColumn = new() { Width = new GridLength(300) };
    private readonly HashSet<string> selected = new();
    private readonly HashSet<string> collapsed = new();
    private readonly DispatcherTimer followTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private List<Chat> chats = new();
    private string? activeId;
    private ChatHistory? history;
    private bool connected, busy, shortcutRegistered, demo;
    private int previewRequest;
    private IntPtr handle;
    private Point? panStart;
    private double panX, panY, zoom = .85;
    private readonly ScaleTransform scale = new(.85, .85);

    public MapWindow(bool demo = false)
    {
        this.demo = demo;
        Title = WindowTitle; Width = 1100; Height = 690; MinWidth = 500; MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = Brush("#FCFDF9");
        Foreground = Brush("#34462F"); FontFamily = new FontFamily("Segoe UI"); FontSize = 12;
        var outer = new Grid { Background = Background }; TextElement.SetForeground(outer, Foreground); foreach (var height in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto }) outer.RowDefinitions.Add(new RowDefinition { Height = height });
        var top = new WrapPanel { Margin = new(12, 7, 12, 4) };
        top.Children.Add(new TextBlock { Text = "⑂ branch", FontSize = 23, FontWeight = FontWeights.Bold, Margin = new(5, 2, 20, 5) });
        top.Children.Add(Btn("Refresh", async () => await RefreshAsync()));
        top.Children.Add(Btn("Compare", async () => await CompareAsync()));
        top.Children.Add(Btn("Merge selected", async () => await MergeAsync()));
        top.Children.Add(Btn("Fit", Fit));
        top.Children.Add(Btn("−", () => Zoom(-.1)));
        top.Children.Add(Btn("+", () => Zoom(.1)));
        top.Children.Add(Btn("Hide map", HideMap));
        outer.Children.Add(top);
        var filters = new WrapPanel { Margin = new(12, 0, 12, 5) };
        filters.Children.Add(new TextBlock { Text = "Project", Margin = new(5, 12, 0, 0) }); filters.Children.Add(project);
        filters.Children.Add(new TextBlock { Text = "Find", Margin = new(5, 12, 0, 0) }); filters.Children.Add(search);
        filters.Children.Add(new TextBlock { Text = "Window", Margin = new(5, 12, 0, 0) }); filters.Children.Add(host); filters.Children.Add(attach);
        filters.Children.Add(Btn("Alignment", AlignmentDialog));
        Grid.SetRow(filters, 1); outer.Children.Add(filters);
        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition()); body.ColumnDefinitions.Add(previewColumn);
        mapScroll.Content = canvas; canvas.LayoutTransform = scale; body.Children.Add(mapScroll);
        var inspector = new ScrollViewer { Content = preview, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brush("#FCFDF9") };
        Grid.SetColumn(inspector, 1); body.Children.Add(new Border { Child = inspector, BorderThickness = new(1, 0, 0, 0), BorderBrush = Brush("#DDE4D5") });
        Grid.SetColumn(body.Children[^1], 1); Grid.SetRow(body, 2); outer.Children.Add(body);
        Grid.SetRow(status, 3); outer.Children.Add(status); Content = outer;
        project.SelectionChanged += (_, _) => { project.ToolTip = project.SelectedItem as string; selected.Clear(); collapsed.Clear(); Draw(); };
        search.TextChanged += (_, _) => Draw();
        attach.Checked += (_, _) => AttachHost(); attach.Unchecked += (_, _) => DetachHost();
        host.SelectionChanged += (_, _) => { if (attach.IsChecked == true) AttachHost(); };
        followTimer.Tick += (_, _) => FollowHost();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { HideMap(); e.Handled = true; } };
        mapScroll.PreviewMouseWheel += (_, e) => { if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { Zoom(e.Delta > 0 ? .1 : -.1); e.Handled = true; } };
        canvas.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource != canvas) return; panStart = e.GetPosition(mapScroll); panX = mapScroll.HorizontalOffset; panY = mapScroll.VerticalOffset; canvas.CaptureMouse(); };
        canvas.MouseMove += (_, e) => { if (panStart is not Point start) return; var point = e.GetPosition(mapScroll); mapScroll.ScrollToHorizontalOffset(panX + start.X - point.X); mapScroll.ScrollToVerticalOffset(panY + start.Y - point.Y); };
        canvas.MouseLeftButtonUp += (_, _) => { panStart = null; canvas.ReleaseMouseCapture(); };
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle).AddHook(WindowMessage);
            shortcutRegistered = Native.RegisterHotKey(handle, 7, 0x0002 | 0x0004 | 0x4000, 0x20); // Ctrl+Shift+Space; no repeat.
        };
        Loaded += async (_, _) =>
        {
            if (demo) { LoadDemo(); return; }
            try { state = DesktopState.Load(); await client.StartAsync(); connected = true; await RefreshAsync(); }
            catch (Exception error) { ShowError(error); }
        };
        Closed += (_, _) => { followTimer.Stop(); Native.UnregisterHotKey(handle, 7); client.Dispose(); };
        SizeChanged += (_, _) => previewColumn.Width = new GridLength(ActualWidth < 850 ? 235 : 300);
    }

    private static Brush Brush(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    private static Button Btn(string label, Action action)
    {
        var button = new Button { Content = label, Padding = new(11, 7, 11, 7), Margin = new(3), Background = Brush("#EEF3E7"), Foreground = Brush("#4B6440"), BorderBrush = Brush("#D7E0CD"), BorderThickness = new(1), Cursor = Cursors.Hand };
        button.Click += (_, _) => action(); return button;
    }
    private void Info(string text) => status.Text = text;
    private void ShowError(Exception error) { Info(error.Message); MessageBox.Show(this, error.Message, "Branch", MessageBoxButton.OK, MessageBoxImage.Information); }
    private List<Chat> ProjectChats() => chats.Where(c => c.Cwd == (project.SelectedItem as string ?? "")).ToList();
    private string ProjectName() { string path = project.SelectedItem as string ?? ""; return string.IsNullOrEmpty(path) ? "Standalone conversations" : new DirectoryInfo(path).Name; }

    private async Task RefreshAsync()
    {
        if (busy) return;
        if (demo) { Draw(); return; }
        if (!connected) { Info("Restart Branch after fixing the Codex connection."); return; }
        busy = true; Info("Reading saved Codex conversations…");
        try
        {
            string? previous = project.SelectedItem as string;
            chats = await client.ListAsync();
            project.ItemsSource = chats.Select(c => c.Cwd).Distinct().OrderBy(c => c).ToList();
            project.SelectedItem = previous != null && project.Items.Contains(previous) ? previous : project.Items.Contains(Environment.CurrentDirectory) ? Environment.CurrentDirectory : project.Items.Cast<string>().FirstOrDefault();
            var oldHost = host.SelectedItem as HostWindow; var hosts = Native.Hosts(); host.ItemsSource = hosts;
            host.SelectedItem = hosts.FirstOrDefault(h => h.Handle == oldHost?.Handle) ?? (hosts.Count == 1 ? hosts[0] : null);
            Info($"{chats.Count} saved chats · select a project · Ctrl+Shift+Space toggles the map" + (shortcutRegistered ? "" : " (shortcut unavailable; use the taskbar)"));
            Draw();
        }
        catch (Exception error) { ShowError(error); }
        finally { busy = false; }
    }

    private void Draw()
    {
        canvas.Children.Clear();
        var current = ProjectChats();
        var ids = current.Select(c => c.Id).ToHashSet();
        var nodes = new List<MapNode> { new("root", ProjectName(), "CODEX PROJECT", new()) };
        nodes.AddRange(current.Select(c => new MapNode(c.Id, c.Title, c.Status == "active" ? "Working in Codex" : "Saved conversation",
            state.Parents.TryGetValue(c.Id, out var parents) ? parents.Where(ids.Contains).DefaultIfEmpty("root").ToList() : c.Parent != null && ids.Contains(c.Parent) ? new() { c.Parent } : new() { "root" })));
        MapLayout graph;
        try { graph = Data.Arrange(nodes, collapsed); } catch (Exception error) { Info(error.Message); return; }
        canvas.Width = graph.Width; canvas.Height = graph.Height;
        foreach (var node in nodes.Where(n => graph.Positions.ContainsKey(n.Id)))
        {
            var target = graph.Positions[node.Id];
            foreach (var parentId in node.Parents.Where(graph.Positions.ContainsKey))
            {
                var parent = graph.Positions[parentId]; double startX = parent.X + 108, startY = parent.Y + 119, endX = target.X + 108, endY = target.Y;
                var figure = new PathFigure { StartPoint = new Point(startX, startY) };
                figure.Segments.Add(new BezierSegment(new Point(startX, (startY + endY) / 2), new Point(endX, (startY + endY) / 2), new Point(endX, endY), true));
                canvas.Children.Add(new System.Windows.Shapes.Path { Data = new PathGeometry(new[] { figure }), Stroke = Brush(node.Parents.Count > 1 ? "#A590AF" : "#B9C8AB"), StrokeThickness = 1.5, StrokeDashArray = node.Parents.Count > 1 ? new DoubleCollection { 5, 4 } : null });
            }
        }
        foreach (var node in nodes.Where(n => graph.Positions.ContainsKey(n.Id)))
        {
            var pos = graph.Positions[node.Id];
            var panel = new StackPanel();
            var top = new DockPanel();
            top.Children.Add(new TextBlock { Text = node.Id == "root" ? "PROJECT" : node.Parents.Count > 1 ? "MERGED CHAT" : "CONVERSATION", FontSize = 9, Foreground = Brush("#8C9C7D") });
            if (node.Id != "root")
            {
                var check = new CheckBox { IsChecked = selected.Contains(node.Id), HorizontalAlignment = HorizontalAlignment.Right, ToolTip = "Select for comparison or merging" };
                check.Click += (_, _) => { if (check.IsChecked == true) selected.Add(node.Id); else selected.Remove(node.Id); Info($"{selected.Count} selected for comparison or merging"); };
                top.Children.Add(check);
            }
            panel.Children.Add(top);
            panel.Children.Add(new TextBlock { Text = node.Title, FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new(0, 12, 0, 7), TextWrapping = TextWrapping.Wrap, MaxHeight = 42, TextTrimming = TextTrimming.CharacterEllipsis });
            panel.Children.Add(new TextBlock { Text = node.Subtitle, FontSize = 10, Foreground = Brush("#8D9A7E") });
            var card = new Border { Width = 218, Height = 120, Background = Brush(node.Id == "root" ? "#E8F0DD" : "#FFFFFF"), BorderBrush = Brush(node.Id == activeId ? "#698457" : "#D9E2CF"), BorderThickness = new(node.Id == activeId ? 2 : 1), CornerRadius = new(9), Padding = new(14), Child = panel,
                Opacity = string.IsNullOrEmpty(search.Text) || node.Title.Contains(search.Text, StringComparison.OrdinalIgnoreCase) || node.Id == "root" ? 1 : .25, Cursor = Cursors.Hand };
            Canvas.SetLeft(card, pos.X); Canvas.SetTop(card, pos.Y); canvas.Children.Add(card);
            card.MouseLeftButtonUp += async (_, e) => { if (e.OriginalSource is CheckBox || node.Id == "root") return; await SelectAsync(node.Id); e.Handled = true; };
            if (nodes.Any(n => n.Parents.Contains(node.Id)))
            {
                var collapse = Btn(collapsed.Contains(node.Id) ? "+" : "−", () => { if (!collapsed.Add(node.Id)) collapsed.Remove(node.Id); Draw(); });
                collapse.Padding = new(8, 0, 8, 0); Canvas.SetLeft(collapse, pos.X + 92); Canvas.SetTop(collapse, pos.Y + 124); canvas.Children.Add(collapse);
            }
        }
    }

    private void Fit() { zoom = Math.Clamp((mapScroll.ViewportWidth - 35) / Math.Max(780, canvas.Width), .2, 1); scale.ScaleX = scale.ScaleY = zoom; }
    private void Zoom(double change) { zoom = Math.Clamp(zoom + change, .2, 1.6); scale.ScaleX = scale.ScaleY = zoom; }

    private async Task SelectAsync(string id)
    {
        if (busy) return;
        int request = ++previewRequest; activeId = id; history = null; Draw(); preview.Children.Clear();
        var chat = chats.First(c => c.Id == id);
        preview.Children.Add(new TextBlock { Text = chat.Title, FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        preview.Children.Add(Btn("Open in Codex", () => OpenChat(chat.Id)));
        if (demo) { preview.Children.Add(new TextBlock { Text = "A native preview of your conversation appears here.", Margin = new(0, 16, 0, 0), TextWrapping = TextWrapping.Wrap }); return; }
        try
        {
            Info("Reading completed messages…"); var read = await client.ReadAsync(chat); if (request != previewRequest) return;
            history = read;
            if (read.Chat.Parent != null) { chats[chats.FindIndex(c => c.Id == id)] = read.Chat; Draw(); }
            preview.Children.Add(new TextBlock { Text = $"{read.Messages.Count} text messages · completed turns", FontSize = 10, Margin = new(0, 10, 0, 12), Foreground = Brush("#91A081") });
            foreach (var message in read.Messages)
            {
                var text = new TextBox { Text = message.Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), Background = Brushes.Transparent, Margin = new(0, 5, 0, 5) };
                preview.Children.Add(new TextBlock { Text = message.Role.ToUpperInvariant(), FontSize = 9, Foreground = Brush("#8E9A7C"), Margin = new(0, 12, 0, 0) });
                preview.Children.Add(text);
                if (message == read.Messages.Last(m => m.TurnId == message.TurnId)) preview.Children.Add(Btn("Branch after this turn", () => ForkDialog(read.Chat, message.TurnId)));
            }
            Info("Open a chat to continue in Codex’s existing composer. Branch origins use completed turns.");
        }
        catch (Exception error) { if (request == previewRequest) ShowError(error); }
    }

    private void OpenChat(string id)
    {
        try { Process.Start(new ProcessStartInfo(Data.ChatLink(id)) { UseShellExecute = true }); HideMap(); }
        catch (Exception error) { ShowError(error); }
    }

    private void ForkDialog(Chat chat, string turnId)
    {
        if (busy) return;
        var content = new StackPanel(); var name = new TextBox { Text = "New direction", Padding = new(9), Margin = new(0, 12, 0, 12) };
        content.Children.Add(new TextBlock { Text = "Create a native Codex branch through this completed turn. Continue writing in Codex after opening it.", TextWrapping = TextWrapping.Wrap }); content.Children.Add(name);
        var dialog = Dialog("A new direction", content);
        content.Children.Add(Btn("Create branch", async () =>
        {
            if (busy || string.IsNullOrWhiteSpace(name.Text)) return; busy = true; content.IsEnabled = false;
            try { var fork = await client.ForkAsync(chat, turnId, name.Text.Trim()); state.Parents[fork.Id] = new() { chat.Id }; state.Save(); chats.Insert(0, fork); selected.Clear(); dialog.Close(); Draw(); OpenChat(fork.Id); }
            catch (Exception error) { ShowError(error); }
            finally { busy = false; content.IsEnabled = true; }
        })); dialog.ShowDialog();
    }

    private async Task<List<ChatHistory>> SelectedHistoriesAsync()
    {
        if (selected.Count < 2) throw new InvalidOperationException("Select at least two conversations using the checkboxes on their cards.");
        if (selected.Count > 8) throw new InvalidOperationException("Select up to eight conversations at a time.");
        var result = new List<ChatHistory>(); foreach (var id in selected) result.Add(await client.ReadAsync(chats.First(c => c.Id == id))); return result;
    }

    private async Task CompareAsync()
    {
        if (busy) return; if (selected.Count != 2) { Info("Select exactly two conversations to compare."); return; } busy = true;
        try
        {
            var sources = await SelectedHistoriesAsync();
            var leftIds = sources[0].Messages.Select(m => m.Id + "\0" + m.Text).ToHashSet();
            var rightIds = sources[1].Messages.Select(m => m.Id + "\0" + m.Text).ToHashSet();
            int shared = leftIds.Intersect(rightIds).Count();
            var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = $"{shared} shared messages. Unique additions are shown below.", Margin = new(0, 0, 0, 15) });
            for (int i = 0; i < 2; i++)
            {
                panel.Children.Add(new TextBlock { Text = sources[i].Chat.Title, FontSize = 17, FontWeight = FontWeights.SemiBold });
                var other = i == 0 ? rightIds : leftIds;
                panel.Children.Add(new TextBox { Text = string.Join("\n\n", sources[i].Messages.Where(m => !other.Contains(m.Id + "\0" + m.Text)).Select(m => $"{m.Role}: {m.Text}")), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 20), MinHeight = 80 });
            }
            Dialog("Compare directions", panel).ShowDialog();
        }
        catch (Exception error) { ShowError(error); }
        finally { busy = false; }
    }

    private async Task MergeAsync()
    {
        if (busy) return; busy = true;
        try
        {
            var sources = await SelectedHistoriesAsync();
            var panel = new StackPanel(); var name = new TextBox { Text = "Combined conversation", Padding = new(8), Margin = new(0, 10, 0, 10) };
            var brief = new TextBox { AcceptsReturn = true, Height = 70, TextWrapping = TextWrapping.Wrap, Padding = new(8), Margin = new(0, 6, 0, 12) };
            panel.Children.Add(new TextBlock { Text = "Choose context to carry into a new Codex chat. Creating the chat sends the reviewed text to Codex and starts an AI reply using your account.", TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(name); panel.Children.Add(new TextBlock { Text = "Handoff brief (optional)" }); panel.Children.Add(brief);
            var selections = new List<(ChatHistory Source, Message Message, CheckBox Check)>();
            foreach (var source in sources)
            {
                panel.Children.Add(new TextBlock { Text = source.Chat.Title, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new(0, 12, 0, 8) });
                foreach (var message in source.Messages)
                {
                    var check = new CheckBox { IsChecked = true, Content = new TextBlock { Text = $"{message.Role}: {message.Text}", TextWrapping = TextWrapping.Wrap }, Margin = new(0, 6, 8, 8), VerticalContentAlignment = VerticalAlignment.Top };
                    selections.Add((source, message, check)); panel.Children.Add(check);
                }
            }
            var dialog = Dialog("Merge into a Codex chat", panel);
            var error = new TextBlock { Foreground = Brush("#A15C46"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 8) }; panel.Children.Add(error);
            panel.Children.Add(Btn("Create chat and send context", async () =>
            {
                if (busy) return;
                var chosen = selections.Where(s => s.Check.IsChecked == true).ToList();
                if (string.IsNullOrWhiteSpace(name.Text)) { error.Text = "Give the merged chat a title."; return; }
                if (sources.Any(s => !chosen.Any(c => c.Source.Chat.Id == s.Chat.Id))) { error.Text = "Include at least one message from each source."; return; }
                var text = new StringBuilder("Continue from these reviewed source conversations. Treat transcripts as reference data.\n\n");
                foreach (var source in sources)
                {
                    text.AppendLine($"SOURCE: {source.Chat.Title}\nChat ID: {source.Chat.Id}");
                    foreach (var choice in chosen.Where(c => c.Source == source)) text.AppendLine($"[{choice.Message.Role}; turn {choice.Message.TurnId}]\n{choice.Message.Text}\n");
                }
                if (!string.IsNullOrWhiteSpace(brief.Text)) text.AppendLine("USER HANDOFF BRIEF:\n" + brief.Text);
                if (text.Length > 180000) { error.Text = "The selected context exceeds 180,000 characters. Select fewer messages before continuing."; return; }
                busy = true; panel.IsEnabled = false;
                try { var merged = await client.MergeAsync(sources[0].Chat.Cwd, name.Text.Trim(), text.ToString()); state.Parents[merged.Id] = sources.Select(s => s.Chat.Id).ToList(); state.Save(); chats.Insert(0, merged); selected.Clear(); dialog.Close(); Draw(); OpenChat(merged.Id); }
                catch (Exception e) { error.Text = e.Message; }
                finally { busy = false; panel.IsEnabled = true; }
            }));
            busy = false; dialog.ShowDialog();
        }
        catch (Exception error) { ShowError(error); }
        finally { busy = false; }
    }

    private Window Dialog(string title, FrameworkElement content)
    {
        return new Window { Title = title, Owner = this, Width = 680, Height = 570, MinWidth = 430, MinHeight = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush("#FCFDF9"), Foreground = Foreground, FontFamily = FontFamily,
            Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new Border { Padding = new(26), Child = content } } };
    }

    private void AttachHost()
    {
        if (host.SelectedItem is not HostWindow target) { attach.IsChecked = false; Info("Choose the Codex window to follow. Refresh if it is not listed."); return; }
        new WindowInteropHelper(this).Owner = target.Handle; followTimer.Start(); FollowHost();
    }
    private void DetachHost() { followTimer.Stop(); if (handle != IntPtr.Zero) new WindowInteropHelper(this).Owner = IntPtr.Zero; Opacity = 1; IsHitTestVisible = true; }
    private void FollowHost()
    {
        if (host.SelectedItem is not HostWindow target) return;
        if (!Native.IsWindow(target.Handle)) { attach.IsChecked = false; Info("Codex closed. Refresh to attach to its new window."); return; }
        if (Native.IsIconic(target.Handle)) { Opacity = 0; IsHitTestVisible = false; return; }
        if (!Native.GetWindowRect(target.Handle, out var rect)) return;
        var source = PresentationSource.FromVisual(this); var transform = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        double sx = transform.M11, sy = transform.M22;
        var inset = state.Insets;
        var bounds = Data.Bounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, new Insets(inset.Left * sx, inset.Top * sy, inset.Right * sx, inset.Bottom * sy));
        if (bounds == null) { Opacity = 0; IsHitTestVisible = false; Info("Enlarge the Codex window or reduce the overlay insets."); return; }
        Opacity = 1; IsHitTestVisible = true;
        Native.SetWindowPos(handle, IntPtr.Zero, (int)bounds.Left, (int)bounds.Top, (int)bounds.Width, (int)bounds.Height, 0x0010 | 0x0004); // NOACTIVATE | NOZORDER
    }

    private void AlignmentDialog()
    {
        var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = "Reserve space for Codex’s sidebar, header, and composer. Adjust these values to match your layout.", TextWrapping = TextWrapping.Wrap });
        var boxes = new List<TextBox>(); var old = state.Insets;
        foreach (var value in new[] { ("Sidebar / left", old.Left), ("Header / top", old.Top), ("Right edge", old.Right), ("Composer / bottom", old.Bottom) })
        {
            panel.Children.Add(new TextBlock { Text = value.Item1, Margin = new(0, 15, 0, 5) });
            var box = new TextBox { Text = value.Item2.ToString(System.Globalization.CultureInfo.InvariantCulture), Padding = new(7) }; boxes.Add(box); panel.Children.Add(box);
        }
        var dialog = Dialog("Overlay alignment", panel);
        panel.Children.Add(Btn("Save alignment", () =>
        {
            var values = new List<double>(); foreach (var box in boxes) { if (!double.TryParse(box.Text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out double number) || number < 0 || number > 1000) { Info("Insets must be numbers from 0 to 1,000."); return; } values.Add(number); }
            state.Insets = new Insets(values[0], values[1], values[2], values[3]);
            try { state.Save(); dialog.Close(); FollowHost(); } catch (Exception error) { ShowError(error); }
        })); dialog.ShowDialog();
    }

    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((message == 0x0312 && wParam.ToInt32() == 7) || message == 0x8033) { Toggle(); handled = true; } return IntPtr.Zero;
    }
    private void HideMap() { if (shortcutRegistered) Hide(); else WindowState = WindowState.Minimized; }
    private void Toggle() { if (IsVisible && WindowState != WindowState.Minimized) HideMap(); else { Show(); WindowState = WindowState.Normal; Activate(); } }

    public void LoadDemo()
    {
        chats = new() {
            new("00000000-0000-0000-0000-000000000001", "Product vision", "AI Branch Interface", null, "idle"),
            new("00000000-0000-0000-0000-000000000002", "Interface directions", "AI Branch Interface", "00000000-0000-0000-0000-000000000001", "idle"),
            new("00000000-0000-0000-0000-000000000003", "Explore the landscape", "AI Branch Interface", "00000000-0000-0000-0000-000000000001", "idle") };
        project.ItemsSource = new[] { "AI Branch Interface" }; project.SelectedIndex = 0; Draw();
        _ = SelectAsync(chats[1].Id);
        Info("Native Windows overlay · sample data · no browser or local web server");
    }
}
