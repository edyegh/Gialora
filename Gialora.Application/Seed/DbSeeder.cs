// Gialora.Application/Seed/DbSeeder.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Gialora.Application.Common;
using Gialora.Data;
using Gialora.Data.Entities;
using Gialora.Shared.Enums;

namespace Gialora.Application.Seed;

/// <summary>
/// Առաջին գործարկման տվյալները՝ admin հաշիվ, բաղադրիչների կատալոգ և մի քանի
/// միջերկրածովյան ռեցեպտ։ Առանց սրանց meal-planning engine-ը դատարկ բազայի վրա
/// ոչինչ չէր կարող առաջարկել, և app-ը "կոտրված" տպավորություն կթողներ։
///
/// Idempotent է — ամեն startup-ին ապահով կանչվում է, կրկնօրինակներ չի ստեղծում։
/// </summary>
public class DbSeeder
{
    private readonly GialoraDbContext _db;
    private readonly ILogger<DbSeeder> _logger;

    public DbSeeder(GialoraDbContext db, ILogger<DbSeeder> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task SeedAsync(string adminEmail, string adminPassword)
    {
        var admin = await EnsureAdminAsync(adminEmail, adminPassword);
        var ingredients = await EnsureIngredientsAsync();
        var tags = await EnsureTagsAsync();
        await EnsureRecipesAsync(admin, ingredients, tags);
        await EnsureBlogAsync(admin);
    }

    // -----------------------------------------------------------------------
    // Admin
    // -----------------------------------------------------------------------

    private async Task<User> EnsureAdminAsync(string email, string password)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var existing = await _db.Users.FirstOrDefaultAsync(u => u.Email == normalized);

        if (existing is not null)
        {
            // Դերը վերականգնում ենք, եթե ձեռքով փոխվել է — առանց admin-ի
            // admin panel-ը դառնում է անհասանելի։
            if (existing.Role != UserRole.Admin)
            {
                existing.Role = UserRole.Admin;
                await _db.SaveChangesAsync();
            }

            return existing;
        }

        var admin = new User
        {
            Email = normalized,
            DisplayName = "Gialora Admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12),
            Role = UserRole.Admin,
            EmailConfirmed = true
        };

        _db.Users.Add(admin);
        await _db.SaveChangesAsync();

        _logger.LogWarning(
            "Seeded admin account {Email}. Change the password before going to production.", normalized);

        return admin;
    }

    // -----------------------------------------------------------------------
    // Ingredients
    // -----------------------------------------------------------------------

    private record IngredientSpec(
        string Name,
        IngredientCategory Category,
        UnitOfMeasure Unit,
        bool Gluten = false,
        bool Dairy = false,
        bool Nuts = false,
        bool Egg = false,
        bool Fish = false,
        bool Shellfish = false,
        bool Soy = false,
        bool Staple = false);

