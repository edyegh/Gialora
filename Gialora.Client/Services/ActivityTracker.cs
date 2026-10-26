// Gialora.Client/Services/ActivityTracker.cs
using Gialora.Client.Components;
using Gialora.Client.Localization;
using Gialora.Shared.Dtos;
using Gialora.Shared.Enums;
using Microsoft.JSInterop;

namespace Gialora.Client.Services;

/// <summary>
/// Admin-ի վիճակագրության համար հավաքում է էջի դիտումները / click-երը և ուղարկում
/// batch-երով (ամեն click-ի համար առանձին request չենք անում)։
///
/// ԱՇԽԱՏՈՒՄ Է ՄԻԱՅՆ cookie-ների "Accept all"-ից հետո — մինչ այդ իրադարձությունները
/// պարզապես չեն գրանցվում (ոչ էլ հերթում են պահվում)։
///
/// Վիճակագրությունը "best effort" է. ձախողված ուղարկումը լուռ բաց ենք թողնում,
/// որ user-ը երբեք սխալ չտեսնի մի բանի պատճառով, որ իրեն չի վերաբերում։
/// </summary>
public class ActivityTracker : ApiClientBase
{
    private const int FlushAtCount = 20;
    private static readonly TimeSpan FlushDelay = TimeSpan.FromSeconds(5);

    private readonly IJSRuntime _js;
    private readonly List<ActivityEventDto> _queue = new();

    // null — դեռ չգիտենք (կամ user-ը դեռ չի ընտրել)
    private bool? _allowed;
    private bool _flushScheduled;
    private bool _flushing;

    public ActivityTracker(HttpClient http, Localizer localizer, IJSRuntime js) : base(http, localizer)
    {
        _js = js;
    }

    public Task TrackPageViewAsync(string path) =>
        EnqueueAsync(new ActivityEventDto { Type = ActivityType.PageView, Path = path });

    public Task TrackClickAsync(string target, string path) =>
        EnqueueAsync(new ActivityEventDto { Type = ActivityType.Click, Target = target, Path = path });

    public Task TrackRecipeViewAsync(Guid recipeId, string path) =>
        EnqueueAsync(new ActivityEventDto { Type = ActivityType.RecipeView, RecipeId = recipeId, Path = path });

    /// <summary>CookieConsent-ը կանչում է ընտրությունից անմիջապես հետո։</summary>
    public void SetConsent(bool allowed)
    {
        _allowed = allowed;
        if (!allowed)
            _queue.Clear();
    }

    public async Task FlushAsync()
    {
        if (_flushing || _queue.Count == 0)
            return;

        _flushing = true;
        var batch = _queue.Take(ActivityBatchDto.MaxEvents).ToList();
        _queue.RemoveRange(0, batch.Count);

        try
        {
            await PostAsync("api/activity", new ActivityBatchDto { Events = batch });
        }
        catch (Exception ex) when (ex is HttpRequestException or ApiException or TaskCanceledException)
        {
            // Offline / rate limit — այս batch-ը կորչում է, վիճակագրության համար անկարևոր է
        }
        finally
        {
            _flushing = false;
        }

        if (_queue.Count > 0)
            ScheduleFlush();
    }

    private async Task EnqueueAsync(ActivityEventDto activity)
    {
        if (!await IsAllowedAsync())
            return;

        _queue.Add(activity);

        if (_queue.Count >= FlushAtCount)
            await FlushAsync();
        else
            ScheduleFlush();
    }

    private void ScheduleFlush()
    {
        if (_flushScheduled)
            return;

        _flushScheduled = true;
        _ = FlushLaterAsync();
    }

    private async Task FlushLaterAsync()
    {
        await Task.Delay(FlushDelay);
        _flushScheduled = false;
        await FlushAsync();
    }

    private async Task<bool> IsAllowedAsync()
    {
        if (_allowed is { } known)
            return known;

        try
        {
            var saved = await _js.InvokeAsync<string?>("localStorage.getItem", CookieConsent.StorageKey);

            // Միայն վերջնական ընտրությունն ենք cache անում — "դեռ չի ընտրել"-ը կարող է փոխվել
            if (saved == CookieConsent.AcceptAll)
                _allowed = true;
            else if (saved == CookieConsent.NecessaryOnly)
                _allowed = false;

            return _allowed == true;
        }
        catch (JSException)
        {
            // Storage-ը արգելափակված է — համաձայնությունն անհայտ է, չենք հետևում
            return false;
        }
    }
}
