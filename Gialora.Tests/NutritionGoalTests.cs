// Gialora.Tests/NutritionGoalTests.cs
using Gialora.Application.Planning;
using Gialora.Shared.Enums;
using Gialora.Shared.Goals;

namespace Gialora.Tests;

/// <summary>
/// Family goals ("more iron", "picky eater") used to be stored and displayed but
/// never reached the planner, so choosing one changed nothing. These tests pin
/// the behaviour down.
/// </summary>
public class NutritionGoalTests
{
    private readonly MealPlanningEngine _engine = new();

    private static PlanningCandidate Recipe(
        string title,
        bool ironRich = false,
        bool highProtein = false,
        bool kidFriendly = false,
        int vegetables = 0,
        int minutes = 40,
        decimal? cost = null) => new()
        {
            RecipeId = Guid.NewGuid(),
            Title = title,
            PrimaryProtein = ProteinType.None,
            TotalMinutes = minutes,
            IsIronRich = ironRich,
            IsHighProtein = highProtein,
            IsKidFriendly = kidFriendly,
            VegetableCount = vegetables,
            CostPerServing = cost
        };

    private static PlanningRequest Request(params string[] goalKeys) => Request(1, goalKeys);

    private static PlanningRequest Request(int slots, params string[] goalKeys) => new()
    {
        Constraints = new PlanningConstraints
        {
            MaxCookingTimeMinutes = 120,
            Goals = goalKeys.ToHashSet(StringComparer.OrdinalIgnoreCase)
        },
        SlotCount = slots,
        Seed = 4242
    };

    [Fact]
    public void Free_text_is_mapped_to_a_known_goal()
    {
        var resolved = NutritionGoals.Resolve(new[] { "more iron", "picky eater" });

        Assert.Equal(new[] { "more-iron", "picky-eater" }, resolved.Select(g => g.Key));
    }

    [Fact]
    public void Text_that_means_nothing_to_the_planner_is_reported_not_silently_dropped()
    {
        var unknown = NutritionGoals.Unrecognised(new[] { "more iron", "more zinc" });

        Assert.Equal(new[] { "more zinc" }, unknown);
    }

    [Fact]
    public void Every_goal_offered_to_users_has_a_matcher()
    {
        // Guards against adding a goal to the UI list that the engine ignores.
        foreach (var goal in NutritionGoals.All)
        {
            var matching = Recipe("x", ironRich: true, highProtein: true, kidFriendly: true,
                vegetables: 5, minutes: 10, cost: 1m);

            Assert.True(GoalMatchers.Matches(goal.Key, matching),
                $"Goal '{goal.Key}' is offered but nothing satisfies it.");
        }
    }

    [Fact]
    public void More_iron_prefers_an_iron_rich_recipe()
    {
        var plain = Recipe("Plain pasta");
        var ironRich = Recipe("Lentil and spinach stew", ironRich: true);

        var withoutGoal = _engine.Plan(new[] { plain, ironRich }, Request());
        var withGoal = _engine.Plan(new[] { plain, ironRich }, Request("more-iron"));

        // Same seed and pool: only the goal differs.
        Assert.Equal("Lentil and spinach stew", withGoal.Selection[0].Title);
        Assert.Equal("Plain pasta", withoutGoal.Selection[0].Title);
    }

    [Fact]
    public void Picky_eater_prefers_a_kid_friendly_recipe()
    {
        var plain = Recipe("Spiced fish stew");
        var kidFriendly = Recipe("Chicken meatballs", kidFriendly: true);

        var result = _engine.Plan(new[] { plain, kidFriendly }, Request("picky-eater"));

        Assert.Equal("Chicken meatballs", result.Selection[0].Title);
    }

    [Fact]
    public void Two_goals_beat_one()
    {
        var onlyIron = Recipe("Iron only", ironRich: true);
        var both = Recipe("Iron and kid friendly", ironRich: true, kidFriendly: true);

        var result = _engine.Plan(new[] { onlyIron, both }, Request("more-iron", "picky-eater"));

        Assert.Equal("Iron and kid friendly", result.Selection[0].Title);
    }

    [Fact]
    public void A_goal_is_a_preference_not_a_filter()
    {
        // Nothing matches the goal, but the plan must still be produced.
        var plain = Recipe("Plain pasta");

        var result = _engine.Plan(new[] { plain }, Request("more-iron"));

        Assert.Single(result.Selection);
        Assert.Contains(result.Notes, n => n.Contains("More iron") && n.Contains("no matching recipes"));
    }

    [Fact]
    public void The_plan_reports_how_many_meals_met_the_goal()
    {
        var pool = new[]
        {
            Recipe("Iron A", ironRich: true),
            Recipe("Iron B", ironRich: true),
            Recipe("Plain", ironRich: false)
        };

        var result = _engine.Plan(pool, Request(slots: 3, "more-iron"));

        Assert.Contains(result.Notes, n => n.Contains("Goal \"More iron\": 2 of 3 meals match."));
    }
}
