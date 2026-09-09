# Gialora — Technical Documentation

Mediterranean meal planning for real families.

Version 1.0 · September 2026

---

## 1. What Gialora is

Gialora is two products sharing one backend.

**Product 1 — Recipe discovery.** A browsable, filterable catalogue of structured Mediterranean recipes.

**Product 2 — Personal meal planner.** The more valuable of the two: *"Tell me about your family and I'll organise your week."*

The differentiation is deliberately not "we have good Mediterranean recipes" — there are thousands of recipe websites. It is *"we help busy families decide what to cook, organise the week and shop for it."* Every design decision below follows from that.

### The core user journey

User → Family profile → Preferences → Weekly plan → Automatic grocery list → Cooking → Feedback → Better future plans

1. The user creates an account.
2. They add family members: adults, children with ages, dietary restrictions, allergies, and optional goals such as "more iron" or "picky eater".
3. They select preferences: cuisine, maximum cooking time, number of cooking days, disliked ingredients, budget.
4. The app generates a daily, weekly, biweekly or batch-cook meal plan.
5. The user can accept it, replace one meal, replace an entire day, or regenerate the week.
6. The app builds a consolidated shopping list from the recipes and quantities.
7. The user checks off ingredients while shopping.
8. After cooking, the user can leave feedback — liked, disliked, too difficult, kids didn't eat it.
9. The system uses that feedback to improve subsequent recommendations.

### Site structure

| Section | Purpose |
| --- | --- |
| Home | "What do you need today?" — the three entry points |
| Daily | What should I cook today? |
| Weekly | Plan my week |
| Batch & freeze | Prepare more, cook less |
| Recipes | Browse all recipes |
| Shopping list | Consolidated grocery lists |
| Blog | Tips, guides and Mediterranean inspiration |

Authenticated users additionally get **Saved** (favourites) and **My family**. Administrators get an **Admin** area.

---

## 2. Solution architecture

The solution is five projects targeting .NET 9.

| Project | Type | Responsibility |
| --- | --- | --- |
| Gialora.Shared | Class library | DTOs and domain enums. No dependencies at all. |
| Gialora.Data | Class library | EF Core entities, DbContext, migrations. |
| Gialora.Application | Class library | Business logic: services, the planning engine, seeding. |
| Gialora.Api | ASP.NET Core Web API | HTTP surface, authentication, error translation. |
| Gialora.Client | Blazor WebAssembly | The user interface. |
| Gialora.Tests | xUnit | Unit tests for the engine, the unit maths and the client auth flow. |

### Dependency direction

```
Gialora.Client  ──────────────┐
                              ▼
Gialora.Api ──► Gialora.Application ──► Gialora.Data ──► Gialora.Shared
                                                              ▲
                                                              │
Gialora.Client ───────────────────────────────────────────────┘
```

Two rules matter here.

**The client references only Gialora.Shared.** It never sees EF Core. This is not stylistic: a Blazor WebAssembly application downloads every referenced assembly into the browser. An earlier version of this project had `Microsoft.EntityFrameworkCore.SqlServer` reachable from the client, which pulled the entire ORM into the browser payload for a database the browser can never open.

**Domain enums live in Gialora.Shared, not Gialora.Data.** DTOs need them and so do entities. Putting them in Shared lets both sides use the same definitions without the client taking a dependency on the data layer. It also means the Blazor UI can call `Enum.GetValues<MealType>()` to populate a dropdown with no network round-trip and full compile-time safety.

### Request flow

A typical authenticated request travels:

```
Blazor page
   → typed API client (RecipeApi / PlannerApi / AdminApi …)
      → ApiClientBase          — JSON options, error translation
         → HttpClient + AuthHeaderHandler   — attaches the bearer token
            ── HTTPS ──►
            ExceptionHandlingMiddleware     — typed exceptions → HTTP status
               → Controller                 — authorisation, model binding
                  → Application service     — business rules, ownership checks
                     → GialoraDbContext     — soft-delete filters, timestamps
                        → SQL Server
```

---

## 3. Data model

The database structure matters more than the visual design: if the architecture is right, features can be added later without rebuilding everything.

### 3.1 BaseEntity

Every persisted entity except the pure join tables derives from `BaseEntity`.

| Field | Type | Notes |
| --- | --- | --- |
| Id | Guid | Generated client-side (`Guid.NewGuid()`) so object graphs can be wired up before saving. |
| CreatedAtUtc | DateTime | Set on construction. |
| UpdatedAtUtc | DateTime? | Set automatically — see below. |
| IsDeleted | bool | Soft delete. |

`GialoraDbContext.SaveChangesAsync` overrides the base implementation and stamps `UpdatedAtUtc` on every entity in the `Modified` state. Doing this centrally removes a class of bug where an individual service forgets to set it.

### 3.2 Users, families and members

**User** — Email (unique, 256 chars), PasswordHash, DisplayName, EmailConfirmed, Role (`User` or `Admin`), FailedLoginAttempts, LockoutEndUtc, optional FamilyId.

**Family** — the household, and the complete input to the planning engine alongside its members.

| Field | Purpose |
| --- | --- |
| Name | Display name, defaulted to "*DisplayName*'s Family". |
| PreferredCuisine | Scoring preference, not a hard filter. |
| MaxCookingTimeMinutes | Hard filter: prep + cook must not exceed this. |
| CookingDaysPerWeek | Default number of days a generated week covers. |
| ServingsPerMeal | Drives quantity scaling throughout. |
| Budget | `Any`, `Low`, `Medium`, `High`. |
| DietPreference | Optional household-wide diet. |
| ExcludedProteins | Stored as a pipe-delimited list of enum **names**. This is where "no fish" lives. |
| DislikedIngredients | Pipe-delimited, normalised to lowercase. |
| PreferFreezerFriendly | Scoring preference. |

Excluded proteins are persisted by name rather than by numeric value so the database stays readable and so reordering the `ProteinType` enum cannot silently corrupt existing rows.

**FamilyMember** — Name, MemberType (`Adult` / `Child`), Age, AgeMonths, DietaryRestrictions, Allergies, Goals. `EffectiveAgeMonths` is a computed property (`AgeMonths ?? Age * 12`) mapped as `Ignore` — it exists for the engine, not for the database.