    private static readonly IngredientSpec[] Catalog =
    {
        // Meat & fish
        new("Chicken mince", IngredientCategory.Meat, UnitOfMeasure.Gram),
        new("Chicken breast", IngredientCategory.Meat, UnitOfMeasure.Gram),
        new("Chicken thighs", IngredientCategory.Meat, UnitOfMeasure.Gram),
        new("Beef mince", IngredientCategory.Meat, UnitOfMeasure.Gram),
        new("Lamb mince", IngredientCategory.Meat, UnitOfMeasure.Gram),
        new("Salmon fillet", IngredientCategory.Fish, UnitOfMeasure.Gram, Fish: true),
        new("White fish fillet", IngredientCategory.Fish, UnitOfMeasure.Gram, Fish: true),

        // Dairy & eggs
        new("Egg", IngredientCategory.DairyAndEggs, UnitOfMeasure.Piece, Egg: true),
        new("Greek yogurt", IngredientCategory.DairyAndEggs, UnitOfMeasure.Gram, Dairy: true),
        new("Feta cheese", IngredientCategory.DairyAndEggs, UnitOfMeasure.Gram, Dairy: true),
        new("Parmesan", IngredientCategory.DairyAndEggs, UnitOfMeasure.Gram, Dairy: true),
        new("Milk", IngredientCategory.DairyAndEggs, UnitOfMeasure.Milliliter, Dairy: true),

        // Vegetables
        new("Onion", IngredientCategory.Vegetables, UnitOfMeasure.Gram),
        new("Garlic", IngredientCategory.Vegetables, UnitOfMeasure.Clove),
        new("Carrot", IngredientCategory.Vegetables, UnitOfMeasure.Gram),
        new("Courgette", IngredientCategory.Vegetables, UnitOfMeasure.Gram),
        new("Aubergine", IngredientCategory.Vegetables, UnitOfMeasure.Gram),
        new("Tomato", IngredientCategory.Vegetables, UnitOfMeasure.Gram),
        new("Cherry tomatoes", IngredientCategory.Vegetables, UnitOfMeasure.Gram),
        new("Red pepper", IngredientCategory.Vegetables, UnitOfMeasure.Piece),
        new("Cucumber", IngredientCategory.Vegetables, UnitOfMeasure.Piece),
        new("Spinach", IngredientCategory.Vegetables, UnitOfMeasure.Gram),
        new("Potato", IngredientCategory.Vegetables, UnitOfMeasure.Gram),
        new("Sweet potato", IngredientCategory.Vegetables, UnitOfMeasure.Gram),
        new("Celery", IngredientCategory.Vegetables, UnitOfMeasure.Gram),
        new("Broccoli", IngredientCategory.Vegetables, UnitOfMeasure.Gram),

        // Fruit
        new("Lemon", IngredientCategory.Fruit, UnitOfMeasure.Piece),
        new("Olives", IngredientCategory.Fruit, UnitOfMeasure.Gram),

        // Grains & legumes
        new("Pasta", IngredientCategory.Grains, UnitOfMeasure.Gram, Gluten: true),
        new("Couscous", IngredientCategory.Grains, UnitOfMeasure.Gram, Gluten: true),
        new("Rice", IngredientCategory.Grains, UnitOfMeasure.Gram),
        new("Bulgur", IngredientCategory.Grains, UnitOfMeasure.Gram, Gluten: true),
        new("Breadcrumbs", IngredientCategory.Pantry, UnitOfMeasure.Gram, Gluten: true),
        new("Pita bread", IngredientCategory.Bakery, UnitOfMeasure.Piece, Gluten: true),
        new("Red lentils", IngredientCategory.Legumes, UnitOfMeasure.Gram),
        new("Chickpeas", IngredientCategory.Legumes, UnitOfMeasure.Gram),
        new("White beans", IngredientCategory.Legumes, UnitOfMeasure.Gram),

        // Herbs, spices, oils, pantry
        new("Parsley", IngredientCategory.HerbsAndSpices, UnitOfMeasure.Bunch),
        new("Mint", IngredientCategory.HerbsAndSpices, UnitOfMeasure.Bunch),
        new("Oregano", IngredientCategory.HerbsAndSpices, UnitOfMeasure.Teaspoon, Staple: true),
        new("Cumin", IngredientCategory.HerbsAndSpices, UnitOfMeasure.Teaspoon, Staple: true),
        new("Paprika", IngredientCategory.HerbsAndSpices, UnitOfMeasure.Teaspoon, Staple: true),
        new("Salt", IngredientCategory.HerbsAndSpices, UnitOfMeasure.Teaspoon, Staple: true),
        new("Black pepper", IngredientCategory.HerbsAndSpices, UnitOfMeasure.Teaspoon, Staple: true),
        new("Olive oil", IngredientCategory.OilsAndVinegars, UnitOfMeasure.Tablespoon, Staple: true),
        new("Chopped tomatoes", IngredientCategory.Pantry, UnitOfMeasure.Can),
        new("Vegetable stock", IngredientCategory.Pantry, UnitOfMeasure.Milliliter),
        new("Tahini", IngredientCategory.Pantry, UnitOfMeasure.Tablespoon, Nuts: true),
        new("Tomato paste", IngredientCategory.Pantry, UnitOfMeasure.Tablespoon)
    };

