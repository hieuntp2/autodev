using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace AutoDev.OpenAI;

public sealed class OpenAIPlannerClient(HttpClient httpClient)
{
    private const string ResponsesEndpoint = "https://api.openai.com/v1/responses";

    public async Task<string> CreatePlanAsync(PlannerRequest request, CancellationToken cancellationToken = default)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("OPENAI_API_KEY is not set.");
        }

        var model = Environment.GetEnvironmentVariable("OPENAI_PLANNER_MODEL");
        if (string.IsNullOrWhiteSpace(model))
        {
            model = "gpt-4.1";
        }

        var body = new JsonObject
        {
            ["model"] = model,
            ["input"] = request.Prompt
        };

        var vectorStoreIds = ResolveVectorStoreIds(request.Project.PlannerVectorStoreIds);
        if (vectorStoreIds.Count > 0)
        {
            body["tools"] = new JsonArray(
                new JsonObject
                {
                    ["type"] = "file_search",
                    ["vector_store_ids"] = new JsonArray(vectorStoreIds.Select(id => JsonValue.Create(id)).ToArray<JsonNode?>())
                });
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, ResponsesEndpoint);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        message.Content = new StringContent(
            body.ToJsonString(),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(message, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(request.WorkspacePath, "01-planning", "planner-raw-response.json"),
            raw,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI planner call failed with HTTP {(int)response.StatusCode}: {raw}");
        }

        var text = ExtractText(raw);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("OpenAI planner response did not contain text output.");
        }

        return text.Trim();
    }

    /// <summary>
    /// Knowledge base vector stores attached to every planner call via the file_search tool.
    /// Project config (plannerVectorStoreIds) wins; OPENAI_VECTOR_STORE_IDS (comma-separated) is the fallback.
    /// </summary>
    private static IReadOnlyList<string> ResolveVectorStoreIds(IReadOnlyList<string> configuredIds)
    {
        var ids = configuredIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToList();

        if (ids.Count > 0)
        {
            return ids;
        }

        var fromEnvironment = Environment.GetEnvironmentVariable("OPENAI_VECTOR_STORE_IDS");
        return string.IsNullOrWhiteSpace(fromEnvironment)
            ? []
            : fromEnvironment
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();
    }

    private static string ExtractText(string rawJson)
    {
        var node = JsonNode.Parse(rawJson);
        var outputText = node?["output_text"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(outputText))
        {
            return outputText;
        }

        var builder = new StringBuilder();
        foreach (var output in node?["output"]?.AsArray() ?? [])
        {
            foreach (var content in output?["content"]?.AsArray() ?? [])
            {
                var text = content?["text"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    builder.AppendLine(text);
                }
            }
        }

        return builder.ToString();
    }
}
