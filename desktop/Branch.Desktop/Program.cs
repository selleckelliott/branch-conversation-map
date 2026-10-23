using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Branch.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "--self-test") return SelfTest(args[1]);
        if (args.FirstOrDefault() == "--check-codex") return CheckConnection(args[1]).GetAwaiter().GetResult();
        if (args.FirstOrDefault() == "--preview") return Preview(args[1]);
        using var singleInstance = new Mutex(true, "Local\\BranchDesktopOverlay", out bool first);
        if (!first) { var other = Native.FindWindow(null, MapWindow.WindowTitle); if (other != IntPtr.Zero) Native.SendMessage(other, 0x8033, IntPtr.Zero, IntPtr.Zero); return 0; }
        var app = new Application();
        app.DispatcherUnhandledException += (_, e) => { MessageBox.Show(e.Exception.Message, "Branch", MessageBoxButton.OK, MessageBoxImage.Information); e.Handled = true; };
        return app.Run(new MapWindow(args.Contains("--demo")));
    }

    private static void Report(string path, object report)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static int SelfTest(string report)
    {
        var passed = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed.Add(name); }
        try
        {
            var uuid = "01a1698c-f76f-74b0-a413-3fb8d880568d";
            Check(Data.ChatLink(uuid) == "codex://threads/" + uuid, "documented local thread links");
            bool invalid = false; try { Data.ChatLink("../../settings"); } catch (InvalidDataException) { invalid = true; }
            Check(invalid, "reject invalid navigation targets");
            var bounds = Data.Bounds(50, 60, 1440, 1000, new Insets());
            Check(bounds is { Left: 310, Top: 152, Width: 1162, Height: 698 }, "sidebar and composer insets");
            Check(Data.Bounds(0, 0, 400, 300, new Insets()) == null, "hide rather than cover composer in small windows");
            var nodes = new List<MapNode> { new("root", "Project", "", new()), new("a", "A", "", new() { "root" }), new("b", "B", "", new() { "root" }), new("merge", "Merged", "", new() { "a", "b" }) };
            var graph = Data.Arrange(nodes, new());
            Check(graph.Positions["merge"].Y > graph.Positions["a"].Y, "multi-parent merge layout");
            Check(Data.Arrange(nodes, new() { "a" }).Positions.ContainsKey("merge"), "merge reachable through an expanded parent");
            Check(Data.Arrange(nodes, new() { "root" }).Positions.Count == 1, "collapse project descendants");
            bool cycle = false; try { Data.Arrange(new() { new("a", "A", "", new() { "b" }), new("b", "B", "", new() { "a" }) }, new()); } catch (InvalidDataException) { cycle = true; }
            Check(cycle, "reject cyclic graph metadata");
            var turns = JsonNode.Parse("""
                [{"id":"turn1","status":"completed","items":[{"id":"u1","type":"userMessage","content":[{"type":"text","text":"Question"},{"type":"image","url":"ignored"}]},{"id":"hidden","type":"reasoning","content":["hidden"]},{"id":"a1","type":"agentMessage","text":"Answer"}]},{"id":"turn2","status":"inProgress","items":[{"id":"a2","type":"agentMessage","text":"Partial"}]}]
                """)!.AsArray();
            var messages = Data.ParseTurns(turns.Select(n => n!));
            Check(messages.Count == 2 && messages[1].Text == "Answer", "completed user and assistant messages only");
            Check(messages[0].Text.Contains("Non-text attachment"), "visible non-text attachment placeholders");
            Check(messages[1].TurnId == "turn1" && messages[1].Id == "a1", "stable native turn and message IDs");
            Check(Data.ParseChat(JsonNode.Parse("""{"id":"native","preview":"Title","cwd":"C:/project","status":{"type":"idle"},"forkedFromId":"parent"}""")).Parent == "parent", "native fork ancestry parsing");
            var roundTrip = JsonSerializer.Deserialize<DesktopState>(JsonSerializer.Serialize(new DesktopState { Parents = new() { ["merge"] = new() { "a", "b" } } }))!;
            Check(roundTrip.Parents["merge"].SequenceEqual(new[] { "a", "b" }), "merge provenance settings round trip");
            Report(report, new { ok = true, passed, count = passed.Count }); return 0;
        }
        catch (Exception error) { Report(report, new { ok = false, passed, error = error.Message }); return 1; }
    }

    private static async Task<int> CheckConnection(string report)
    {
        using var client = new CodexClient();
        try { await client.StartAsync(); var chats = await client.ListAsync(); var source = chats.FirstOrDefault(c => c.Cwd.TrimEnd('\\') == Environment.CurrentDirectory.TrimEnd('\\')); var history = source == null ? null : await client.ReadAsync(source); Report(report, new { ok = true, count = chats.Count, previewMessages = history?.Messages.Count, note = "Read-only handshake, thread listing, and current-project preview. No messages sent; no chats created." }); return 0; }
        catch (Exception error) { Report(report, new { ok = false, error = error.Message }); return 1; }
    }

    private static int Preview(string path)
    {
        var app = new Application(); var window = new MapWindow(true); window.LoadDemo();
        var content = (FrameworkElement)window.Content; content.Measure(new Size(1100, 690)); content.Arrange(new Rect(0, 0, 1100, 690)); content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1100, 690, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var output = File.Create(path); png.Save(output); window.Close(); return 0;
    }
}
