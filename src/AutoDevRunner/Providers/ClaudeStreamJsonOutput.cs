using System.Text.Json;

namespace AutoDevRunner.Providers;

public enum ClaudeTerminalKind
{
    None,
    Completed,
    Failed
}

public sealed record ClaudeStreamJsonResult(
    bool IsStreamJson,
    ClaudeTerminalKind TerminalKind,
    string? FinalMessage,
    string? SessionId,
    int? InputTokens,
    int? OutputTokens,
    decimal? CostUsd,
    string? Model,
    string? Error);

/// <summary>
/// Parses the JSONL protocol produced by
/// <c>claude -p --output-format stream-json --verbose</c>. Streaming output is
/// what lets the runner show live progress, feed the idle-timeout watchdog
/// (the old single-envelope <c>--output-format json</c> printed NOTHING until
/// the very end, so any Claude run longer than the idle timeout was killed as
/// "stalled"), and detect the terminal <c>result</c> event.
/// </summary>
public static class ClaudeStreamJsonOutput
{
    public static ClaudeStreamJsonResult Parse(string output)
    {
        var isStream = false;
        var terminal = ClaudeTerminalKind.None;
        string? final = null;
        string? sessionId = null;
        string? model = null;
        string? error = null;
        decimal? cost = null;
        int? inputTokens = null;
        int? outputTokens = null;

        foreach (var raw in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.TrimEnd('\r');
            if (!TryParse(line, out var doc)) continue;
            using (doc)
            {
                var root = doc.RootElement;
                var type = String(root, "type");
                switch (type)
                {
                    case "system":
                        // {"type":"system","subtype":"init","session_id":...,"model":...}
                        // (other system subtypes exist, e.g. thinking_tokens ticks)
                        isStream = true;
                        sessionId = String(root, "session_id") ?? sessionId;
                        model ??= String(root, "model");
                        break;
                    case "assistant":
                    case "user":
                        isStream = true;
                        sessionId ??= String(root, "session_id");
                        break;
                    case "result":
                        isStream = true;
                        var isError = root.TryGetProperty("is_error", out var flag)
                                      && flag.ValueKind == JsonValueKind.True;
                        terminal = isError ? ClaudeTerminalKind.Failed : ClaudeTerminalKind.Completed;
                        final = String(root, "result") ?? final;
                        if (isError)
                            error = String(root, "result")
                                    ?? $"Claude run failed ({String(root, "subtype") ?? "unknown"}).";
                        sessionId = String(root, "session_id") ?? sessionId;
                        cost = Decimal(root, "total_cost_usd") ?? cost;
                        if (root.TryGetProperty("usage", out var usage)
                            && usage.ValueKind == JsonValueKind.Object)
                        {
                            inputTokens = SumTokens(
                                Int(usage, "input_tokens"),
                                Int(usage, "cache_creation_input_tokens"),
                                Int(usage, "cache_read_input_tokens"));
                            outputTokens = Int(usage, "output_tokens");
                        }
                        break;
                }
            }
        }

        return new ClaudeStreamJsonResult(
            isStream, terminal, final, sessionId, inputTokens, outputTokens, cost, model, error);
    }

    public static ProcessTerminalSignal? DetectTerminal(string line)
    {
        if (!TryParse(line, out var doc)) return null;
        using (doc)
        {
            var root = doc.RootElement;
            if (String(root, "type") != "result") return null;

            var isError = root.TryGetProperty("is_error", out var flag)
                          && flag.ValueKind == JsonValueKind.True;
            return isError
                ? new ProcessTerminalSignal(ProcessTerminalKind.Failed,
                    String(root, "result") ?? $"Claude run failed ({String(root, "subtype") ?? "unknown"}).")
                : new ProcessTerminalSignal(ProcessTerminalKind.Completed);
        }
    }

    public static string? FormatLiveLine(string line)
    {
        if (!TryParse(line, out var doc)) return line;
        using (doc)
        {
            // Runs inside the process-output callback (same hazard as the Codex
            // parser): never let a surprising event shape take the run down.
            try
            {
                return FormatLive(doc.RootElement);
            }
            catch (Exception)
            {
                return line;
            }
        }
    }

    private static string? FormatLive(JsonElement root)
    {
        var type = String(root, "type");
        if (type == "system")
        {
            // Only the init event is worth a line; thinking_tokens ticks and
            // rate_limit events would flood the live view (they still count
            // as output for the idle timeout, which is what matters).
            if (String(root, "subtype") != "init") return null;
            var model = String(root, "model");
            return $"[session] {String(root, "session_id")}{(model is null ? "" : $" ({model})")}";
        }
        if (type == "result")
        {
            var isError = root.TryGetProperty("is_error", out var flag)
                          && flag.ValueKind == JsonValueKind.True;
            if (isError)
                return "[error] " + (String(root, "result") ?? "Claude run failed.");
            var usage = root.TryGetProperty("usage", out var u) ? u : default;
            return $"[complete] result (in {Int(usage, "input_tokens") ?? 0:N0} / out {Int(usage, "output_tokens") ?? 0:N0})";
        }
        if (type != "assistant") return null; // tool results ("user" events) are noise
        if (!root.TryGetProperty("message", out var message)
            || message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var block in content.EnumerateArray())
        {
            var blockType = String(block, "type");
            if (blockType == "text")
            {
                var text = String(block, "text");
                if (!string.IsNullOrWhiteSpace(text)) parts.Add("[agent] " + text);
            }
            else if (blockType == "tool_use")
            {
                var name = String(block, "name") ?? "tool";
                var detail = ToolDetail(block);
                parts.Add($"[tool] {name}{(detail is null ? "" : $": {detail}")}");
            }
        }
        return parts.Count == 0 ? null : string.Join('\n', parts);
    }

    /// <summary>Best-effort one-line summary of a tool call's target.</summary>
    private static string? ToolDetail(JsonElement block)
    {
        if (!block.TryGetProperty("input", out var input)
            || input.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var detail = String(input, "command")
                     ?? String(input, "file_path")
                     ?? String(input, "path")
                     ?? String(input, "pattern")
                     ?? String(input, "description");
        if (detail is null) return null;
        detail = detail.ReplaceLineEndings(" ");
        return detail.Length <= 200 ? detail : detail[..200] + "…";
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

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // ValueKind checks are load-bearing (see CodexJsonOutput): TryGetInt32/
    // TryGetDecimal THROW when the element is not a Number (e.g. null).
    private static int? Int(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
            ? number
            : null;

    private static decimal? Decimal(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDecimal(out var number)
            ? number
            : null;

    private static int? SumTokens(params int?[] values)
    {
        var total = 0;
        var any = false;
        foreach (var value in values)
        {
            if (value is null) continue;
            total += value.Value;
            any = true;
        }
        return any ? total : null;
    }
}
