namespace Gialora.Data.Entities;

public class Family : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public ICollection<User> Members { get; set; } = new List<User>();
    public ICollection<FamilyMember> FamilyMembers { get; set; } = new List<FamilyMember>();
    public ICollection<MealPlan> MealPlans { get; set; } = new List<MealPlan>();
}