### 3.3 Recipes as data, not articles

This is the single most important design decision in the project. A recipe is not a title plus a blob of text; it is structured data. Once it is structured, a great deal can be automated: serving calculations, shopping list consolidation, filtering by age, filtering by allergy, dietary restrictions, cooking time and categorisation.

**Recipe**

| Group | Fields |
| --- | --- |
| Identity | Title, Slug (unique, URL-friendly), Description, Instructions, ImageUrl |
| Classification | MealType, Cuisine, Difficulty, DietType, PrimaryProtein |
| Time & yield | PrepTimeMinutes, CookTimeMinutes, Servings |
| Age suitability | MinAgeMonths (0 = suitable for everyone) |
| Characteristics | IsFreezerFriendly, IsLunchboxFriendly, IsKidFriendly, IsHighProtein, IsIronRich, IsBatchFriendly |
| Cost | EstimatedCostPerServing |
| Publishing | IsPublished, PublishedAtUtc, CreatedByAdminId |

`TotalTimeMinutes` is a computed property (`Prep + Cook`) mapped as `Ignore`; filtering by total time is expressed in SQL as `PrepTimeMinutes + CookTimeMinutes <= x`.

`PrimaryProtein` exists specifically to support the planning rule *"don't repeat the same protein two days in a row"*. Without a first-class field, that rule would require guessing from ingredient names.

**Ingredient** — Name (unique), DefaultUnit, Category, seven allergen flags (gluten, dairy, nuts, egg, fish, shellfish, soy) and IsPantryStaple.

The `Category` is the supermarket aisle. It is what allows the shopping list to be grouped the way people actually shop, rather than grouped by recipe.

**RecipeIngredient** — the join between a recipe and an ingredient, with a composite primary key of (RecipeId, IngredientId).

| Field | Notes |
| --- | --- |
| Quantity | `decimal(10,3)` |
| Unit | Stored **here**, not on the Ingredient |
| Note | Free text: "finely chopped", "to taste" |
| IsOptional | Optional items are flagged separately on the shopping list |
| SortOrder | Display order on the recipe page |

The unit belongs to the relationship, not to the ingredient, because the same onion is 100 g in one recipe and 1 piece in another.

**Tag / RecipeTag** — Tags carry a normalised lowercase `Name` (the slug, e.g. `high-protein`) and a `DisplayName` for the UI. Tags are created on demand when a recipe references one that does not exist yet.

### 3.4 Meal plans

**MealPlan** — FamilyId, Name, PlanType (`Daily`, `Weekly`, `Biweekly`, `BatchAndFreeze`), Status (`Draft`, `Accepted`, `Archived`), WeekStartDate, ServingsPerMeal. Each plan has at most one shopping list.

**MealPlanDay** — a date within the plan.

**MealPlanEntry** — MealType, RecipeId, PlannedServings, **IsLocked**, SortOrder.

`IsLocked` is what makes "regenerate the week" safe. A locked meal is never replaced, so the Friday pasta the family insists on survives every regeneration. Swapping a meal by hand locks it automatically — a deliberate choice made by the user should not be undone by the next regeneration.

### 3.5 Shopping lists

**ShoppingList** — FamilyId, optional MealPlanId, Name, Status.

**ShoppingListItem** — optional IngredientId (null means a hand-added line such as "kitchen roll"), DisplayName, Quantity, Unit, Category, IsChecked, IsOptional, SourceRecipes, SortOrder.

`SourceRecipes` records which recipes contributed to the line. This is the visible trace of consolidation: the user sees *"Onion — 825 g · for Chicken meatballs, Chickpea stew, Lamb pilaf, Red lentil soup"* and understands where the number came from.

### 3.6 Feedback and favourites

**Feedback** — Rating (0–5, where 0 means "not rated, only flags given"), Reaction (`None` / `Liked` / `Disliked`), TooDifficult, KidsDidNotEat, TookTooLong, Comment. A unique index on (RecipeId, UserId) enforces one feedback record per user per recipe; the service upserts.

**FavoriteRecipe** — a pure join table with composite key (UserId, RecipeId). Deleted hard rather than soft; a join table needs no audit trail.

**BlogPost** — Title, Slug (unique), Excerpt, Content, CoverImageUrl, IsPublished, PublishedAtUtc, AuthorId.

### 3.7 Cross-cutting persistence rules

**Soft delete.** Every `BaseEntity` type has a global query filter of `!IsDeleted`. Deletes set the flag rather than removing rows.

**Join tables inherit their ends' filters.** `RecipeIngredient`, `RecipeTag` and `FavoriteRecipe` have no `IsDeleted` of their own, so they carry filters referencing both ends — for example `!ri.Recipe.IsDeleted && !ri.Ingredient.IsDeleted`. Without this, a soft-deleted ingredient would still appear inside recipes and shopping lists, and EF Core emits a model-validation warning.

**List columns.** `List<string>` properties are stored as pipe-delimited strings via a `ValueConverter`. Each one also gets a `ValueComparer`; without it EF Core cannot detect a change *inside* the list and `SaveChanges` silently writes nothing.

**Delete behaviours.**

| Relationship | Behaviour | Reason |
| --- | --- | --- |
| Recipe → CreatedByAdmin | Restrict | An admin can only be deleted once their recipes are reassigned. |
| RecipeIngredient → Ingredient | Restrict | Prevents orphaned recipe lines. |
| MealPlanEntry → Recipe | Restrict | A recipe in use cannot vanish from a plan. |
| Feedback → User | Restrict | Preserves rating history. |
| ShoppingList → MealPlan | ClientCascade | SQL Server refuses two cascade paths from Family. |

**Indexes.** Unique on `User.Email`, `Recipe.Slug`, `Ingredient.Name`, `Tag.Name`, `BlogPost.Slug` and `Feedback (RecipeId, UserId)`. Composite non-unique on `Recipe (IsPublished, MealType, DietType)` — the combination the catalogue and the planner filter on most — and on `MealPlan (FamilyId, WeekStartDate)`.

