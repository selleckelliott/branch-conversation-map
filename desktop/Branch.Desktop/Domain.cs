using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Branch.Desktop;

public record Chat(string Id, string Title, string Cwd, string? Parent, string Status);
public record Message(string Id, string TurnId, string Role, string Text);
public record ChatHistory(Chat Chat, List<Message> Messages);
public record MapNode(string Id, string Title, string Subtitle, List<string> Parents);
public record MapPosition(double X, double Y);
public record MapLayout(Dictionary<string, MapPosition> Positions, double Width, double Height);
public record Insets(double Left = 260, double Top = 92, double Right = 18, double Bottom = 210);
public record OverlayBounds(double Left, double Top, double Width, double Height);

public static class Data
{
    public static string Text(JsonNode? node, string key, string fallback = "") => node?[key]?.GetValue<string>() ?? fallback;

    public static Chat ParseChat(JsonNode? n)
    {
        string id = Text(n, "id");
        if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException("Codex returned a chat without an ID.");
        return new Chat(id, Text(n, "name", Text(n, "preview", "Untitled chat")), Text(n, "cwd"),
            n?["forkedFromId"]?.GetValue<string>(), Text(n?["status"], "type", "unknown"));
    }

    public static List<Message> ParseTurns(IEnumerable<JsonNode> turns)
    {
        var messages = new List<Message>();
        foreach (var turn in turns)
        {
            // A live partial reply is not stable source context for a branch or merge.
            if (Text(turn, "status") != "completed") continue;
            string turnId = Text(turn, "id");
            if (turn["items"] is not JsonArray items) continue;
            foreach (var item in items)
            {
                string type = Text(item, "type"), role, text;
                if (type == "userMessage")
                {
                    role = "You";
                    text = string.Join("\n", (item?["content"] as JsonArray ?? new JsonArray()).Select(c =>
                        Text(c, "type") == "text" ? Text(c, "text") : "[Non-text attachment: open the original chat to view]"));
                }
                else if (type == "agentMessage") { role = "Assistant"; text = Text(item, "text"); }
                else continue; // No raw reasoning, tool output, or hidden instructions.
                if (!string.IsNullOrWhiteSpace(text)) messages.Add(new Message(Text(item, "id"), turnId, role, text));
            }
        }
        return messages;
    }

    public static string ChatLink(string id)
    {
        if (!Guid.TryParse(id, out _)) throw new InvalidDataException("This version can open local Codex chats with UUID IDs only.");
        return "codex://threads/" + Uri.EscapeDataString(id);
    }

    public static OverlayBounds? Bounds(double left, double top, double width, double height, Insets inset)
    {
        double w = width - inset.Left - inset.Right, h = height - inset.Top - inset.Bottom;
        if (w < 440 || h < 240) return null;
        return new OverlayBounds(left + inset.Left, top + inset.Top, w, h);
    }

    public static MapLayout Arrange(List<MapNode> nodes, HashSet<string> collapsed)
    {
        var index = nodes.ToDictionary(n => n.Id);
        var levels = new Dictionary<string, int>();
        var visiting = new HashSet<string>();
        int Level(string id)
        {
            if (levels.TryGetValue(id, out int l)) return l;
            if (!visiting.Add(id)) throw new InvalidDataException("The conversation graph contains a cycle.");
            var parents = index[id].Parents.Where(index.ContainsKey).ToList();
            int depth = parents.Count == 0 ? 0 : parents.Max(p => Level(p)) + 1;
            visiting.Remove(id); return levels[id] = depth;
        }
        foreach (var n in nodes) Level(n.Id);
        var visible = new HashSet<string>();
        foreach (var n in nodes.OrderBy(n => levels[n.Id]))
            if (n.Parents.Count == 0 || n.Parents.Any(p => visible.Contains(p) && !collapsed.Contains(p))) visible.Add(n.Id);
        var rows = nodes.Where(n => visible.Contains(n.Id)).GroupBy(n => levels[n.Id]).ToList();
        double width = Math.Max(780, rows.Select(r => r.Count() * 255 + 50).DefaultIfEmpty(0).Max());
        var positions = new Dictionary<string, MapPosition>();
        foreach (var row in rows)
        {
            int i = 0, count = row.Count();
            foreach (var n in row) positions[n.Id] = new MapPosition(width / 2 - count * 255 / 2.0 + i++ * 255 + 15, 35 + row.Key * 180);
        }
        return new MapLayout(positions, width, Math.Max(420, (levels.Values.DefaultIfEmpty(0).Max() + 1) * 180 + 35));
    }
}

public class DesktopState
{
    public Dictionary<string, List<string>> Parents { get; set; } = new();
    public Insets Insets { get; set; } = new();
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Branch");
    public static string FilePath => Path.Combine(DirectoryPath, "desktop.json");
    public static DesktopState Load()
    {
        if (!File.Exists(FilePath)) return new();
        // Preserve an unreadable file: never silently overwrite it with defaults.
        return JsonSerializer.Deserialize<DesktopState>(File.ReadAllText(FilePath)) ?? throw new InvalidDataException("Desktop settings are empty.");
    }
    public void Save()
    {
        Directory.CreateDirectory(DirectoryPath);
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, true);
    }
}
