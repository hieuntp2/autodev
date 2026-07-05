using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AutoDevOrchestrator;

/// <summary>
/// Minimal OpenAI Chat Completions client. One call per planning cycle, JSON-mode output,
/// capped output tokens. OpenAI never sees or edits source code — only reports and logs.
/// </summary>
public static class OpenAiPlanner
{
    public static async Task<PlanDocument> RequestPlanAsync(
        RepoPaths paths, OrchestratorConfig config, string systemPrompt, string userContext)
    {
        if (string.IsNullOrWhiteSpace(config.OpenAiApiKey))
        {
            throw new OrchestratorException(
                "OPENAI_API_KEY is not set. Add it to the .env file at the repo root (see .env.example), " +
                "or set AUTODEV_DRY_RUN=true to generate a sample plan without calling OpenAI.");
        }

        var useWeekly = config.EnableWeeklyReview && DateTime.Now.DayOfWeek == DayOfWeek.Monday;
        var model = useWeekly ? config.WeeklyModel : config.DailyModel;
        Console.WriteLine($"Calling OpenAI planner (model: {model}{(useWeekly ? ", weekly review" : "")}, max output tokens: {config.MaxOutputTokens})...");

        var content = await SendChatRequestAsync(config, model, systemPrompt, userContext);
        var plan = ParsePlan(content);
        Normalize(plan);
        return plan;
    }

    private static async Task<string> SendChatRequestAsync(
        OrchestratorConfig config, string model, string systemPrompt, string userContext)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.OpenAiApiKey);

        // Newer models expect max_completion_tokens; fall back to max_tokens for older ones.
        foreach (var tokenParam in new[] { "max_completion_tokens", "max_tokens" })
        {
            var body = new Dictionary<string, object?>
            {
                ["model"] = model,
                [tokenParam] = config.MaxOutputTokens,
                ["response_format"] = new Dictionary<string, object?> { ["type"] = "json_object" },
                ["messages"] = new object[]
                {
                    new Dictionary<string, object?> { ["role"] = "system", ["content"] = systemPrompt },
                    new Dictionary<string, object?> { ["role"] = "user", ["content"] = userContext }
                }
            };

            HttpResponseMessage response;
            string responseText;
            try
            {
                var request = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                response = await http.PostAsync($"{config.OpenAiBaseUrl}/chat/completions", request);
                responseText = await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new OrchestratorException($"OpenAI request failed: {ex.Message}");
            }

            if (!response.IsSuccessStatusCode)
            {
                if (tokenParam == "max_completion_tokens" && responseText.Contains("max_completion_tokens"))
                    continue; // retry once with the legacy parameter name
                throw new OrchestratorException(
                    $"OpenAI returned {(int)response.StatusCode} {response.StatusCode}: {Truncate(responseText, 600)}");
            }

            using var doc = JsonDocument.Parse(responseText);
            LogUsage(doc.RootElement);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
            if (string.IsNullOrWhiteSpace(content))
                throw new OrchestratorException("OpenAI returned an empty message (output token cap may be too low).");
            return content;
        }

        throw new OrchestratorException("OpenAI rejected both max_completion_tokens and max_tokens parameters.");
    }

    private static void LogUsage(JsonElement root)
    {
        if (root.TryGetProperty("usage", out var usage) &&
            usage.TryGetProperty("prompt_tokens", out var prompt) &&
            usage.TryGetProperty("completion_tokens", out var completion))
        {
            Console.WriteLine($"OpenAI usage: {prompt.GetInt32()} prompt + {completion.GetInt32()} completion tokens.");
        }
    }

    private static PlanDocument ParsePlan(string content)
    {
        var json = content.Trim();
        // Defensive: strip markdown fences if the model ignored JSON mode.
        if (json.StartsWith("```"))
        {
            var firstNewline = json.IndexOf('\n');
            var lastFence = json.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
                json = json[(firstNewline + 1)..lastFence].Trim();
        }

        try
        {
            var plan = JsonSerializer.Deserialize<PlanDocument>(json, PlanStore.JsonOptions);
            if (plan is null || plan.Tasks.Count == 0)
                throw new OrchestratorException("OpenAI returned a plan with no tasks.");
            return plan;
        }
        catch (JsonException ex)
        {
            throw new OrchestratorException(
                $"Could not parse OpenAI response as a plan ({ex.Message}). Raw response starts with: {Truncate(json, 300)}");
        }
    }

    private static void Normalize(PlanDocument plan)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (string.IsNullOrWhiteSpace(plan.PlanDate)) plan.PlanDate = today.ToString("yyyy-MM-dd");
        var index = 1;
        foreach (var task in plan.Tasks)
        {
            if (string.IsNullOrWhiteSpace(task.Id)) task.Id = $"DPG-{today:yyyyMMdd}-{index:000}";
            if (string.IsNullOrWhiteSpace(task.Status)) task.Status = "pending";
            task.CompletedAt = task.IsPending ? null : task.CompletedAt;
            index++;
        }
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "...";
}
