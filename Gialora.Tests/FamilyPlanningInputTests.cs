// Gialora.Tests/FamilyPlanningInputTests.cs
using Gialora.Application.Planning;
using Gialora.Data.Entities;
using Gialora.Shared.Enums;

namespace Gialora.Tests;

/// <summary>
/// A family member can be switched off without being deleted. While inactive,
/// nothing about them (allergies, restrictions, goals, age, portion) should
/// reach the planner or the shopping list.
/// </summary>
public class FamilyPlanningInputTests
{
    private static FamilyMember Member(
        string name,
        bool active = true,
        FamilyMemberType type = FamilyMemberType.Adult,
        int? age = null,
        string[]? allergies = null,
        string[]? restrictions = null,
        string[]? goals = null,
        bool deleted = false) => new()
        {
            Name = name,
            IsActive = active,
            IsDeleted = deleted,
            MemberType = type,
            Age = age,
            Allergies = (allergies ?? Array.Empty<string>()).ToList(),
            DietaryRestrictions = (restrictions ?? Array.Empty<string>()).ToList(),
            Goals = (goals ?? Array.Empty<string>()).ToList()
        };

    private static Family FamilyOf(params FamilyMember[] members) => new()
    {
        Name = "Test family",
        ServingsPerMeal = 4,
        FamilyMembers = members.ToList()
    };

    [Fact]
    public void Inactive_member_allergies_and_restrictions_are_ignored()
    {
        var family = FamilyOf(
            Member("Ana", allergies: new[] { "dairy" }),
            Member("Ben", active: false, allergies: new[] { "nuts" }, restrictions: new[] { "gluten" }));

        var constraints = FamilyPlanningInput.BuildConstraints(family);

        Assert.Contains("dairy", constraints.Allergies);
        Assert.DoesNotContain("nuts", constraints.Allergies);
        Assert.DoesNotContain("gluten", constraints.Allergies);
    }

    [Fact]
    public void Inactive_member_goals_are_ignored()
    {
        var family = FamilyOf(
            Member("Ana", goals: new[] { "more iron" }),
            Member("Ben", active: false, goals: new[] { "picky eater" }));

        var constraints = FamilyPlanningInput.BuildConstraints(family);

        Assert.Contains("more-iron", constraints.Goals);
        Assert.DoesNotContain("picky-eater", constraints.Goals);
    }

    [Fact]
    public void Inactive_child_does_not_set_age_limit_or_children_flag()
    {
        var family = FamilyOf(
            Member("Ana"),
            Member("Kid", active: false, type: FamilyMemberType.Child, age: 1));

        var constraints = FamilyPlanningInput.BuildConstraints(family);

        Assert.Null(constraints.YoungestAgeMonths);
        Assert.False(constraints.HasChildren);
    }

    [Fact]
    public void Active_child_still_sets_age_limit()
    {
        var family = FamilyOf(
            Member("Ana"),
            Member("Kid", type: FamilyMemberType.Child, age: 2),
            Member("Baby", active: false, type: FamilyMemberType.Child, age: 0));

        var constraints = FamilyPlanningInput.BuildConstraints(family);

        // The youngest ACTIVE child (2y = 24 months) sets the limit, not the inactive baby.
        Assert.Equal(24, constraints.YoungestAgeMonths);
        Assert.True(constraints.HasChildren);
    }

    [Fact]
    public void Reactivating_a_member_brings_their_rules_back()
    {
        var ben = Member("Ben", active: false, allergies: new[] { "fish" });
        var family = FamilyOf(Member("Ana"), ben);

        Assert.DoesNotContain("fish", FamilyPlanningInput.BuildConstraints(family).Allergies);

        ben.IsActive = true;

        Assert.Contains("fish", FamilyPlanningInput.BuildConstraints(family).Allergies);
    }

    [Fact]
    public void Default_servings_count_only_active_members()
    {
        var family = FamilyOf(
            Member("Ana"),
            Member("Ben"),
            Member("Kid", type: FamilyMemberType.Child, age: 5),
            Member("Away", active: false));

        // 4 members, 3 eating → 3 servings, so the shopping list shrinks with them.
        Assert.Equal(3, FamilyPlanningInput.DefaultServings(family));
    }

    [Fact]
    public void Default_servings_fall_back_to_preference_when_no_one_is_active()
    {
        var empty = FamilyOf();
        var allAway = FamilyOf(Member("Ana", active: false));

        Assert.Equal(4, FamilyPlanningInput.DefaultServings(empty));
        Assert.Equal(4, FamilyPlanningInput.DefaultServings(allAway));
    }

    [Fact]
    public void Soft_deleted_members_are_never_counted_even_if_active()
    {
        var family = FamilyOf(
            Member("Ana"),
            Member("Gone", deleted: true, allergies: new[] { "soy" }));

        Assert.Equal(1, FamilyPlanningInput.DefaultServings(family));
        Assert.DoesNotContain("soy", FamilyPlanningInput.BuildConstraints(family).Allergies);
    }
}