### 3.8 Migrations

| Migration | Contents |
| --- | --- |
| `initialcreate` | First schema. |
| `AddUserRoleAndRecipeAdmin` | User roles and recipe authorship. |
| `ExpandDomainForMealPlanning` | The full domain above: recipe classification fields, per-line units, family preferences, shopping lists, favourites, blog posts. |

The third migration contains **two hand-written backfill statements** that scaffolding cannot infer. `Recipe.Slug` is added as a non-nullable column with a default of `""` and then given a unique index — on any database holding two or more existing recipes, that index would fail. The backfill derives a slug from the title and appends the first eight characters of the row's Id to guarantee uniqueness. A second statement copies `Tag.Name` into the new non-nullable `Tag.DisplayName`.
---

## 4. The meal planning engine

The engine is deliberately **rules-based, not AI**. The brief is explicit on this point: build a rules-based recommendation engine first. Everything here is deterministic apart from one small random term, and every decision is explainable to the user.

It lives in `Gialora.Application/Planning/` and is a pure class with no database access. `MealPlanService` prepares its input and persists its output; the engine itself can be unit-tested with plain objects.

### 4.1 Inputs

**PlanningCandidate** — a flattened recipe: identity, classification, total minutes, minimum age, characteristic flags, cost, the set of ingredient Ids, ingredient names, tags, a vegetable count, aggregate allergen flags, and a `HistoryScore`.

**PlanningConstraints** — the family's rules: maximum cooking time, diet preference, preferred cuisine, budget, freezer preference, excluded proteins, disliked ingredients, the union of all members' allergies, the youngest child's age in months, and whether there are children at all.

**PlanningRequest** — the constraints plus the number of slots to fill, the plan type, whether to optimise for ingredient reuse, the set of recipe Ids already used (locked meals), and a random seed.

### 4.2 Step one — hard filters

A candidate that fails any of these is removed from consideration entirely. It is never rescued by a good score.

| Rule | Behaviour |
| --- | --- |
| Cooking time | `TotalMinutes > MaxCookingTimeMinutes` → excluded. |
| Diet | A vegetarian household accepts vegetarian and vegan recipes; a vegan household accepts only vegan; a pescatarian household accepts anything that is not meat. |
| Excluded proteins | The "no fish" rule. Direct match on `PrimaryProtein`. |
| Age | If the youngest child is 24 months, a recipe with `MinAgeMonths = 36` is excluded. |
| Allergies | Known allergens map to the ingredient flags. Unknown allergy strings fall back to matching against ingredient names. |
| Disliked ingredients | Substring match against ingredient names; the whole recipe is excluded. |
| Budget | `Low` allows up to 3 per serving, `Medium` up to 6, `High` and `Any` are unlimited. Recipes with no cost recorded are never excluded on price. |

Two of these deserve comment.

**Allergies are the strictest rule in the system.** They are never softened into a score. For an allergy string the engine does not recognise, it falls back to matching the text against ingredient names — deliberately over-cautious. Excluding a few extra recipes is an inconvenience; suggesting a meal that sends a child to hospital is not.

**Allergies are unioned across the household.** One member's nut allergy is the whole family's constraint, because the family eats together.

### 4.3 Step two — soft scoring

Surviving candidates are scored and the best is taken for each slot in turn. The weights are constants at the top of the class so they can be tuned in one line.

| Signal | Weight | Rule |
| --- | --- | --- |
| Same protein as the previous day | −6.0 | *"Don't repeat the same protein two days in a row."* |
| Protein already used twice | −2.5 each | Variety across the whole week. |
| Shared ingredient with an already-chosen meal | +0.9 each, capped at +4.0 | *"Use ingredients already selected elsewhere in the week."* |
| Family history | ×3.0 | Recipes the family liked come first. |
| Vegetable-rich, quota not yet met | +2.5 | *"Include vegetables at least X times."* |
| Legume-based, quota not yet met | +3.0 | *"Include legumes X times per week."* |
| Freezer-friendly, if the family prefers it | +1.5 | |
| Kid-friendly, if the household has children | +1.5 | |
| Matches the preferred cuisine | +1.0 | |
| Under 30 minutes, in the first two slots | +0.8 | Weeknights are the tightest. |
| Random jitter | 0 … +1.2 | |

The vegetable target is 60% of slots (rounded up); the legume target is one meal per five slots. A recipe counts as vegetable-rich when it contains at least two ingredients in the *Vegetables* or *Fruit* categories, and as legume-based when its primary protein is `Legume` or it carries a `legumes`, `beans` or `lentils` tag.

**Why the random jitter exists.** Without it, "regenerate the week" is a pure function of the same inputs and returns exactly the same plan every time — the button would appear broken. The jitter is small enough that it never overrides a real signal, and the seed is stored per request so a single generation is internally consistent.

### 4.4 Step three — greedy selection

For each slot in order:

1. Take all candidates not already selected.
2. Score each against the current state — the previous day's meal, the ingredients used so far, the protein tally, and how far the vegetable and legume quotas still are from being met.
3. Take the highest scorer, add it to the plan, and update that state.

Selection proceeds in date order so that "same protein as the previous day" is meaningful.

**The engine never repeats a meal to fill a slot.** If the filtered pool runs out, it stops early, marks the result partial, and returns the note *"Only 3 of 5 meals could be planned — there aren't enough matching recipes yet."* Repeating a recipe would look like a working plan while quietly being worse than the honest answer.

### 4.5 Batch & freeze

For a `BatchAndFreeze` plan the pool is narrowed to recipes that are batch-friendly or freezer-friendly, and a note records that. If no such recipes exist yet the engine falls back to the full pool and says so, rather than returning nothing.

### 4.6 Planning notes

Every result carries human-readable notes, shown on the plan page under "Why this plan":

- *4 different protein sources across 5 meals.*
- *2 vegetable-rich meals.*
- *2 legume-based meal(s) included.*
- *Shared ingredients across meals: 12 repeats over 23 distinct items — fewer things to buy.*
- *4 recipe(s) can be frozen for later.*

