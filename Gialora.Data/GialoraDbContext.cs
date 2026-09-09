// Gialora.Data/GialoraDbContext.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Gialora.Data.Entities;

namespace Gialora.Data;

public class GialoraDbContext : DbContext
{
    public GialoraDbContext(DbContextOptions<GialoraDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Family> Families => Set<Family>();
    public DbSet<FamilyMember> FamilyMembers => Set<FamilyMember>();
    public DbSet<MealPlan> MealPlans => Set<MealPlan>();
    public DbSet<MealPlanDay> MealPlanDays => Set<MealPlanDay>();
    public DbSet<MealPlanEntry> MealPlanEntries => Set<MealPlanEntry>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<RecipeIngredient> RecipeIngredients => Set<RecipeIngredient>();
    public DbSet<RecipeTag> RecipeTags => Set<RecipeTag>();
    public DbSet<Feedback> Feedbacks => Set<Feedback>();
    public DbSet<FavoriteRecipe> FavoriteRecipes => Set<FavoriteRecipe>();
    public DbSet<ShoppingList> ShoppingLists => Set<ShoppingList>();
    public DbSet<ShoppingListItem> ShoppingListItems => Set<ShoppingListItem>();
    public DbSet<BlogPost> BlogPosts => Set<BlogPost>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // CSV <-> List<string> converter։ ValueComparer-ը պարտադիր է — առանց դրա EF-ը
        // չի նկատում list-ի ՆԵՐՍԻ փոփոխությունը և SaveChanges-ը լուռ ոչինչ չի գրում։
        var csvConverter = new ValueConverter<List<string>, string>(
            v => string.Join("|", v),
            v => v.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).ToList());

        var csvComparer = new ValueComparer<List<string>>(
            (a, b) => a != null && b != null && a.SequenceEqual(b),
            v => v.Aggregate(0, (acc, s) => HashCode.Combine(acc, s.GetHashCode())),
            v => v.ToList());

        // ---------------- User ----------------
        modelBuilder.Entity<User>(b =>
        {
            b.Property(u => u.Email).HasMaxLength(256).IsRequired();
            b.Property(u => u.DisplayName).HasMaxLength(100).IsRequired();
            b.Property(u => u.PasswordHash).HasMaxLength(256).IsRequired();
            b.HasIndex(u => u.Email).IsUnique();
            b.HasQueryFilter(u => !u.IsDeleted);
        });

        // ---------------- Family ----------------
        modelBuilder.Entity<Family>(b =>
        {
            b.Property(f => f.Name).HasMaxLength(150).IsRequired();
            b.Property(f => f.ExcludedProteins).HasConversion(csvConverter).Metadata.SetValueComparer(csvComparer);
            b.Property(f => f.DislikedIngredients).HasConversion(csvConverter).Metadata.SetValueComparer(csvComparer);
            b.HasQueryFilter(f => !f.IsDeleted);
        });

