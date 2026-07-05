using System.Text;
using System.Text.Json.Nodes;
using AutoDevRunner.Config;
using AutoDevRunner.Models;
using Microsoft.Extensions.Options;

namespace AutoDevRunner.Services;

/// <summary>
/// Optional pre-step that asks the OpenAI Responses API for a bold, creative
/// daily plan, grounded in a knowledge-base vector store via the file_search
/// tool. The plan is injected into the CLI prompt so the autonomous implementer
/// builds against it.
///
/// Entirely fail-soft: if disabled, unconfigured, or the call fails, it returns
/// null and the run continues with the base prompt. It never throws.
/// </summary>
public class OpenAiCreativePlanner
{
    private readonly PlannerOptions _opt;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<OpenAiCreativePlanner> _log;

    public OpenAiCreativePlanner(
        IOptions<AutoDevOptions> opt, IHttpClientFactory httpFactory, ILogger<OpenAiCreativePlanner> log)
    {
        _opt = opt.Value.Planner;
        _httpFactory = httpFactory;
        _log = log;
    }

    /// <summary>
    /// Returns a creative plan as Markdown, or null when planning is skipped or
    /// fails. Never throws — the run must not depend on this step succeeding.
    /// </summary>
    public async Task<string?> CreatePlanAsync(Project project, string brief, CancellationToken ct = default)
    {
        if (!_opt.Enabled)
            return null;

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _log.LogWarning("Creative planner is enabled but no API key is configured (Planner:ApiKey or OPENAI_API_KEY); skipping creative planning.");
            return null;
        }

        try
        {
            var body = BuildRequestBody(project, brief);

            var http = _httpFactory.CreateClient();
            using var message = new HttpRequestMessage(HttpMethod.Post, _opt.ApiUrl)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };
            message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await http.SendAsync(message, ct);
            var raw = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogError("Creative planner call failed: {Status} {Detail}", (int)response.StatusCode, Truncate(raw, 600));
                return null;
            }

            var text = ExtractText(raw);
            if (string.IsNullOrWhiteSpace(text))
            {
                _log.LogWarning("Creative planner returned an empty plan; continuing with the base prompt.");
                return null;
            }

            _log.LogInformation("Creative planner produced a plan for {Project} ({Chars} chars).", project.Name, text.Length);
            return text.Trim();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Creative planner failed; continuing with the base prompt.");
            return null;
        }
    }

    private string ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(_opt.ApiKey))
            return _opt.ApiKey.Trim();

        return Environment.GetEnvironmentVariable("OPENAI_API_KEY")?.Trim() ?? string.Empty;
    }

    private JsonObject BuildRequestBody(Project project, string brief)
    {
        var instructions = new StringBuilder();
        instructions.AppendLine("You are the Creative Director for an autonomous software project.");
        instructions.AppendLine("Design the boldest, most delightful, highest-impact plan for the NEXT development run.");
        instructions.AppendLine("You have COMPLETE creative freedom and full autonomy: decide boldly, take creative risks,");
        instructions.AppendLine("and go beyond the literal ask when it makes the product better. Nothing needs prior approval.");
        instructions.AppendLine("Search the attached knowledge base freely for product vision, design language, and inspiration,");
        instructions.AppendLine("and let it fuel — not limit — your creativity.");
        instructions.AppendLine();
        instructions.AppendLine("Output concise Markdown a coding agent can execute this run, with sections:");
        instructions.AppendLine("## Creative Direction — the vision/theme for this run");
        instructions.AppendLine("## This Run's Task — one coherent, buildable slice");
        instructions.AppendLine("## Concrete Steps — ordered, specific");
        instructions.AppendLine("## Delight & Polish — small touches that make it shine");
        instructions.AppendLine("## Acceptance — how we know it's done");
        instructions.AppendLine();
        instructions.AppendLine("## Project");
        instructions.AppendLine($"- Name: {project.Name}");
        if (!string.IsNullOrWhiteSpace(project.Notes))
            instructions.AppendLine($"- Notes: {project.Notes}");
        if (!string.IsNullOrWhiteSpace(project.CurrentTask))
            instructions.AppendLine($"- Task in progress from last run: {project.CurrentTask}");
        if (!string.IsNullOrWhiteSpace(project.LastSummary))
        {
            instructions.AppendLine("- Previous run summary:");
            instructions.AppendLine(project.LastSummary!.Trim());
        }
        instructions.AppendLine();
        instructions.AppendLine("## Project brief");
        instructions.AppendLine(string.IsNullOrWhiteSpace(brief)
            ? "(No brief provided. Infer the goal from the knowledge base and the project name.)"
            : brief.Trim());

        var body = new JsonObject
        {
            ["model"] = _opt.Model,
            ["input"] = instructions.ToString()
        };

        var vectorStoreIds = _opt.VectorStoreIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToArray();

        if (vectorStoreIds.Length > 0)
        {
            body["tools"] = new JsonArray(
                new JsonObject
                {
                    ["type"] = "file_search",
                    ["vector_store_ids"] = new JsonArray(vectorStoreIds.Select(id => JsonValue.Create(id)).ToArray<JsonNode?>())
                });
        }

        return body;
    }

    private static string ExtractText(string rawJson)
    {
        var node = JsonNode.Parse(rawJson);

        var outputText = node?["output_text"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(outputText))
            return outputText;

        var builder = new StringBuilder();
        foreach (var output in node?["output"]?.AsArray() ?? new JsonArray())
        {
            foreach (var content in output?["content"]?.AsArray() ?? new JsonArray())
            {
                var text = content?["text"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(text))
                    builder.AppendLine(text);
            }
        }

        return builder.ToString();
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "...";
}