These exist because a plan the user does not understand is a plan they will not trust. They are **persisted on the meal plan**, not just returned from the generate call — the launcher navigates to the plan page, which re-reads the plan, so notes held only in the generate response would never be seen. Regenerating replaces them, so the explanation always describes the plan currently on screen.

### 4.7 Learning from feedback

`MealPlanService.LoadHistoryScoresAsync` reads every feedback record and favourite belonging to any user in the family and reduces them to one number per recipe.

| Signal | Delta |
| --- | --- |
| 👍 Liked | +1.5 |
| 👎 Disliked | −2.5 |
| Rating 4–5 | +1.0 |
| Rating 1–2 | −1.5 |
| Kids didn't eat it | −2.0 |
| Too difficult | −1.0 |
| Took too long | −0.75 |
| Favourited | +1.0 |

That total becomes `HistoryScore`, multiplied by 3.0 in the scorer. "Kids didn't eat it" is the strongest negative signal available short of an outright dislike — it is the most common reason a meal genuinely fails in a family household.

This is the "the system learns" step. It is a weighted sum, not machine learning, and that is the correct amount of machinery for a first version.

### 4.8 Replacing a single meal

`PickReplacement` runs the same hard filters, then excludes every recipe currently in the plan *and* the one being replaced, and scores the remainder against the rest of the week. If nothing qualifies it returns null and the API responds with *"No other recipe matches your preferences for this slot. Try widening your filters."*

With a small catalogue this is a realistic outcome rather than an error. A family that excludes fish, has a nut allergy and caps meals at 45 minutes may have only five qualifying dinners; if all five are already in the plan, there is genuinely nothing to swap to.

### 4.9 Regeneration

`RegenerateAsync` collects every unlocked entry, asks the engine to fill exactly that many slots while treating locked recipes as already used, and writes the results back in date order. `RegenerateDayAsync` does the same for one day. If every meal in the plan is locked, the API returns a 400 explaining that rather than silently doing nothing.

---

## 5. Shopping list consolidation

This is the feature that makes the app more valuable than a recipe website. Three recipes needing 500 g, 750 g and 450 g of chicken should produce one line reading **1.7 kg**, not three lines the user has to add up in the supermarket.

### 5.1 The algorithm

For every entry in the plan, for every ingredient in its recipe:

1. **Scale.** `UnitConverter.Scale` converts the recipe's quantity from the recipe's own serving count to the plan's planned servings.
2. **Bucket.** The line is assigned to a bucket keyed by ingredient *and* unit family.
3. **Accumulate.** The scaled quantity is converted to a base unit and added to the bucket. The contributing recipe title is recorded.
4. **Render.** Each bucket's base total is converted back to the most readable unit.

### 5.2 Unit families

Units belong to one of four families, and quantities are only ever summed within a family.

| Family | Units | Base |
| --- | --- | --- |
| Mass | Gram, Kilogram | gram |
| Volume | Millilitre, Litre, Teaspoon, Tablespoon, Cup | millilitre |
| Count | Piece, Clove, Slice, Can, Pack, Bunch | itself |
| Loose | Pinch | itself |

Mass and volume consolidate across the whole family, so 1 kg + 500 g becomes 1.5 kg. Count and loose units consolidate only with the *same* unit — 2 cloves of garlic and 5 g of garlic stay as two separate lines, because adding them would be arithmetically meaningless.

### 5.3 Display rules

Base totals are rendered back into whichever unit reads best:

- Mass: ≥ 1000 g → kilograms, otherwise grams.
- Volume measured in **spoons** returns to spoons: ≥ 240 ml → cups, ≥ 15 ml → tablespoons, otherwise teaspoons.
- Volume measured in millilitres or litres: ≥ 1000 ml → litres, otherwise millilitres.
- Count and loose units keep their own unit.

The spoon rule was added after testing produced *"Salt — 7.5 ml"*. That is arithmetically correct and completely useless on a shopping list; nobody reads 7.5 ml as "a teaspoon and a half". Summing still happens through the common millilitre base, so olive oil used across five recipes still merges into one line — it is only the *display* that returns to the unit the recipes were written in.

### 5.4 Whole units

Piece, Can, Pack, Slice, Bunch and Clove cannot be fractional on a shopping list. Scaling a recipe from 4 to 6 servings turns 1 egg into 1.5, which is rounded **up** to 2. You cannot buy half an egg, and rounding down would leave the cook short.

### 5.5 Pantry staples

Ingredients flagged `IsPantryStaple` — salt, pepper, olive oil, dried herbs — are marked on the list so the UI can show them as *"check cupboard"* rather than as things to buy. A list that tells a parent to buy 1.5 teaspoons of salt trains them to ignore the list.

### 5.6 Regeneration preserves progress

Regenerating a shopping list from a changed plan is a common action, and losing the user's progress mid-shop would be unacceptable. The service therefore:

- records which ingredients are already ticked and re-applies those ticks to the newly generated lines;
- keeps every hand-added item (those with no `IngredientId`);
- hard-deletes only the previously generated lines, which are derived data.

### 5.7 An EF Core pitfall worth documenting

`BaseEntity` assigns `Id` in its property initialiser, so a new entity already has a non-default primary key before it is saved. When such an object is added to a **tracked** parent's navigation collection, EF Core's change detector reads the populated key and concludes the row already exists — it emits an `UPDATE` instead of an `INSERT`, which affects zero rows and throws `DbUpdateConcurrencyException`.

The fix, applied in three places (shopping list items, recipe update, bulk import), is to set the foreign key explicitly and call `DbSet.Add` / `AddRange` directly rather than adding through the navigation property. The same rule applies to removal: call `RemoveRange` and leave the collection alone, because clearing the collection as well makes EF generate both a delete and an orphan update for the same rows.

---

## 6. Application services

All services live in `Gialora.Application/Services/` and are registered as scoped. They own the business rules; controllers only handle HTTP concerns.

### 6.1 AuthService

**Registration** normalises the email to lowercase, hashes the password with BCrypt at work factor 12, and always assigns the `User` role — self-registration can never create an administrator. A duplicate email is caught both by an explicit pre-check and by a `DbUpdateException` handler, so two simultaneous registrations produce a clean 409 rather than a 500.

