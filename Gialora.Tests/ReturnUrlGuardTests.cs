// Gialora.Tests/ReturnUrlGuardTests.cs
using Gialora.Client.Auth;

namespace Gialora.Tests;

/// <summary>
/// The login page navigates to ?returnUrl= after sign-in. NavigationManager hands
/// anything outside the app's base straight to location.replace, so an unchecked
/// value could run script on our origin (where the JWT lives) or redirect off-site.
/// </summary>
public class ReturnUrlGuardTests
{
    [Theory]
    [InlineData("weekly")]
    [InlineData("recipes/lamb-pilaf")]
    [InlineData("shopping-lists?id=3f2a&tab=open")]
    [InlineData("recipes/chicken%20soup#steps")]
    public void App_relative_paths_are_kept(string returnUrl)
        => Assert.Equal(returnUrl, ReturnUrlGuard.Sanitize(returnUrl));

    [Theory]
    [InlineData("javascript:alert(document.domain)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData(" javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("https://evil.example")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("\\\\evil.example")]
    [InlineData("recipes//evil")]
    [InlineData("/weekly")]
    [InlineData("weekly\"onload=")]
    [InlineData("we ekly")]
    public void Anything_that_could_leave_the_app_falls_back_to_home(string returnUrl)
        => Assert.Equal(string.Empty, ReturnUrlGuard.Sanitize(returnUrl));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_values_go_home(string? returnUrl)
        => Assert.Equal(string.Empty, ReturnUrlGuard.Sanitize(returnUrl));
}