        modelBuilder.Entity<FamilyMember>(b =>
        {
            b.Property(fm => fm.Name).HasMaxLength(100).IsRequired();
            b.Property(fm => fm.DietaryRestrictions).HasConversion(csvConverter).Metadata.SetValueComparer(csvComparer);
            b.Property(fm => fm.Allergies).HasConversion(csvConverter).Metadata.SetValueComparer(csvComparer);
            b.Property(fm => fm.Goals).HasConversion(csvConverter).Metadata.SetValueComparer(csvComparer);
            b.Ignore(fm => fm.EffectiveAgeMonths); // հաշվարկվող — column չէ

            b.HasOne(fm => fm.Family)
             .WithMany(f => f.FamilyMembers)
             .HasForeignKey(fm => fm.FamilyId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasQueryFilter(fm => !fm.IsDeleted);
        });

        // ---------------- Recipe ----------------
        modelBuilder.Entity<Recipe>(b =>
        {
            b.Property(r => r.Title).HasMaxLength(200).IsRequired();
            b.Property(r => r.Slug).HasMaxLength(220).IsRequired();
            b.Property(r => r.Description).HasMaxLength(1000);
            b.Property(r => r.ImageUrl).HasMaxLength(500);
            b.Property(r => r.EstimatedCostPerServing).HasPrecision(8, 2);
            b.Ignore(r => r.TotalTimeMinutes); // հաշվարկվող — column չէ

            b.HasIndex(r => r.Slug).IsUnique();
            // Catalog-ի և planner-ի ամենահաճախ օգտագործվող filter-ի համադրությունը
            b.HasIndex(r => new { r.IsPublished, r.MealType, r.DietType });

            b.HasOne(r => r.CreatedByAdmin)
             .WithMany()
             .HasForeignKey(r => r.CreatedByAdminId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasQueryFilter(r => !r.IsDeleted);
        });

        modelBuilder.Entity<Ingredient>(b =>
        {
            b.Property(i => i.Name).HasMaxLength(120).IsRequired();
            b.HasIndex(i => i.Name).IsUnique();
            b.HasQueryFilter(i => !i.IsDeleted);
        });

        modelBuilder.Entity<Tag>(b =>
        {
            b.Property(t => t.Name).HasMaxLength(60).IsRequired();
            b.Property(t => t.DisplayName).HasMaxLength(60).IsRequired();
            b.HasIndex(t => t.Name).IsUnique();
            b.HasQueryFilter(t => !t.IsDeleted);
        });

        modelBuilder.Entity<RecipeIngredient>(b =>
        {
            b.HasKey(ri => new { ri.RecipeId, ri.IngredientId });
            b.Property(ri => ri.Quantity).HasPrecision(10, 3);
            b.Property(ri => ri.Note).HasMaxLength(200);

            b.HasOne(ri => ri.Recipe)
             .WithMany(r => r.RecipeIngredients)
             .HasForeignKey(ri => ri.RecipeId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(ri => ri.Ingredient)
             .WithMany(i => i.RecipeIngredients)
             .HasForeignKey(ri => ri.IngredientId)
             .OnDelete(DeleteBehavior.Restrict);

            // Join table-ը ինքը IsDeleted չունի, բայց երկու ծայրն էլ ունեն։ Առանց
            // համապատասխան filter-ի EF-ը զգուշացնում է, և soft-delete արված
            // բաղադրիչը դեռ կհայտնվեր ռեցեպտի ու shopping list-ի մեջ։
            b.HasQueryFilter(ri => !ri.Recipe.IsDeleted && !ri.Ingredient.IsDeleted);
        });

        modelBuilder.Entity<RecipeTag>(b =>
        {
            b.HasKey(rt => new { rt.RecipeId, rt.TagId });

            b.HasOne(rt => rt.Recipe)
             .WithMany(r => r.RecipeTags)
             .HasForeignKey(rt => rt.RecipeId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(rt => rt.Tag)
             .WithMany(t => t.RecipeTags)
             .HasForeignKey(rt => rt.TagId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasQueryFilter(rt => !rt.Recipe.IsDeleted && !rt.Tag.IsDeleted);
        });

        // ---------------- Meal plans ----------------
        modelBuilder.Entity<MealPlan>(b =>
        {
            b.Property(mp => mp.Name).HasMaxLength(150);
            b.Property(mp => mp.PlanningNotes).HasConversion(csvConverter).Metadata.SetValueComparer(csvComparer);

            b.HasOne(mp => mp.Family)
             .WithMany(f => f.MealPlans)
             .HasForeignKey(mp => mp.FamilyId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(mp => new { mp.FamilyId, mp.WeekStartDate });
            b.HasQueryFilter(mp => !mp.IsDeleted);
        });

        modelBuilder.Entity<MealPlanDay>(b =>
        {
            b.HasOne(d => d.MealPlan)
             .WithMany(mp => mp.Days)
             .HasForeignKey(d => d.MealPlanId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasQueryFilter(d => !d.IsDeleted);
        });

        modelBuilder.Entity<MealPlanEntry>(b =>
        {
            b.HasOne(e => e.MealPlanDay)
             .WithMany(d => d.Entries)
             .HasForeignKey(e => e.MealPlanDayId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(e => e.Recipe)
             .WithMany()
             .HasForeignKey(e => e.RecipeId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasQueryFilter(e => !e.IsDeleted);
        });

        // ---------------- Shopping lists ----------------
        modelBuilder.Entity<ShoppingList>(b =>
        {
            b.Property(s => s.Name).HasMaxLength(150);

            b.HasOne(s => s.Family)
             .WithMany()
             .HasForeignKey(s => s.FamilyId)
             .OnDelete(DeleteBehavior.Cascade);

            // Մեկ meal plan → առավելագույնը մեկ shopping list։
            // ClientCascade — SQL Server-ը երկու cascade ուղի (Family → MealPlan → List
            // և Family → List) միաժամանակ չի թույլատրում։
            b.HasOne(s => s.MealPlan)
             .WithOne(mp => mp.ShoppingList!)
             .HasForeignKey<ShoppingList>(s => s.MealPlanId)
             .OnDelete(DeleteBehavior.ClientCascade);

            b.HasQueryFilter(s => !s.IsDeleted);
        });

        modelBuilder.Entity<ShoppingListItem>(b =>
        {
            b.Property(i => i.DisplayName).HasMaxLength(150).IsRequired();
            b.Property(i => i.Quantity).HasPrecision(10, 3);
            b.Property(i => i.SourceRecipes).HasConversion(csvConverter).Metadata.SetValueComparer(csvComparer);

            b.HasOne(i => i.ShoppingList)
             .WithMany(s => s.Items)
             .HasForeignKey(i => i.ShoppingListId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(i => i.Ingredient)
             .WithMany()
             .HasForeignKey(i => i.IngredientId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasQueryFilter(i => !i.IsDeleted);
        });

        // ---------------- Feedback / favorites ----------------
        modelBuilder.Entity<Feedback>(b =>
        {
            b.Property(f => f.Comment).HasMaxLength(1000);

            b.HasOne(f => f.Recipe)
             .WithMany(r => r.Feedbacks)
             .HasForeignKey(f => f.RecipeId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(f => f.User)
             .WithMany(u => u.Feedbacks)
             .HasForeignKey(f => f.UserId)
             .OnDelete(DeleteBehavior.Restrict);

            // Նույն user-ը մեկ recipe-ի համար ունի ՄԵԿ feedback (upsert ենք անում)
            b.HasIndex(f => new { f.RecipeId, f.UserId }).IsUnique();
            b.HasQueryFilter(f => !f.IsDeleted);
        });

        modelBuilder.Entity<FavoriteRecipe>(b =>
        {
            b.HasKey(f => new { f.UserId, f.RecipeId });

            b.HasOne(f => f.User)
             .WithMany(u => u.Favorites)
             .HasForeignKey(f => f.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(f => f.Recipe)
             .WithMany(r => r.Favorites)
             .HasForeignKey(f => f.RecipeId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasQueryFilter(f => !f.Recipe.IsDeleted && !f.User.IsDeleted);
        });

        // ---------------- Blog ----------------
        modelBuilder.Entity<BlogPost>(b =>
        {
            b.Property(p => p.Title).HasMaxLength(200).IsRequired();
            b.Property(p => p.Slug).HasMaxLength(220).IsRequired();
            b.Property(p => p.Excerpt).HasMaxLength(500);
            b.Property(p => p.CoverImageUrl).HasMaxLength(500);
            b.HasIndex(p => p.Slug).IsUnique();

            b.HasOne(p => p.Author)
             .WithMany()
             .HasForeignKey(p => p.AuthorId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasQueryFilter(p => !p.IsDeleted);
        });
    }

    /// <summary>
    /// UpdatedAtUtc-ը ձեռքով դնելը ամեն service-ում մոռացվում էր — կենտրոնացնում ենք այստեղ։
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        TouchTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        TouchTimestamps();
        return base.SaveChanges();
    }

    private void TouchTimestamps()
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Modified)
                entry.Entity.UpdatedAtUtc = DateTime.UtcNow;
        }
    }
}