**Login** is written to leak nothing:

- An unknown email still runs a BCrypt verification against a fixed dummy hash, so the response time does not reveal whether the account exists.
- Both "no such user" and "wrong password" return the same generic message.
- Five consecutive failures lock the account for 15 minutes.
- When a lockout expires the failure counter is reset. Without this, the counter stays at five and the very next mistake re-locks the account immediately.

### 6.2 FamilyService

`GetOrCreateFamilyAsync` creates a family on first access, so a user who reaches the planner via a deep link without visiting the family page still works. `UpdatePreferencesAsync` clamps every numeric preference into a sane range and stores excluded proteins by enum name.

Every member operation checks that the member belongs to the calling user's family. A member that exists but belongs to someone else returns 404, not 403 — revealing that an id exists is itself a small information leak.

### 6.3 RecipeService

One `BuildFilteredQuery` method builds the query for both the public catalogue and the admin catalogue, differing only in the published filter. Search matches title, description **and ingredient names**, so "chickpea" finds recipes that never mention it in the title.

Tag filters are applied with AND semantics — a recipe must carry every selected tag. Excluded ingredients remove the recipe entirely.

On update, the join rows are replaced wholesale rather than diffed. Diffing join tables produces more bugs than it saves queries. The slug is only regenerated when the title actually changes, so existing links are not broken by a typo fix.

`BuildIngredients` de-duplicates the incoming list, because the composite primary key (RecipeId, IngredientId) makes a repeated ingredient a hard database error rather than a harmless duplicate.

### 6.4 IngredientService

Standard CRUD, plus one guard: an ingredient used by any recipe cannot be deleted, and the attempt returns 409 with the usage count. The client disables the button in that case, so the error is a backstop rather than the primary defence.

### 6.5 MealPlanService

The bridge between the engine and the database. It loads candidates, builds constraints from the family, computes history scores, calls the engine, and persists the result. It contains no planning rules of its own — those all live in the engine.

Every read and write resolves the caller's family first and filters by it. Knowing a plan's Guid is not sufficient to read or modify it.

### 6.6 ShoppingListService, FeedbackService, FavoriteService, BlogService

`ShoppingListService` owns consolidation (section 5). `FeedbackService` upserts, so a user editing their rating updates the existing row; a concurrent duplicate is caught by the unique index and resolved by reading the winning row. `FavoriteService` is idempotent — favouriting twice is not an error. `BlogService` stamps `PublishedAtUtc` only on first publication, so later edits do not reorder the blog.

### 6.7 DbSeeder

Idempotent seeding, safe to run on every startup. It creates an administrator, 49 ingredients with correct categories and allergen flags, 12 tags, 12 published Mediterranean recipes and 2 blog posts.

The recipes are chosen to exercise the engine: several proteins, two legume dishes, freezer-friendly and batch-friendly options, a range of cooking times and minimum ages. Without seed data the planner has nothing to select from and a fresh installation looks broken.
---

## 7. HTTP API reference

All endpoints are prefixed `/api`. "Anonymous" means no token is required; "User" means any signed-in account; "Admin" means the `Admin` role.

### Authentication

| Method | Route | Access | Purpose |
| --- | --- | --- | --- |
| POST | `/auth/register` | Anonymous | Create an account, returns a token. |
| POST | `/auth/login` | Anonymous | Sign in, returns a token. |
| GET | `/auth/me` | User | Validate a token, return the current user. |

Both anonymous routes sit behind a fixed-window rate limiter of 10 requests per minute per IP address.

### Family

| Method | Route | Access |
| --- | --- | --- |
| GET | `/family/me` | User |
| PUT | `/family/me` | User |
| PUT | `/family/me/preferences` | User |
| POST | `/family/members` | User |
| PUT | `/family/members/{memberId}` | User |
| DELETE | `/family/members/{memberId}` | User |

### Recipes

| Method | Route | Access | Purpose |
| --- | --- | --- | --- |
| GET | `/recipes` | Anonymous | Paged catalogue with the full filter set. |
| GET | `/recipes/{id}` | Anonymous | Published recipe by id. |
| GET | `/recipes/by-slug/{slug}` | Anonymous | Published recipe by slug (the SEO route). |
| GET | `/recipes/{id}/ratings` | Anonymous | Aggregate ratings, plus the caller's own if signed in. |
| GET | `/recipes/admin` | Admin | Catalogue including drafts. |
| GET | `/recipes/admin/{id}` | Admin | Any recipe, published or not. |
| POST | `/recipes` | Admin | Create as draft. |
| PUT | `/recipes/{id}` | Admin | Update, including publish state. |
| POST | `/recipes/{id}/publish` | Admin | |
| POST | `/recipes/{id}/unpublish` | Admin | |
| DELETE | `/recipes/{id}` | Admin | Soft delete and unpublish. |

`GET /recipes` accepts: `Search`, `MealType`, `Cuisine`, `DietType`, `Difficulty`, `MaxTotalMinutes`, `SuitableForAgeMonths`, `FreezerFriendly`, `LunchboxFriendly`, `KidFriendly`, `HighProtein`, `IronRich`, `BatchFriendly`, `Tags` (repeatable), `ExcludeIngredients` (repeatable), `FavoritesOnly`, `SortBy`, `Page`, `PageSize`.

`FavoritesOnly=true` without a token returns 401 rather than silently ignoring the filter.

