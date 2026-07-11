using System.Text.Json;

namespace AutoDevRunner.Providers;

public enum CodexTerminalKind
{
    None,
    Completed,
    Failed
}

public sealed record CodexJsonResult(
    bool IsJsonStream,
    CodexTerminalKind TerminalKind,
    string? FinalMessage,
    string? SessionId,
    int? InputTokens,
    int? OutputTokens,
    string? Error,
    IReadOnlyList<string> LiveLines);

/// <summary>Parses the JSONL protocol produced by <c>codex exec --json</c>.</summary>
public static class CodexJsonOutput
{
    public static CodexJsonResult Parse(string output)
    {
        var isJson = false;
        var terminal = CodexTerminalKind.None;
        string? final = null;
        string? sessionId = null;
        string? error = null;
        int? inputTokens = null;
        int? outputTokens = null;
        var live = new List<string>();

        foreach (var raw in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.TrimEnd('\r');
            if (!TryParse(line, out var doc)) continue;
            using (doc)
            {
                isJson = true;
                var root = doc.RootElement;
                var type = String(root, "type");
                switch (type)
                {
                    case "thread.started":
                        sessionId = String(root, "thread_id") ?? sessionId;
                        break;
                    case "item.completed":
                        if (root.TryGetProperty("item", out var item)
                            && String(item, "type") == "agent_message")
                        {
                            final = String(item, "text") ?? final;
                        }
                        break;
                    case "turn.completed":
                        terminal = CodexTerminalKind.Completed;
                        if (root.TryGetProperty("usage", out var usage))
                        {
                            inputTokens = Int(usage, "input_tokens");
                            outputTokens = Int(usage, "output_tokens");
                        }
                        break;
                    case "turn.failed":
                    case "error":
                        terminal = CodexTerminalKind.Failed;
                        error = ErrorMessage(root) ?? error ?? "Codex turn failed.";
                        break;
                }

                var display = FormatLive(root);
                if (!string.IsNullOrWhiteSpace(display)) live.Add(display);
            }
        }

        return new CodexJsonResult(
            isJson, terminal, final, sessionId, inputTokens, outputTokens, error, live);
    }

    public static ProcessTerminalSignal? DetectTerminal(string line)
    {
        if (!TryParse(line, out var doc)) return null;
        using (doc)
        {
            var root = doc.RootElement;
            return String(root, "type") switch
            {
                "turn.completed" => new ProcessTerminalSignal(ProcessTerminalKind.Completed),
                "turn.failed" or "error" => new ProcessTerminalSignal(
                    ProcessTerminalKind.Failed, ErrorMessage(root) ?? "Codex turn failed."),
                _ => null
            };
        }
    }

    public static string? FormatLiveLine(string line)
    {
        if (!TryParse(line, out var doc)) return line;
        using (doc) return FormatLive(doc.RootElement);
    }

    private static string? FormatLive(JsonElement root)
    {
        var type = String(root, "type");
        if (type == "thread.started")
            return $"[session] {String(root, "thread_id")}";
        if (type == "turn.started") return "[phase] Codex turn started";
        if (type == "turn.completed")
        {
            var usage = root.TryGetProperty("usage", out var u) ? u : default;
            return $"[complete] turn completed (in {Int(usage, "input_tokens") ?? 0:N0} / out {Int(usage, "output_tokens") ?? 0:N0})";
        }
        if (type is "turn.failed" or "error")
            return "[error] " + (ErrorMessage(root) ?? "Codex turn failed.");
        if (type is not ("item.started" or "item.completed")) return null;
        if (!root.TryGetProperty("item", out var item)) return null;

        var itemType = String(item, "type");
        if (itemType == "reasoning") return null;
        if (itemType == "agent_message")
            return "[agent] " + (String(item, "text") ?? string.Empty);
        if (itemType == "command_execution")
        {
            var command = String(item, "command") ?? "command";
            var status = String(item, "status") ?? (type == "item.started" ? "started" : "completed");
            var exit = Int(item, "exit_code");
            return exit is null
                ? $"[command] {status}: {command}"
                : $"[command] {status} (exit {exit}): {command}";
        }
        if (itemType == "file_change")
            return "[file] " + (String(item, "path") ?? String(item, "text") ?? "file changed");
        if (itemType == "mcp_tool_call")
            return "[tool] " + (String(item, "server") ?? "MCP") + "/" + (String(item, "tool") ?? "call");
        return null;
    }

    private static bool TryParse(string line, out JsonDocument doc)
    {
        try
        {
            doc = JsonDocument.Parse(line);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("type", out _);
        }
        catch
        {
            doc = null!;
            return false;
        }
    }

    private static string? ErrorMessage(JsonElement root)
    {
        if (root.TryGetProperty("error", out var error))
        {
            if (error.ValueKind == JsonValueKind.String) return error.GetString();
            if (error.ValueKind == JsonValueKind.Object) return String(error, "message");
        }
        return String(root, "message");
    }

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? Int(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.TryGetInt32(out var number)
            ? number
            : null;
}
