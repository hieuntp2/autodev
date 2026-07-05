using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoDevOrchestrator;

public sealed class PlanDocument
{
    [JsonPropertyName("plan_date")] public string PlanDate { get; set; } = "";
    [JsonPropertyName("theme")] public string Theme { get; set; } = "";
    [JsonPropertyName("creative_direction")] public string CreativeDirection { get; set; } = "";
    [JsonPropertyName("tasks")] public List<PlanTask> Tasks { get; set; } = [];
    [JsonPropertyName("story_log")] public string? StoryLog { get; set; }
    [JsonPropertyName("future_ideas")] public List<string> FutureIdeas { get; set; } = [];
    [JsonPropertyName("constraints")] public List<string> Constraints { get; set; } = [];

    [JsonIgnore] public IEnumerable<PlanTask> PendingTasks => Tasks.Where(t => t.IsPending);
    [JsonIgnore] public bool IsCompleted => Tasks.Count == 0 || !PendingTasks.Any();
}

public sealed class PlanTask
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "feature";
    [JsonPropertyName("priority")] public string Priority { get; set; } = "medium";
    [JsonPropertyName("estimated_size")] public string EstimatedSize { get; set; } = "small";
    [JsonPropertyName("implementation_prompt")] public string ImplementationPrompt { get; set; } = "";
    [JsonPropertyName("acceptance_criteria")] public List<string> AcceptanceCriteria { get; set; } = [];
    [JsonPropertyName("status")] public string Status { get; set; } = "pending";
    [JsonPropertyName("completed_at")] public string? CompletedAt { get; set; }

    [JsonIgnore]
    public bool IsPending =>
        Status.Equals("pending", StringComparison.OrdinalIgnoreCase) ||
        Status.Equals("in_progress", StringComparison.OrdinalIgnoreCase);
}

public static class PlanStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    public static PlanDocument? Load(RepoPaths paths)
    {
        if (!File.Exists(paths.CurrentPlanFile)) return null;
        try
        {
            var plan = JsonSerializer.Deserialize<PlanDocument>(File.ReadAllText(paths.CurrentPlanFile), JsonOptions);
            return plan;
        }
        catch (JsonException ex)
        {
            throw new OrchestratorException(
                $"CURRENT_PLAN.json is not valid JSON ({ex.Message}). Fix or delete {paths.CurrentPlanFile}.");
        }
    }

    public static void Save(PlanDocument plan, string filePath)
    {
        File.WriteAllText(filePath, JsonSerializer.Serialize(plan, JsonOptions) + Environment.NewLine);
    }
}
