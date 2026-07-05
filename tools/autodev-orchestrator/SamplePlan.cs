namespace AutoDevOrchestrator;

/// <summary>Built-in plan used in dry-run mode so the whole loop can be tested without an OpenAI key.</summary>
public static class SamplePlan
{
    public static PlanDocument Create(DateOnly date) => new()
    {
        PlanDate = date.ToString("yyyy-MM-dd"),
        Theme = "A quiet breeze visits the garden",
        CreativeDirection = "Add one gentle motion so the garden no longer feels frozen in time.",
        Tasks =
        [
            new PlanTask
            {
                Id = $"DPG-{date:yyyyMMdd}-001",
                Title = "Make the plants sway gently in the wind",
                Type = "visual",
                Priority = "high",
                EstimatedSize = "small",
                ImplementationPrompt =
                    "In the PixiJS scene, add a subtle sway animation to existing plant/sprout sprites using the app ticker. " +
                    "A slow sine-based rotation or skew of a few degrees is enough. No external assets, keep it performant.",
                AcceptanceCriteria =
                [
                    "App builds successfully",
                    "Plants visibly sway in a slow, calm rhythm",
                    "Frame rate remains smooth",
                    "No console errors"
                ]
            },
            new PlanTask
            {
                Id = $"DPG-{date:yyyyMMdd}-002",
                Title = "Add one drifting leaf particle",
                Type = "visual",
                Priority = "medium",
                EstimatedSize = "small",
                ImplementationPrompt =
                    "Occasionally spawn a single small pixel leaf that drifts across the scene and despawns off-screen. " +
                    "Draw it with PIXI.Graphics rectangles; at most one leaf on screen at a time.",
                AcceptanceCriteria =
                [
                    "App builds successfully",
                    "A leaf occasionally drifts across the scene",
                    "Leaf is removed once off-screen (no memory growth)",
                    "No console errors"
                ]
            }
        ],
        StoryLog = "A quiet breeze passed through today. The sprouts learned to dance, just a little.",
        FutureIdeas =
        [
            "A snail that crosses the garden once per day",
            "Wind strength that changes with real weather seasons"
        ],
        Constraints =
        [
            "Keep the change small",
            "Do not add backend",
            "Do not add paid assets"
        ]
    };
}
