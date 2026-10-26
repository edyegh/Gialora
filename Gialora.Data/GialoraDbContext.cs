// Gialora.Data/GialoraDbContext.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Gialora.Data.Entities;
using Gialora.Shared.Enums;

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
    public DbSet<RecipeTranslation> RecipeTranslations => Set<RecipeTranslation>();
    public DbSet<IngredientTranslation> IngredientTranslations => Set<IngredientTranslation>();
    public DbSet<TagTranslation> TagTranslations => Set<TagTranslation>();
    public DbSet<Feedback> Feedbacks => Set<Feedback>();
    public DbSet<FavoriteRecipe> FavoriteRecipes => Set<FavoriteRecipe>();
    public DbSet<ShoppingList> ShoppingLists => Set<ShoppingList>();
    public DbSet<ShoppingListItem> ShoppingListItems => Set<ShoppingListItem>();
    public DbSet<BlogPost> BlogPosts => Set<BlogPost>();
    public DbSet<ChatConversation> ChatConversations => Set<ChatConversation>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<UserActivity> UserActivities => Set<UserActivity>();

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

        // List<Allergen> <-> "TreeNuts|Milk"։ ԱՆՎԱՆԲ, ոչ թե թվով — enum-ի հերթականությունը
        // փոխելը չպիտի փոխի պահված տվյալի իմաստը։ Անհայտ անունը (հին/սխալ տվյալ) բաց է թողնվում։
        var allergenConverter = new ValueConverter<List<Allergen>, string>(
            v => FormatAllergens(v),
            v => ParseAllergens(v));

        var allergenComparer = new ValueComparer<List<Allergen>>(
            (a, b) => a != null && b != null && a.SequenceEqual(b),
            v => v.Aggregate(0, (acc, x) => HashCode.Combine(acc, x.GetHashCode())),
            v => v.ToList());

        // ---------------- User ----------------
        modelBuilder.Entity<User>(b =>
        {
            b.Property(u => u.Email).HasMaxLength(256).IsRequired();
            b.Property(u => u.DisplayName).HasMaxLength(100).IsRequired();
            b.Property(u => u.PasswordHash).HasMaxLength(256).IsRequired();
            b.Property(u => u.EmailVerificationCodeHash).HasMaxLength(64);
            // Concurrency token — զուգահեռ verify request-ները չեն կարող փորձերի սահմանը շրջանցել
            b.Property(u => u.EmailVerificationAttempts).IsConcurrencyToken();
            // Concurrency token — զուգահեռ առաջին request-ները երկու ընտանիք չստեղծեն (FamilyService)
            b.Property(u => u.FamilyId).IsConcurrencyToken();
            // Filtered — ջնջված user-ի email-ով նորից գրանցվելը հնարավոր է
            b.HasIndex(u => u.Email).IsUnique().HasFilter("[IsDeleted] = 0");
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
            b.Property(fm => fm.Allergens).HasConversion(allergenConverter).Metadata.SetValueComparer(allergenComparer);
            b.Property(fm => fm.OtherAllergies).HasConversion(csvConverter).Metadata.SetValueComparer(csvComparer);
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
            b.Property(r => r.Slug).HasMaxLength(220).IsRequired();
            b.Property(r => r.ImageUrl).HasMaxLength(500);
            b.Property(r => r.EstimatedCostPerServing).HasPrecision(8, 2);
            b.Ignore(r => r.TotalTimeMinutes); // հաշվարկվող — column չէ

            // Unique-ը միայն ՉՋՆՋՎԱԾ տողերի վրա. soft-deleted ռեցեպտի slug-ը պիտի ազատ լինի
            // նույն վերնագրով նորի համար (այլապես filter-ված ստուգումը բաց թողներ, INSERT-ը՝ 500)
            b.HasIndex(r => r.Slug).IsUnique().HasFilter("[IsDeleted] = 0");
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
            b.Property(i => i.Allergens).HasConversion(allergenConverter).Metadata.SetValueComparer(allergenComparer);
            b.HasQueryFilter(i => !i.IsDeleted);
        });

        modelBuilder.Entity<Tag>(b =>
        {
            b.Property(t => t.Name).HasMaxLength(60).IsRequired();
            b.HasIndex(t => t.Name).IsUnique().HasFilter("[IsDeleted] = 0");
            b.HasQueryFilter(t => !t.IsDeleted);
        });

        // ---------------- Translations ----------------
        // Մեկ տող (ծնող, լեզու) զույգի համար։ Ծնողը hard-delete չի լինում (soft delete),
        // ուստի cascade-ը գործնականում միայն test/admin մաքրման համար է։

        modelBuilder.Entity<RecipeTranslation>(b =>
        {
            b.HasKey(t => new { t.RecipeId, t.Language });
            b.Property(t => t.Language).HasMaxLength(8);
            b.Property(t => t.Title).HasMaxLength(200).IsRequired();
            b.Property(t => t.Description).HasMaxLength(1000);

            b.HasOne(t => t.Recipe)
             .WithMany(r => r.Translations)
             .HasForeignKey(t => t.RecipeId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasQueryFilter(t => !t.Recipe.IsDeleted);
        });

        modelBuilder.Entity<IngredientTranslation>(b =>
        {
            b.HasKey(t => new { t.IngredientId, t.Language });
            b.Property(t => t.Language).HasMaxLength(8);
            b.Property(t => t.Name).HasMaxLength(120).IsRequired();

            // Նույն լեզվով երկու բաղադրիչ նույն անունով չի կարող լինել։ Filter չունի,
            // որովհետև IsDeleted-ը ծնողի վրա է — դրա փոխարեն IngredientService-ը
            // soft delete-ի ժամանակ ջնջում է ջնջված բաղադրիչի թարգմանությունները,
            // որ անունը ազատվի նորի համար։
            b.HasIndex(t => new { t.Language, t.Name }).IsUnique();

            b.HasOne(t => t.Ingredient)
             .WithMany(i => i.Translations)
             .HasForeignKey(t => t.IngredientId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasQueryFilter(t => !t.Ingredient.IsDeleted);
        });

        modelBuilder.Entity<TagTranslation>(b =>
        {
            b.HasKey(t => new { t.TagId, t.Language });
            b.Property(t => t.Language).HasMaxLength(8);
            b.Property(t => t.DisplayName).HasMaxLength(60).IsRequired();

            b.HasOne(t => t.Tag)
             .WithMany(tag => tag.Translations)
             .HasForeignKey(t => t.TagId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasQueryFilter(t => !t.Tag.IsDeleted);
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

            // "Մեկ plan → մեկ list"-ը պարտադրում ենք միայն ակտիվ list-երի համար. ջնջված
            // list-ը MealPlanId-ն պահում է, և առանց IsDeleted-ի filter-ի նոր list-ը
            // նույն plan-ի համար այլևս չէր ստեղծվի (unique index-ի բախում → 500)։
            b.HasIndex(s => s.MealPlanId)
             .IsUnique()
             .HasFilter("[MealPlanId] IS NOT NULL AND [IsDeleted] = 0");

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
            b.HasIndex(p => p.Slug).IsUnique().HasFilter("[IsDeleted] = 0");

            b.HasOne(p => p.Author)
             .WithMany()
             .HasForeignKey(p => p.AuthorId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasQueryFilter(p => !p.IsDeleted);
        });

        // ---------------- Support chat ----------------
        modelBuilder.Entity<ChatConversation>(b =>
        {
            b.Property(c => c.GuestKeyHash).HasMaxLength(64);

            // Մեկ user / մեկ հյուրի բանալի → մեկ ակտիվ խոսակցություն
            b.HasIndex(c => c.UserId).IsUnique().HasFilter("[UserId] IS NOT NULL AND [IsDeleted] = 0");
            b.HasIndex(c => c.GuestKeyHash).IsUnique().HasFilter("[GuestKeyHash] IS NOT NULL AND [IsDeleted] = 0");

            b.HasOne(c => c.User)
             .WithMany()
             .HasForeignKey(c => c.UserId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasQueryFilter(c => !c.IsDeleted);
        });

        modelBuilder.Entity<ChatMessage>(b =>
        {
            b.Property(m => m.Text).HasMaxLength(4096).IsRequired(); // Telegram-ի հաղորդագրության սահմանը
            b.Property(m => m.Sender).HasConversion<string>().HasMaxLength(10);

            b.HasIndex(m => new { m.ConversationId, m.CreatedAtUtc });
            // Admin-ի "Reply"-ը գտնում ենք bot-ի ուղարկած պատճենի id-ով
            b.HasIndex(m => m.TelegramMessageId).HasFilter("[TelegramMessageId] IS NOT NULL");

            b.HasOne(m => m.Conversation)
             .WithMany(c => c.Messages)
             .HasForeignKey(m => m.ConversationId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasQueryFilter(m => !m.IsDeleted && !m.Conversation.IsDeleted);
        });

        // ---------------- Usage statistics ----------------
        modelBuilder.Entity<UserActivity>(b =>
        {
            b.Property(a => a.Type).HasConversion<string>().HasMaxLength(20);
            b.Property(a => a.Target).HasMaxLength(120);
            b.Property(a => a.Path).HasMaxLength(300);

            // Admin-ի էջը միշտ ժամանակահատվածով է ֆիլտրում
            b.HasIndex(a => new { a.Type, a.CreatedAtUtc });
            b.HasIndex(a => new { a.UserId, a.CreatedAtUtc });

            b.HasOne(a => a.User)
             .WithMany()
             .HasForeignKey(a => a.UserId)
             .OnDelete(DeleteBehavior.Restrict);

            b.HasQueryFilter(a => !a.IsDeleted);
        });
    }

    // ValueConverter-ի lambda-ն expression tree է, որտեղ "out var" չի թույլատրվում —
    // ուստի parse/format-ը առանձին մեթոդներ են։
    private static string FormatAllergens(List<Allergen> allergens) =>
        string.Join("|", allergens.Distinct().OrderBy(a => a.ToString()).Select(a => a.ToString()));

    private static List<Allergen> ParseAllergens(string value) =>
        value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
             .Select(s => Enum.TryParse<Allergen>(s, out var a) && Enum.IsDefined(a) ? (Allergen?)a : null)
             .Where(a => a.HasValue)
             .Select(a => a!.Value)
             .Distinct()
             .ToList();

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