    private async Task<Dictionary<string, Ingredient>> EnsureIngredientsAsync()
    {
        var existing = await _db.Ingredients.ToDictionaryAsync(i => i.Name, StringComparer.OrdinalIgnoreCase);
        var created = 0;

        foreach (var spec in Catalog)
        {
            if (existing.ContainsKey(spec.Name))
                continue;

            var ingredient = new Ingredient
            {
                Name = spec.Name,
                Category = spec.Category,
                DefaultUnit = spec.Unit,
                ContainsGluten = spec.Gluten,
                ContainsDairy = spec.Dairy,
                ContainsNuts = spec.Nuts,
                ContainsEgg = spec.Egg,
                ContainsFish = spec.Fish,
                ContainsShellfish = spec.Shellfish,
                ContainsSoy = spec.Soy,
                IsPantryStaple = spec.Staple
            };

            _db.Ingredients.Add(ingredient);
            existing[spec.Name] = ingredient;
            created++;
        }

        if (created > 0)
        {
            await _db.SaveChangesAsync();
            _logger.LogInformation("Seeded {Count} ingredients", created);
        }

        return existing;
    }

    // -----------------------------------------------------------------------
    // Tags
    // -----------------------------------------------------------------------

    private static readonly string[] TagCatalog =
    {
        "quick", "kid-friendly", "high-protein", "iron-rich", "freezer-friendly",
        "lunchbox", "one-pot", "batch-cook", "legumes", "vegetarian", "budget", "make-ahead"
    };

    private async Task<Dictionary<string, Tag>> EnsureTagsAsync()
    {
        var existing = await _db.Tags.ToDictionaryAsync(t => t.Name);
        var created = 0;

        foreach (var name in TagCatalog)
        {
            if (existing.ContainsKey(name))
                continue;

            var tag = new Tag { Name = name, DisplayName = Services.RecipeService.ToDisplayName(name) };
            _db.Tags.Add(tag);
            existing[name] = tag;
            created++;
        }

        if (created > 0)
            await _db.SaveChangesAsync();

        return existing;
    }

    // -----------------------------------------------------------------------
    // Recipes
    // -----------------------------------------------------------------------

    private record RecipeSpec(
        string Title,
        string Description,
        MealType MealType,
        DietType Diet,
        ProteinType Protein,
        int Prep,
        int Cook,
        int MinAgeMonths,
        bool Freezer,
        bool Lunchbox,
        bool KidFriendly,
        bool HighProtein,
        bool IronRich,
        bool Batch,
        decimal Cost,
        string[] Tags,
        (string Ingredient, decimal Quantity, UnitOfMeasure Unit)[] Ingredients,
        string[] Steps);

