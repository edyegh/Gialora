// Gialora.Client/Localization/LocalizedComponentBase.cs
using Microsoft.AspNetCore.Components;

namespace Gialora.Client.Localization;

/// <summary>
/// Բոլոր .razor բաղադրիչների base-ը (_Imports.razor-ի @inherits)։ Տալիս է
/// <c>L["Key"]</c>-ը և լեզուն փոխվելիս ինքնուրույն re-render է անում —
/// այլապես layout-ի StateHasChanged-ը էջերին չի հասնում, քանի որ նրանց
/// parameter-ները չեն փոխվել։
/// </summary>
public abstract class LocalizedComponentBase : ComponentBase, IDisposable
{
    [Inject] protected Localizer L { get; set; } = default!;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        L.Changed += OnLanguageChanged;
    }

    private void OnLanguageChanged() => InvokeAsync(StateHasChanged);

    public virtual void Dispose()
    {
        L.Changed -= OnLanguageChanged;
        GC.SuppressFinalize(this);
    }
}

/// <summary>Նույնը՝ layout-ների համար (MainLayout-ը LayoutComponentBase է)։</summary>
public abstract class LocalizedLayoutComponentBase : LayoutComponentBase, IDisposable
{
    [Inject] protected Localizer L { get; set; } = default!;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        L.Changed += OnLanguageChanged;
    }

    private void OnLanguageChanged() => InvokeAsync(StateHasChanged);

    public virtual void Dispose()
    {
        L.Changed -= OnLanguageChanged;
        GC.SuppressFinalize(this);
    }
}