### Meal plans

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/mealplans` | All plans for the family. |
| GET | `/mealplans/current` | The active plan, or 204. |
| GET | `/mealplans/{planId}` | One plan. |
| POST | `/mealplans/generate` | Generate a plan. |
| POST | `/mealplans/{planId}/entries/{entryId}/swap` | Replace one meal. |
| POST | `/mealplans/{planId}/entries/{entryId}/lock?locked=` | Lock or unlock a meal. |
| POST | `/mealplans/{planId}/days/{dayId}/regenerate` | Replace an entire day. |
| POST | `/mealplans/{planId}/regenerate` | Regenerate, keeping locked meals. |
| POST | `/mealplans/{planId}/accept` | Mark the plan accepted. |
| POST | `/mealplans/{planId}/shopping-list` | Build or rebuild the shopping list. |
| GET | `/mealplans/{planId}/shopping-list` | Fetch it. |
| DELETE | `/mealplans/{planId}` | Soft delete. |

All require a signed-in user and are scoped to that user's family.

### Shopping lists

| Method | Route |
| --- | --- |
| GET | `/shopping-lists` |
| GET | `/shopping-lists/{listId}` |
| PUT | `/shopping-lists/{listId}/items/{itemId}/checked` |
| POST | `/shopping-lists/{listId}/items` |
| DELETE | `/shopping-lists/{listId}/items/{itemId}` |
| POST | `/shopping-lists/{listId}/clear-checked` |
| DELETE | `/shopping-lists/{listId}` |

### Feedback and favourites

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/feedback/mine` | All feedback by the caller. |
| PUT | `/feedback/recipes/{recipeId}` | Upsert feedback. |
| DELETE | `/feedback/recipes/{recipeId}` | Remove it. |
| GET | `/favorites` | The caller's saved recipes. |
| PUT | `/favorites/{recipeId}` | Save (idempotent). |
| DELETE | `/favorites/{recipeId}` | Unsave. |

### Ingredients, tags, blog and metadata

| Method | Route | Access |
| --- | --- | --- |
| GET | `/ingredients`, `/ingredients/{id}` | Anonymous |
| POST, PUT, DELETE | `/ingredients…` | Admin |
| GET | `/tags` | Anonymous |
| GET | `/blog`, `/blog/{slug}` | Anonymous |
| GET, POST, PUT, DELETE | `/blog/admin…`, `/blog…` | Admin |
| GET | `/meta` | Anonymous |
| GET | `/admin/import/template` | Admin |
| POST | `/admin/import/recipes` | Admin |
| POST | `/admin/import/recipes/file` | Admin |

`GET /meta` returns every enum as value/name/display-name triples. The Blazor client does **not** use it — it references `Gialora.Shared` and reads the enums directly. The endpoint exists for consumers written in another language, such as a future native mobile app, so they need not hand-copy the lists.

### Error format

Every non-success response has the same shape:

```json
{
  "message": "No other recipe matches your preferences for this slot.",
  "errors": { "Title": ["The Title field is required."] }
}
```

`ExceptionHandlingMiddleware` maps typed application exceptions to status codes: `NotFoundException` → 404, `ValidationFailedException` → 400, `ConflictException` → 409, `ForbiddenException` → 403. Anything else becomes a 500 with a generic message, with the detail written to the log — a stack trace tells an attacker about the code and schema.

Enums are serialised **by name** in both directions (`"Dinner"`, not `2`). Reordering an enum therefore cannot silently change the meaning of stored or transmitted data.

---

## 8. The Blazor client

### 8.1 Structure

| Folder | Contents |
| --- | --- |
| `Services/` | Typed API clients and JSON configuration. |
| `Components/` | Reusable pieces: `RecipeCard`, `PlanLauncher`, `Loading`, `EmptyState`, `ErrorAlert`. |
| `Layout/` | `MainLayout`, `NavMenu`. |
| `Pages/` | One file per route. |
| `Pages/Admin/` | The admin area. |
| `Auth/` | Token storage and the authentication state provider. |

### 8.2 The API client layer

Pages never touch `HttpClient` directly. Five typed clients sit on a shared `ApiClientBase`:

| Client | Covers |
| --- | --- |
| `AccountApi` | Register, login, logout — and stores the token on success. |
| `RecipeApi` | Catalogue, ingredients, tags, favourites, feedback. |
| `PlannerApi` | Family, meal plans, shopping lists. |
| `ContentApi` | Blog. |
| `AdminApi` | Admin recipe/ingredient/blog CRUD and bulk import. |

`ApiClientBase` centralises three things:

1. **JSON options.** `GialoraJson.Options` adds `JsonStringEnumConverter`. This is not optional: the API sends enums as names, and Blazor's default `ReadFromJsonAsync` cannot deserialise `"Dinner"` into a `MealType`. Without it every recipe response fails.
2. **Error translation.** Non-success responses are parsed into `ApiErrorDto` and thrown as `ApiException` carrying the server's message and validation errors. Pages show that message directly, so the user sees *"No other recipe matches your preferences"* rather than *"Something went wrong"*.
3. **404 as a value.** `GetOrDefaultAsync` returns null for 404 and 204, because "you have no current plan" is a normal state, not an error.

### 8.3 Authentication

The JWT is kept in `localStorage` behind `TokenStore`, which caches it in memory so that every HTTP request does not cross the JS interop boundary. `TokenAuthStateProvider` reads the token, checks the expiry, and projects the claims into a `ClaimsPrincipal`. `AuthHeaderHandler` is a `DelegatingHandler` that attaches the bearer header.

**`TokenStore` must be registered as a singleton, and this is not a style preference.** `IHttpClientFactory` builds its handler chain inside a DI scope of its own (`DefaultHttpClientFactory.CreateHandlerEntry` calls `_scopeFactory.CreateScope()`). A *scoped* token cache therefore gives `AuthHeaderHandler` a different instance from the one the UI uses. That caused a real bug: a user who browsed anonymously and then registered had a handler whose cache still held "no token", so no `Authorization` header was ever sent. The navigation bar showed them signed in while every page underneath reported *"Please sign in to continue."* A singleton is shared by every scope and removes the failure mode entirely. `Gialora.Tests/AuthTokenFlowTests.cs` covers this, including a regression test that reproduces the fault with the old scoped registration.

`AuthHeaderHandler` also clears the token when the API answers 401, so a rejected or expired session cannot leave the UI claiming to be signed in.

Claim names are short (`name`, `role`) and the same constants are used on both sides. `JwtSecurityTokenHandler` silently rewrites `ClaimTypes.Name` and `ClaimTypes.Role` into different names unless told not to, which previously caused `AuthorizeView Roles="Admin"` to always evaluate false.

### 8.4 Pages