    private static readonly RecipeSpec[] RecipeCatalog =
    {
        new("Chicken meatballs",
            "Soft baked chicken meatballs with parsley and onion — the recipe kids ask for again.",
            MealType.Dinner, DietType.Meat, ProteinType.Chicken,
            15, 20, 12, true, true, true, true, false, true, 2.40m,
            new[] { "kid-friendly", "high-protein", "freezer-friendly", "lunchbox" },
            new[]
            {
                ("Chicken mince", 500m, UnitOfMeasure.Gram),
                ("Egg", 1m, UnitOfMeasure.Piece),
                ("Onion", 100m, UnitOfMeasure.Gram),
                ("Breadcrumbs", 50m, UnitOfMeasure.Gram),
                ("Parsley", 0.5m, UnitOfMeasure.Bunch),
                ("Olive oil", 2m, UnitOfMeasure.Tablespoon),
                ("Salt", 1m, UnitOfMeasure.Teaspoon)
            },
            new[]
            {
                "Heat the oven to 200°C.",
                "Grate the onion and chop the parsley finely.",
                "Mix the mince, egg, breadcrumbs, onion, parsley and salt.",
                "Shape into 20 small balls and place on an oiled tray.",
                "Bake for 20 minutes until golden and cooked through."
            }),

        new("Red lentil soup",
            "A thick, iron-rich lentil soup that freezes beautifully and costs very little.",
            MealType.Dinner, DietType.Vegan, ProteinType.Legume,
            10, 25, 10, true, false, true, true, true, true, 0.90m,
            new[] { "legumes", "iron-rich", "freezer-friendly", "budget", "one-pot", "vegetarian" },
            new[]
            {
                ("Red lentils", 300m, UnitOfMeasure.Gram),
                ("Onion", 150m, UnitOfMeasure.Gram),
                ("Carrot", 200m, UnitOfMeasure.Gram),
                ("Garlic", 2m, UnitOfMeasure.Clove),
                ("Cumin", 1m, UnitOfMeasure.Teaspoon),
                ("Vegetable stock", 1200m, UnitOfMeasure.Milliliter),
                ("Olive oil", 2m, UnitOfMeasure.Tablespoon),
                ("Lemon", 1m, UnitOfMeasure.Piece)
            },
            new[]
            {
                "Soften the chopped onion, carrot and garlic in olive oil for 5 minutes.",
                "Stir in the cumin, then add the lentils and stock.",
                "Simmer for 20 minutes until the lentils collapse.",
                "Blend until smooth and finish with lemon juice."
            }),

        new("Chicken and yogurt wraps",
            "Leftover-friendly wraps with lemony chicken, cucumber and Greek yogurt.",
            MealType.Lunch, DietType.Meat, ProteinType.Chicken,
            15, 12, 24, false, true, true, true, false, false, 2.10m,
            new[] { "quick", "lunchbox", "high-protein", "kid-friendly" },
            new[]
            {
                ("Chicken breast", 400m, UnitOfMeasure.Gram),
                ("Pita bread", 4m, UnitOfMeasure.Piece),
                ("Greek yogurt", 200m, UnitOfMeasure.Gram),
                ("Cucumber", 1m, UnitOfMeasure.Piece),
                ("Parsley", 0.5m, UnitOfMeasure.Bunch),
                ("Lemon", 1m, UnitOfMeasure.Piece),
                ("Olive oil", 1m, UnitOfMeasure.Tablespoon),
                ("Paprika", 1m, UnitOfMeasure.Teaspoon)
            },
            new[]
            {
                "Slice the chicken and toss with olive oil, paprika and lemon zest.",
                "Pan-fry for 10–12 minutes until cooked through.",
                "Mix the yogurt with grated cucumber, chopped parsley and lemon juice.",
                "Warm the pitas and fill with chicken and yogurt sauce."
            }),

        new("Baked salmon with vegetables",
            "One tray, twenty minutes: salmon, cherry tomatoes and courgette.",
            MealType.Dinner, DietType.Fish, ProteinType.Fish,
            10, 20, 12, false, false, false, true, false, false, 4.20m,
            new[] { "quick", "high-protein", "one-pot" },
            new[]
            {
                ("Salmon fillet", 500m, UnitOfMeasure.Gram),
                ("Courgette", 300m, UnitOfMeasure.Gram),
                ("Cherry tomatoes", 250m, UnitOfMeasure.Gram),
                ("Olive oil", 2m, UnitOfMeasure.Tablespoon),
                ("Lemon", 1m, UnitOfMeasure.Piece),
                ("Oregano", 1m, UnitOfMeasure.Teaspoon),
                ("Garlic", 2m, UnitOfMeasure.Clove)
            },
            new[]
            {
                "Heat the oven to 200°C.",
                "Spread the sliced courgette, tomatoes and garlic on a tray with olive oil and oregano.",
                "Roast for 10 minutes, then add the salmon on top.",
                "Roast for another 10 minutes and finish with lemon."
            }),

        new("Chickpea and spinach stew",
            "A Levantine weeknight stew — cheap, filling and rich in iron.",
            MealType.Dinner, DietType.Vegan, ProteinType.Legume,
            10, 20, 12, true, true, false, true, true, true, 1.10m,
            new[] { "legumes", "iron-rich", "budget", "freezer-friendly", "vegetarian", "one-pot" },
            new[]
            {
                ("Chickpeas", 480m, UnitOfMeasure.Gram),
                ("Spinach", 250m, UnitOfMeasure.Gram),
                ("Chopped tomatoes", 1m, UnitOfMeasure.Can),
                ("Onion", 150m, UnitOfMeasure.Gram),
                ("Garlic", 3m, UnitOfMeasure.Clove),
                ("Cumin", 1m, UnitOfMeasure.Teaspoon),
                ("Olive oil", 2m, UnitOfMeasure.Tablespoon)
            },
            new[]
            {
                "Soften the onion and garlic in olive oil.",
                "Add the cumin, chickpeas and chopped tomatoes.",
                "Simmer for 15 minutes.",
                "Stir the spinach through until wilted."
            }),

        new("Pasta with tomato and basil",
            "The fifteen-minute fallback every family needs.",
            MealType.Dinner, DietType.Vegetarian, ProteinType.None,
            5, 15, 10, false, true, true, false, false, false, 0.80m,
            new[] { "quick", "kid-friendly", "budget", "vegetarian" },
            new[]
            {
                ("Pasta", 400m, UnitOfMeasure.Gram),
                ("Chopped tomatoes", 1m, UnitOfMeasure.Can),
                ("Garlic", 2m, UnitOfMeasure.Clove),
                ("Olive oil", 2m, UnitOfMeasure.Tablespoon),
                ("Parmesan", 40m, UnitOfMeasure.Gram),
                ("Oregano", 1m, UnitOfMeasure.Teaspoon)
            },
            new[]
            {
                "Boil the pasta in well-salted water.",
                "Warm the garlic in olive oil, add the tomatoes and oregano.",
                "Simmer for 10 minutes while the pasta cooks.",
                "Toss together and finish with parmesan."
            }),

        new("Greek salad with white beans",
            "A no-cook lunch that still delivers protein.",
            MealType.Lunch, DietType.Vegetarian, ProteinType.Legume,
            15, 0, 24, false, true, false, true, false, false, 1.60m,
            new[] { "quick", "legumes", "lunchbox", "vegetarian" },
            new[]
            {
                ("White beans", 400m, UnitOfMeasure.Gram),
                ("Cucumber", 1m, UnitOfMeasure.Piece),
                ("Tomato", 300m, UnitOfMeasure.Gram),
                ("Red pepper", 1m, UnitOfMeasure.Piece),
                ("Feta cheese", 150m, UnitOfMeasure.Gram),
                ("Olives", 80m, UnitOfMeasure.Gram),
                ("Olive oil", 3m, UnitOfMeasure.Tablespoon),
                ("Oregano", 1m, UnitOfMeasure.Teaspoon)
            },
            new[]
            {
                "Chop the cucumber, tomato and pepper into large pieces.",
                "Add the drained beans and olives.",
                "Dress with olive oil and oregano.",
                "Crumble the feta over the top."
            }),

        new("Lamb and bulgur pilaf",
            "A comforting batch-cook pilaf that reheats better than it cooks.",
            MealType.Dinner, DietType.Meat, ProteinType.Lamb,
            15, 30, 18, true, false, false, true, true, true, 3.10m,
            new[] { "batch-cook", "iron-rich", "freezer-friendly", "make-ahead" },
            new[]
            {
                ("Lamb mince", 500m, UnitOfMeasure.Gram),
                ("Bulgur", 300m, UnitOfMeasure.Gram),
                ("Onion", 150m, UnitOfMeasure.Gram),
                ("Tomato paste", 2m, UnitOfMeasure.Tablespoon),
                ("Vegetable stock", 700m, UnitOfMeasure.Milliliter),
                ("Olive oil", 2m, UnitOfMeasure.Tablespoon),
                ("Mint", 0.5m, UnitOfMeasure.Bunch)
            },
            new[]
            {
                "Brown the lamb with the chopped onion.",
                "Stir in the tomato paste and bulgur.",
                "Pour in the stock, cover and cook gently for 20 minutes.",
                "Rest for 5 minutes and stir the mint through."
            }),

        new("Roasted vegetable couscous",
            "Batch-roast the vegetables once and eat well for three days.",
            MealType.Lunch, DietType.Vegan, ProteinType.None,
            15, 25, 18, true, true, false, false, false, true, 1.20m,
            new[] { "batch-cook", "lunchbox", "vegetarian", "make-ahead" },
            new[]
            {
                ("Couscous", 300m, UnitOfMeasure.Gram),
                ("Aubergine", 300m, UnitOfMeasure.Gram),
                ("Courgette", 250m, UnitOfMeasure.Gram),
                ("Red pepper", 2m, UnitOfMeasure.Piece),
                ("Olive oil", 3m, UnitOfMeasure.Tablespoon),
                ("Lemon", 1m, UnitOfMeasure.Piece),
                ("Parsley", 0.5m, UnitOfMeasure.Bunch)
            },
            new[]
            {
                "Heat the oven to 210°C.",
                "Roast the chopped vegetables with olive oil for 25 minutes.",
                "Cover the couscous with an equal volume of boiling water and rest for 5 minutes.",
                "Fork through the vegetables, lemon juice and parsley."
            }),

        new("Baked white fish with potatoes",
            "A gentle, mild bake that works for small children.",
            MealType.Dinner, DietType.Fish, ProteinType.Fish,
            15, 30, 12, false, false, true, true, false, false, 3.40m,
            new[] { "kid-friendly", "high-protein" },
            new[]
            {
                ("White fish fillet", 500m, UnitOfMeasure.Gram),
                ("Potato", 600m, UnitOfMeasure.Gram),
                ("Cherry tomatoes", 200m, UnitOfMeasure.Gram),
                ("Olive oil", 3m, UnitOfMeasure.Tablespoon),
                ("Lemon", 1m, UnitOfMeasure.Piece),
                ("Garlic", 2m, UnitOfMeasure.Clove)
            },
            new[]
            {
                "Heat the oven to 200°C.",
                "Slice the potatoes thinly and roast with olive oil for 20 minutes.",
                "Lay the fish and tomatoes on top with the sliced garlic.",
                "Bake for another 10–12 minutes and squeeze over the lemon."
            }),

        new("Beef and vegetable ragu",
            "The freezer staple: make a double batch, eat it three ways.",
            MealType.Dinner, DietType.Meat, ProteinType.Beef,
            15, 40, 12, true, false, true, true, true, true, 2.80m,
            new[] { "batch-cook", "freezer-friendly", "iron-rich", "kid-friendly", "make-ahead" },
            new[]
            {
                ("Beef mince", 500m, UnitOfMeasure.Gram),
                ("Onion", 150m, UnitOfMeasure.Gram),
                ("Carrot", 200m, UnitOfMeasure.Gram),
                ("Celery", 100m, UnitOfMeasure.Gram),
                ("Chopped tomatoes", 2m, UnitOfMeasure.Can),
                ("Garlic", 2m, UnitOfMeasure.Clove),
                ("Olive oil", 2m, UnitOfMeasure.Tablespoon),
                ("Oregano", 1m, UnitOfMeasure.Teaspoon)
            },
            new[]
            {
                "Soften the finely chopped onion, carrot and celery in olive oil for 10 minutes.",
                "Add the beef and brown well.",
                "Stir in the garlic, tomatoes and oregano.",
                "Simmer gently for 30 minutes."
            }),

        new("Yogurt and fruit breakfast bowl",
            "Two minutes, no cooking, and it keeps everyone going until lunch.",
            MealType.Breakfast, DietType.Vegetarian, ProteinType.Dairy,
            5, 0, 12, false, false, true, true, false, false, 1.00m,
            new[] { "quick", "kid-friendly", "high-protein", "vegetarian" },
            new[]
            {
                ("Greek yogurt", 500m, UnitOfMeasure.Gram),
                ("Lemon", 1m, UnitOfMeasure.Piece),
                ("Mint", 0.25m, UnitOfMeasure.Bunch)
            },
            new[]
            {
                "Spoon the yogurt into bowls.",
                "Add whatever fruit is in season.",
                "Finish with a little lemon zest and torn mint."
            })
    };

