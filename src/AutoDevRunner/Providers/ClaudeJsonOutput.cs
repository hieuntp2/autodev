using System.Text.Json;

namespace AutoDevRunner.Providers;

public sealed record ClaudeJsonOutputResult(
    bool IsJsonEnvelope,
    string Text,
    string? SessionId,
    int? InputTokens,
    int? OutputTokens,
    decimal? CostUsd,
    string? Model);

/// <summary>Fail-soft parser for `claude -p --output-format json` envelopes.</summary>
public static class ClaudeJsonOutput
{
    public static ClaudeJsonOutputResult Parse(string output)
    {
        try
        {
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;
            if (root.ValueKind is not JsonValueKind.Object)
                return Plain(output);

            if (!TryGetString(root, "type", out var type)
                || !string.Equals(type, "result", StringComparison.OrdinalIgnoreCase))
            {
                return Plain(output);
            }

            var text = TryGetString(root, "result", out var result)
                ? result!
                : output;
            var sessionId = TryGetString(root, "session_id", out var id) ? id : null;
            var model = TryGetString(root, "model", out var modelValue) ? modelValue : null;
            var cost = TryGetDecimal(root, "total_cost_usd");

            int? inputTokens = null;
            int? outputTokens = null;
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind is JsonValueKind.Object)
            {
                inputTokens = SumTokens(
                    TryGetInt(usage, "input_tokens"),
                    TryGetInt(usage, "cache_creation_input_tokens"),
                    TryGetInt(usage, "cache_read_input_tokens"));
                outputTokens = TryGetInt(usage, "output_tokens");
            }

            return new ClaudeJsonOutputResult(
                IsJsonEnvelope: true,
                Text: text,
                SessionId: sessionId,
                InputTokens: inputTokens,
                OutputTokens: outputTokens,
                CostUsd: cost,
                Model: model);
        }
        catch (JsonException)
        {
            return Plain(output);
        }
    }

    private static ClaudeJsonOutputResult Plain(string output) =>
        new(false, output, null, null, null, null, null);

    private static bool TryGetString(JsonElement root, string name, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out var prop) || prop.ValueKind is not JsonValueKind.String)
            return false;

        value = prop.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static int? TryGetInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var prop)) return null;
        return prop.ValueKind is JsonValueKind.Number && prop.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static decimal? TryGetDecimal(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var prop)) return null;
        return prop.ValueKind is JsonValueKind.Number && prop.TryGetDecimal(out var value)
            ? value
            : null;
    }

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