| Route | Page | Access |
| --- | --- | --- |
| `/` | Home — the three route cards, plus the current plan when signed in | Anonymous |
| `/daily`, `/weekly`, `/batch` | Plan launchers | User |
| `/plans`, `/plans/{id}` | Plan list and plan detail | User |
| `/shopping-lists`, `/shopping-lists/{id}` | Shopping lists | User |
| `/recipes`, `/recipes/{slug}` | Catalogue and recipe detail | Anonymous |
| `/favorites` | Saved recipes | User |
| `/family` (also `/family-setup`) | Members and preferences | User |
| `/blog`, `/blog/{slug}` | Blog | Anonymous |
| `/login`, `/register`, `/logout` | Account | Anonymous |
| `/admin/recipes`, `/admin/recipes/new`, `/admin/recipes/{id}` | Recipe admin | Admin |
| `/admin/ingredients`, `/admin/import`, `/admin/blog`, `/admin/blog/{id}` | Admin tools | Admin |

Home, Recipes and Blog are public so that a visitor — and a search engine — can see the product before signing up.

### 8.5 Notable interface behaviour

**One launcher, three routes.** Daily, Weekly and Batch & freeze render the same `PlanLauncher` component with different defaults and copy. The launcher also shows the constraints the plan will be built with — time limit, servings, allergies, excluded proteins — so the result is never a surprise, with a link straight to the preferences.

**The servings scaler on a recipe page** recalculates quantities in the browser using the same rounding rule as the server, so the control feels instant.

**Check-off is optimistic.** Ticking an item on the shopping list updates the UI immediately and rolls back if the request fails. Someone standing in a supermarket should not wait for a round-trip per item.

**Filter changes are race-safe.** Rapid filter changes cancel the previous in-flight search so a slow earlier response cannot overwrite a newer one.

**Blog content is rendered as text.** Paragraphs are split and rendered individually rather than passed through `MarkupString`, which would open an XSS path from admin-authored content.

**Empty states always offer the next action** — an empty plan list links to "Plan my week", an empty admin catalogue links to both "New recipe" and "Bulk import".

### 8.6 Design system

`wwwroot/css/app.css` defines the visual language over Bootstrap's grid and form resets: a Mediterranean palette (olive, terracotta, sand, sea), a serif display face for headings against a system sans for body text, and shared component classes (`card-g`, `pill`, `chip-check`, `route-card`, `plan-day`, `shop-item`). Colours and radii are CSS custom properties on `:root`, so the palette can be changed in one place. The scoped stylesheets from the default Blazor template were emptied so there is a single source of styling.

---

## 9. Administration

### 9.1 The admin panel

Administrators can add and edit recipes, upload a photo URL, add ingredients, assign tags, mark age suitability and freezer-friendliness, change quantities, publish and unpublish, manage the ingredient catalogue, and write blog posts.

Recipes are created as **drafts**. A draft is invisible to the public catalogue, invisible to the planner, and returns 404 on the public route — but remains editable through the admin route. Publishing is an explicit act.

### 9.2 Bulk recipe import

Since many recipes already exist in spreadsheets, the import accepts a structured CSV: **one row per ingredient**, with rows sharing the same `recipe` value merged into a single recipe. Recipe-level metadata therefore only needs filling on the first row of each group.

| Column | Meaning |
| --- | --- |
| `recipe` | Recipe name — the grouping key. Required. |
| `description` | Short description. |
| `mealtype`, `cuisine`, `difficulty`, `diettype`, `protein` | Classification; enum names, case-insensitive. |
| `servings`, `preptime`, `cooktime` | Numbers. |
| `minagemonths` | Minimum age in months. |
| `freezer`, `lunchbox`, `kidfriendly`, `highprotein`, `ironrich`, `batch` | `yes` / `y` / `true` / `1` / `x`. |
| `image` | Image URL. |
| `tags` | Pipe- or semicolon-separated. |
| `ingredient`, `quantity`, `unit`, `category`, `note`, `optional` | One ingredient per row. |
| `instructions` | Steps separated by `\|`. |

Import options: create missing ingredients automatically, publish immediately or leave as drafts, and update recipes that already exist rather than skipping them.

**Partial success is a normal outcome.** One malformed recipe does not abort the file. The response reports counts created, updated and skipped, plus per-recipe errors and warnings — a recipe with no instructions is reported by name while everything else imports. The endpoint returns 200, not 400, because most of the work usually succeeded.

The parser is a small purpose-built CSV reader supporting quoted fields, embedded delimiters and doubled quotes. Numeric parsing accepts both `1.5` and `1,5`; the delimiter is configurable because Excel exports semicolons in many regions. Uploads are capped at 5 MB.

A filled-in template is downloadable from the import page.

---

## 10. Security

| Concern | Measure |
| --- | --- |
| Password storage | BCrypt, work factor 12. |
| Brute force — one account | 5 failed attempts locks the account for 15 minutes; the counter resets when the lockout expires. |
| Brute force — many accounts | Fixed-window rate limit of 10 requests/minute per IP on the auth endpoints. |
| Email enumeration | Identical message and comparable response time for unknown email and wrong password, using a dummy BCrypt verification. |
| Privilege escalation | Self-registration always assigns the `User` role. |
| Token integrity | HMAC-SHA256; the signing key is validated to be at least 32 bytes at startup rather than failing on first use. Issuer, audience and lifetime are all validated with zero clock skew. |
| Horizontal access | Every family-scoped read and write resolves the caller's family and filters by it. A plan or list id alone grants nothing. |
| Vertical access | `[Authorize(Roles = "Admin")]` on every administrative route. |
| Information disclosure | Unhandled exceptions return a generic message; detail goes to the log only. A resource belonging to another family returns 404, not 403. |
| XSS | Blog content is rendered as text, never as raw HTML. |
| Payload leakage | The client references only `Gialora.Shared`, so no data-access code reaches the browser. |
| CORS | An explicit origin allow-list, configurable per environment. |
| Open redirect | The post-login return URL accepts relative paths only. |

**Configuration.** The JWT signing key and the connection string come from configuration. `appsettings.Development.json` holds development-only values; production must supply its own through user secrets, environment variables or a secret store. Seeding is **disabled by default** in `appsettings.json` and enabled in Development, so no automatic administrator account is ever created in production.