    private async Task EnsureRecipesAsync(
        User admin,
        Dictionary<string, Ingredient> ingredients,
        Dictionary<string, Tag> tags)
    {
        var existingTitles = await _db.Recipes
            .Select(r => r.Title)
            .ToListAsync();

        var known = existingTitles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var created = 0;

        foreach (var spec in RecipeCatalog)
        {
            if (known.Contains(spec.Title))
                continue;

            var recipe = new Recipe
            {
                Title = spec.Title,
                Slug = await Slug.UniqueAsync(spec.Title, s => _db.Recipes.AnyAsync(r => r.Slug == s)),
                Description = spec.Description,
                Instructions = string.Join("\n", spec.Steps),
                MealType = spec.MealType,
                Cuisine = CuisineType.Mediterranean,
                Difficulty = DifficultyLevel.Easy,
                DietType = spec.Diet,
                PrimaryProtein = spec.Protein,
                PrepTimeMinutes = spec.Prep,
                CookTimeMinutes = spec.Cook,
                Servings = 4,
                MinAgeMonths = spec.MinAgeMonths,
                IsFreezerFriendly = spec.Freezer,
                IsLunchboxFriendly = spec.Lunchbox,
                IsKidFriendly = spec.KidFriendly,
                IsHighProtein = spec.HighProtein,
                IsIronRich = spec.IronRich,
                IsBatchFriendly = spec.Batch,
                EstimatedCostPerServing = spec.Cost,
                IsPublished = true,
                PublishedAtUtc = DateTime.UtcNow,
                CreatedByAdminId = admin.Id
            };

            var sortOrder = 0;
            foreach (var (name, quantity, unit) in spec.Ingredients)
            {
                if (!ingredients.TryGetValue(name, out var ingredient))
                {
                    _logger.LogWarning("Seed recipe {Title} references unknown ingredient {Ingredient}", spec.Title, name);
                    continue;
                }

                recipe.RecipeIngredients.Add(new RecipeIngredient
                {
                    Ingredient = ingredient,
                    Quantity = quantity,
                    Unit = unit,
                    SortOrder = sortOrder++
                });
            }

            foreach (var tagName in spec.Tags)
            {
                if (tags.TryGetValue(tagName, out var tag))
                    recipe.RecipeTags.Add(new RecipeTag { Tag = tag });
            }

            _db.Recipes.Add(recipe);
            created++;
        }

        if (created > 0)
        {
            await _db.SaveChangesAsync();
            _logger.LogInformation("Seeded {Count} recipes", created);
        }
    }

