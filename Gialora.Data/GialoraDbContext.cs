// Gialora.Data/GialoraDbContext.cs
using Microsoft.EntityFrameworkCore;
using Gialora.Data.Entities;

namespace Gialora.Data;

public class GialoraDbContext : DbContext
{
    public GialoraDbContext(DbContextOptions<GialoraDbContext> options) : base(options) { }


    public DbSet<User> Users => Set<User>();
    public DbSet<Family> Families => Set<Family>();
    public DbSet<MealPlan> MealPlans => Set<MealPlan>();
    public DbSet<MealPlanDay> MealPlanDays => Set<MealPlanDay>();
    public DbSet<MealPlanEntry> MealPlanEntries => Set<MealPlanEntry>();
    public DbSet<FamilyMember> FamilyMembers => Set<FamilyMember>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<RecipeIngredient> RecipeIngredients => Set<RecipeIngredient>();
    public DbSet<RecipeTag> RecipeTags => Set<RecipeTag>();
    public DbSet<Feedback> Feedbacks => Set<Feedback>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Email-ը unique պիտի լինի — կանխում է duplicate account-ներ
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        // Soft-delete filter — ամեն query ինքնաբերաբար բացառում է ջնջվածները
        modelBuilder.Entity<User>().HasQueryFilter(u => !u.IsDeleted);
        modelBuilder.Entity<Family>().HasQueryFilter(f => !f.IsDeleted);
        modelBuilder.Entity<FamilyMember>().HasQueryFilter(fm => !fm.IsDeleted);
        modelBuilder.Entity<MealPlan>().HasQueryFilter(mp => !mp.IsDeleted);
        modelBuilder.Entity<MealPlanDay>().HasQueryFilter(d => !d.IsDeleted);
        modelBuilder.Entity<MealPlanEntry>().HasQueryFilter(e => !e.IsDeleted);

        // DietaryRestrictions/Allergies-ը որպես JSON column
        modelBuilder.Entity<FamilyMember>()
            .Property(fm => fm.DietaryRestrictions)
            .HasConversion(
                v => string.Join(',', v),
                v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList());

        modelBuilder.Entity<FamilyMember>()
            .Property(fm => fm.Allergies)
            .HasConversion(
                v => string.Join(',', v),
                v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList());

        // Composite keys join table-ների համար
        modelBuilder.Entity<RecipeIngredient>()
            .HasKey(ri => new { ri.RecipeId, ri.IngredientId });

        modelBuilder.Entity<RecipeTag>()
            .HasKey(rt => new { rt.RecipeId, rt.TagId });

        // Decimal precision Quantity-ի համար (կանխում է rounding warning-ը)
        modelBuilder.Entity<RecipeIngredient>()
            .Property(ri => ri.Quantity)
            .HasPrecision(8, 2);

        // Query filter-ներ
        modelBuilder.Entity<Recipe>().HasQueryFilter(r => !r.IsDeleted);
        modelBuilder.Entity<Ingredient>().HasQueryFilter(i => !i.IsDeleted);
        modelBuilder.Entity<Tag>().HasQueryFilter(t => !t.IsDeleted);
        modelBuilder.Entity<Feedback>().HasQueryFilter(f => !f.IsDeleted);
        // GialoraDbContext.cs, OnModelCreating-ում
        modelBuilder.Entity<Recipe>()
            .HasOne(r => r.CreatedByAdmin)
            .WithMany()
            .HasForeignKey(r => r.CreatedByAdminId)
            .OnDelete(DeleteBehavior.Restrict); // ջնջել admin-ին՝ միայն եթե նրա recipe-ները արդեն reassign են արվել

        // Feedback-ում կանխում ենք որ նույն user-ը մեկ recipe-ին մի քանի rating տա
        modelBuilder.Entity<Feedback>()
            .HasIndex(f => new { f.RecipeId, f.UserId })
            .IsUnique();

    }
}