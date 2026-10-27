using Gialora.Application.Common;
using Gialora.Application.Services;
using Gialora.Data;
using Gialora.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Gialora.Tests;

/// <summary>
/// The admin edits UI texts (home page, footer…) per language. A saved text overrides the
/// built-in i18n JSON value for that language only; resetting removes the override.
/// </summary>
public class SiteTextServiceTests
{
    private readonly GialoraDbContext _db = new(new DbContextOptionsBuilder<GialoraDbContext>()
        .UseInMemoryDatabase($"site-texts-{Guid.NewGuid()}")
        .Options);

    private SiteTextService Texts() => new(_db);

    [Fact]
    public async Task A_saved_text_is_returned_only_for_its_language()
    {
        await Texts().SaveAsync(new SiteTextDto { Key = "Home.Heading", Language = "hy", Value = "  Բարի գալուստ  " });

        Assert.Equal("Բարի գալուստ", (await Texts().GetForLanguageAsync("hy"))["Home.Heading"]);
        Assert.Empty(await Texts().GetForLanguageAsync("en"));
    }

    [Fact]
    public async Task Saving_again_updates_the_same_row()
    {
        await Texts().SaveAsync(new SiteTextDto { Key = "Footer.Tagline", Language = "en", Value = "First" });
        await Texts().SaveAsync(new SiteTextDto { Key = "Footer.Tagline", Language = "en", Value = "Second" });

        var all = await Texts().GetAllAsync();
        Assert.Equal("Second", Assert.Single(all).Value);
    }

    [Fact]
    public async Task Reset_brings_back_the_built_in_text()
    {
        await Texts().SaveAsync(new SiteTextDto { Key = "Home.Lede", Language = "el", Value = "Γεια" });

        Assert.True(await Texts().ResetAsync("Home.Lede", "el"));
        Assert.False(await Texts().ResetAsync("Home.Lede", "el"));
        Assert.Empty(await Texts().GetForLanguageAsync("el"));
    }

    [Theory]
    [InlineData("Home.Heading", "fr", "Bonjour")]      // unsupported language
    [InlineData("Heading", "en", "Hello")]             // not a dotted i18n key
    [InlineData("Home.<b>", "en", "Hello")]            // malformed key
    [InlineData("Home.Heading", "en", "   ")]          // empty — use Reset instead
    [InlineData("Home.Heading", "en", "<img src=x onerror=alert(1)>")] // HTML is rejected
    public async Task Invalid_input_is_rejected(string key, string language, string value)
    {
        await Assert.ThrowsAsync<ValidationFailedException>(() =>
            Texts().SaveAsync(new SiteTextDto { Key = key, Language = language, Value = value }));
        Assert.Empty(await Texts().GetAllAsync());
    }
}