    // -----------------------------------------------------------------------
    // Blog
    // -----------------------------------------------------------------------

    private async Task EnsureBlogAsync(User admin)
    {
        if (await _db.BlogPosts.AnyAsync())
            return;

        var posts = new[]
        {
            new BlogPost
            {
                Title = "How to plan a Mediterranean week in ten minutes",
                Excerpt = "Pick your cooking days, set your time limit, and let the planner do the rest.",
                Content = string.Join("\n\n", new[]
                {
                    "Most weeknight stress comes from deciding, not cooking.",
                    "Start by telling Gialora who eats with you and how much time you actually have on a Tuesday. Five dinners at thirty minutes is a far more honest plan than seven at ninety.",
                    "Generate the week, swap the one meal you are not in the mood for, and send the shopping list to your phone. That is the whole routine."
                }),
                AuthorId = admin.Id,
                IsPublished = true,
                PublishedAtUtc = DateTime.UtcNow
            },
            new BlogPost
            {
                Title = "Cook once, eat three times: batch cooking basics",
                Excerpt = "A ragu, a pilaf and a lentil soup will carry you through the busiest week.",
                Content = string.Join("\n\n", new[]
                {
                    "Batch cooking is not about spending your Sunday in the kitchen.",
                    "It is about choosing two or three recipes that genuinely improve overnight — stews, ragus, pilafs and soups — and doubling them.",
                    "Freeze in portions the size you actually serve, label them with the date, and your Thursday is already solved."
                }),
                AuthorId = admin.Id,
                IsPublished = true,
                PublishedAtUtc = DateTime.UtcNow.AddDays(-3)
            }
        };

        foreach (var post in posts)
            post.Slug = Slug.From(post.Title);

        _db.BlogPosts.AddRange(posts);
        await _db.SaveChangesAsync();
    }
}
