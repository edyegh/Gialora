# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Gialora is a Mediterranean meal-planning app: a recipe catalogue plus a rules-based (not AI) weekly meal planner that generates plans from a family profile and consolidates them into a grocery list. .NET 9, SQL Server, Blazor WebAssembly.

**`docs/ARCHITECTURE.md` is the authoritative deep reference** (~850 lines): full data model, the planning engine's filter/score/select algorithm, shopping-list consolidation maths, the complete HTTP API table, and security model. Read it before non-trivial work on the domain, engine, or API. This file covers commands, the big-picture layering, and conventions that bite — including the i18n/PWA layer added after ARCHITECTURE.md was written.

## Commands

Run from the solution root. The API applies migrations and seeds on startup, so a fresh clone needs no manual DB setup (seeding is on in Development, off in production).

```bash
dotnet build Gialora.sln
dotnet run --project Gialora.Api    --launch-profile https   # API  → https://localhost:5001 (Swagger at /swagger)
dotnet run --project Gialora.Client --launch-profile https   # UI   → https://localhost:7280
```

Both must run together; the client talks to the API over HTTPS. Seeded admin (dev only): `admin@gialora.local` / `ChangeMe123!`.

Tests (xUnit — engine, family goals, shopping-list unit maths, client auth token flow):

```bash
dotnet test                                                     # all
dotnet test --filter "FullyQualifiedName~AuthTokenFlowTests"    # one class
dotnet test --filter "FullyQualifiedName~Planning.ScorerTests.SameProteinPenalised"  # one test
```

Migrations (tools: `dotnet tool install --global dotnet-ef`):

```bash
dotnet ef migrations add <Name> -p Gialora.Data -s Gialora.Api
dotnet ef database update      -p Gialora.Data -s Gialora.Api
```

## Architecture

Five projects, strict one-way dependency: `Client` and `Api` → `Application` → `Data` → `Shared`.

| Project | Role |
| --- | --- |
| `Gialora.Shared` | DTOs + domain enums. **Zero dependencies.** |
| `Gialora.Data` | EF Core entities, `GialoraDbContext`, migrations. |
| `Gialora.Application` | Business logic: `Services/`, the planning engine (`Planning/`), `Seed/`. |
| `Gialora.Api` | Controllers, auth, middleware. The HTTP surface only. |
| `Gialora.Client` | Blazor WASM UI. |

Request flow: Blazor page → typed API client (`AccountApi`/`RecipeApi`/`PlannerApi`/`ContentApi`/`AdminApi`, all on `ApiClientBase`) → `HttpClient` + `AuthHeaderHandler` → `ExceptionHandlingMiddleware` → controller → Application service → `DbContext`. Controllers do HTTP only; **all business rules live in Application services**, and all planning rules live in the engine (a pure, DB-free class — `MealPlanService` feeds it input and persists its output).

## Conventions that bite

These are non-obvious and have caused real bugs; violating them breaks things silently.

- **The client references ONLY `Gialora.Shared`** — never `Data`/EF Core. Blazor WASM ships every referenced assembly to the browser; pulling in EF Core bloated the download for a DB the browser can't open. Keep domain enums in `Shared` (both DTOs and entities use them) so the UI reads them directly with no round-trip.
- **Enums serialize by NAME, not value**, over the wire and in some DB columns (e.g. `Family.ExcludedProteins` is pipe-delimited enum names). The client's `GialoraJson.Options` adds `JsonStringEnumConverter` — without it recipe deserialization fails outright. Reordering an enum must stay safe; never rely on numeric values.
- **`TokenStore` and `Localizer` are registered as SINGLETONS, deliberately.** `IHttpClientFactory` builds its handler chain in its own DI scope, so a *scoped* token cache gives `AuthHeaderHandler` a different instance than the UI — the bug where a freshly-registered user sent no `Authorization` header. `AuthTokenFlowTests` guards this.
- **EF Core new-entity pitfall:** `BaseEntity` sets `Id` in its initializer, so a new entity already has a non-default PK. Adding it through a *tracked* parent's navigation collection makes EF emit UPDATE (0 rows → `DbUpdateConcurrencyException`) instead of INSERT. Fix: set the FK explicitly and call `DbSet.Add`/`AddRange` directly; for removal use `RemoveRange` and leave the collection alone. Applies to shopping-list items, recipe update, bulk import.
- **Soft delete everywhere:** every `BaseEntity` has a `!IsDeleted` global query filter; deletes set the flag. Join tables (`RecipeIngredient`, `RecipeTag`, `FavoriteRecipe`) carry filters referencing both ends. `List<string>` columns use a `ValueConverter` **and** a `ValueComparer` — omit the comparer and `SaveChanges` silently writes nothing on in-list edits.
- **Catalogue text is per language.** Recipe Title/Description/Instructions, Ingredient Name and Tag DisplayName live in `RecipeTranslation`/`IngredientTranslation`/`TagTranslation` (key = parent + language), not on the entity. Reads take the request language (`IContentLanguage`, from `Accept-Language`) with fallback requested → `en` → any, all fields from ONE row; writes touch only the DTO's `Language`. See ARCHITECTURE.md §3.3.1. Add translation rows via `DbSet.Add` with the FK set.
- **Family-scoping is the security boundary:** every family-scoped read/write resolves the caller's family and filters by it; a plan/list Guid alone grants nothing. A resource owned by another family returns **404, not 403**.

## Localization & PWA (post-dates ARCHITECTURE.md)

The UI is multi-language (en / hy Armenian / el Greek) via a custom `Localizer` (`Gialora.Client/Localization/`), **not** `.resx`/`IStringLocalizer` — the client sets `InvariantGlobalization=true` to avoid shipping ~1.1 MB of ICU data, which satellite assemblies and `CultureInfo` depend on.

- Translations are flat JSON dictionaries in `wwwroot/i18n/{en,hy,el}.json`. Missing keys fall back to English, then to the key itself (so gaps show up in the UI). **When you add UI strings, add the key to all three JSON files.**
- `_Imports.razor` globally sets `@inherits LocalizedComponentBase`, which injects `L` and auto-re-renders on language change. Use `L["Key"]`, `L["Key", arg0]`, `L.Enum(value)`, and `L.ShortDate/LongDate/Weekday` (date month/day names come from the JSON, since `InvariantGlobalization` forces English in `ToString`). If a component needs a different `@inherits`, inherit from a `LocalizedComponentBase` variant or it won't react to language switches. `Localizer.InitializeAsync` runs in `Program.cs` before first render.
- **Code comments in the client (and some config) are written in Armenian.** Match the surrounding language when editing a file whose comments are Armenian; otherwise write English.
- PWA: `service-worker.js` (empty, used in dev) is swapped for `service-worker.published.js` on publish; `service-worker-assets.js` is generated at publish with integrity hashes for precaching. `PwaUpdatePrompt.razor` notifies users of a new version.