---

## 11. Running the project

### Prerequisites

- .NET 9 SDK
- SQL Server LocalDB (or any SQL Server instance)
- `dotnet-ef` tools for migrations: `dotnet tool install --global dotnet-ef`

### Configuration

`Gialora.Api/appsettings.Development.json` provides a LocalDB connection string, a development JWT key, the CORS allow-list and seeding settings. To use a different database, change `ConnectionStrings:DefaultConnection`.

### Start

```
dotnet run --project Gialora.Api    --launch-profile https
dotnet run --project Gialora.Client --launch-profile https
```

| Component | HTTPS | HTTP |
| --- | --- | --- |
| API | https://localhost:5001 | http://localhost:5000 |
| Client | https://localhost:7280 | http://localhost:5056 |
| Swagger | https://localhost:5001/swagger | |

The API applies any pending migrations on startup and then seeds, so a fresh clone needs no manual database setup.

### Seeded administrator

```
admin@gialora.local  /  ChangeMe123!
```

Development only. Change it before any deployment.

### Migrations

```
dotnet ef migrations add <Name> -p Gialora.Data -s Gialora.Api
dotnet ef database update      -p Gialora.Data -s Gialora.Api
```

---

## 12. Current limitations and next steps

### Known limitations

**A small catalogue produces partial plans.** With 12 seeded recipes and strict preferences, a request for five dinners can legitimately return three. The system reports this honestly rather than repeating meals. Importing a real recipe library resolves it.

**Swapping can run out of options.** For the same reason, "Swap" may return *"No other recipe matches your preferences for this slot."* This is correct behaviour, not a fault.

**Images are URLs, not uploads.** There is no file storage; `ImageUrl` points at an externally hosted image.

**Test coverage is partial.** `Gialora.Tests` covers the planning engine, the unit arithmetic behind the shopping list, and the client's authentication token flow — 39 tests in total. The services and controllers have no integration tests yet; those are the next thing worth adding.

**Nutrition is characteristic flags, not data.** `IsHighProtein` and `IsIronRich` are booleans set by an administrator, not computed from nutritional values.

### Deliberately deferred

The brief separates automation from AI, and this build implements only the automation. Rules and the database handle serving calculations, ingredient quantities, shopping list consolidation, filtering by age, allergies, dietary restrictions and cooking time, recipe categorisation, meal frequency, ingredient duplication and meal-plan structure.

The following are recognised as future work and are **not** built:

- Personalisation from free-text feedback ("my daughter didn't like the lentil soup but loved the meatballs").
- Recipe transformation ("make this vegetarian", "make this suitable for my 2-year-old").
- Ingredient substitution ("I don't have zucchini").
- Natural-language planning ("I have chicken, potatoes, carrots and yogurt — what can I make tonight?").
- AI-assisted content generation for descriptions, SEO text, tags and translations.

Also deferred, in line with the MVP boundary: nutrition tracking, grocery delivery integration, supermarket price comparison, budget optimisation, barcode scanning, pantry management, a voice assistant, community features and subscriptions.

### Suggested next steps

1. Integration tests over the services and controllers, on top of the existing unit tests.
2. Image upload with a storage provider.
3. Import the real recipe library so plans stop coming back partial.
4. Server-side rendering or prerendering for the public recipe and blog pages, for SEO.
5. Structured logging and health checks before deployment.

---

## Appendix A — Domain enumerations

| Enum | Values |
| --- | --- |
| MealType | Breakfast, Lunch, Dinner, Snack |
| CuisineType | Mediterranean, Greek, Italian, Spanish, MiddleEastern, NorthAfrican, Levantine, Other |
| DifficultyLevel | Easy, Medium, Hard |
| DietType | Meat, Fish, Vegetarian, Vegan |
| ProteinType | None, Chicken, Beef, Pork, Lamb, Fish, Seafood, Egg, Legume, Dairy, Tofu |
| IngredientCategory | Vegetables, Fruit, Meat, Fish, DairyAndEggs, Bakery, Grains, Legumes, HerbsAndSpices, OilsAndVinegars, Pantry, Frozen, Drinks, Other |
| UnitOfMeasure | Gram, Kilogram, Milliliter, Liter, Piece, Teaspoon, Tablespoon, Cup, Pinch, Bunch, Clove, Slice, Can, Pack |
| MealPlanType | Daily, Weekly, Biweekly, BatchAndFreeze |
| MealPlanStatus | Draft, Accepted, Archived |
| FamilyMemberType | Adult, Child |
| FeedbackReaction | None, Liked, Disliked |
| ShoppingListStatus | Active, Completed, Archived |
| BudgetLevel | Any, Low, Medium, High |

## Appendix B — Worked example

A family of two adults and two children aged 2 and 8. The 2-year-old is allergic to nuts. No fish. Meals under 45 minutes. Five dinners a week. Freezer-friendly preferred.

**Filtering.** Every recipe over 45 minutes is dropped. Every recipe whose primary protein is Fish is dropped. Every recipe containing an ingredient flagged as containing nuts is dropped. Every recipe with `MinAgeMonths` above 24 is dropped.

**Scoring and selection.** Five slots are filled in date order. The result:

| Day | Meal | Protein | Time |
| --- | --- | --- | --- |
| Monday | Red lentil soup | Legume | 35 min |
| Tuesday | Lamb and bulgur pilaf | Lamb | 45 min |
| Wednesday | Chickpea and spinach stew | Legume | 30 min |
| Thursday | Chicken meatballs | Chicken | 35 min |
| Friday | Pasta with tomato and basil | None | 20 min |

No protein repeats on consecutive days. Two legume meals meet the quota. Four of the five freeze well.

**Shopping list.** Twenty-three consolidated lines grouped by aisle. Onion appears once at 825 g, drawn from four different recipes; garlic once at 11 cloves from three. Salt, pepper and olive oil are flagged "check cupboard" rather than listed as purchases.

**Feedback.** The family marks the lentil soup 👎 with "kids didn't eat it". That recipe now carries a history score of −4.5, multiplied by 3.0 in the scorer — enough that it will not be selected again unless very little else qualifies.
