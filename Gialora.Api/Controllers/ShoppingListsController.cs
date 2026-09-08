// Gialora.Api/Controllers/ShoppingListsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Gialora.Api.Extensions;
using Gialora.Application.Services;
using Gialora.Shared.Dtos;

namespace Gialora.Api.Controllers;

[ApiController]
[Route("api/shopping-lists")]
[Authorize]
public class ShoppingListsController : ControllerBase
{
    private readonly IShoppingListService _lists;

    public ShoppingListsController(IShoppingListService lists)
    {
        _lists = lists;
    }

    [HttpGet]
    public async Task<ActionResult<List<ShoppingListDto>>> GetMine()
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();

        return Ok(await _lists.GetMyListsAsync(userId));
    }

    [HttpGet("{listId:guid}")]
    public async Task<ActionResult<ShoppingListDto>> GetById(Guid listId)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();

        var list = await _lists.GetByIdAsync(userId, listId);
        return list is null ? NotFound() : Ok(list);
    }

    /// <summary>"User checks off ingredients while shopping" (app structure §1)։</summary>
    [HttpPut("{listId:guid}/items/{itemId:guid}/checked")]
    public async Task<ActionResult<ShoppingListItemDto>> SetChecked(
        Guid listId, Guid itemId, [FromBody] ShoppingListItemCheckDto dto)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();

        var item = await _lists.SetItemCheckedAsync(userId, listId, itemId, dto.IsChecked);
        return item is null ? NotFound() : Ok(item);
    }

    /// <summary>Ձեռքով ավելացրած տողերը (ոչ ռեցեպտից) regenerate-ից հետո մնում են։</summary>
    [HttpPost("{listId:guid}/items")]
    public async Task<ActionResult<ShoppingListItemDto>> AddItem(
        Guid listId, [FromBody] ShoppingListItemCreateDto dto)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();

        return Ok(await _lists.AddItemAsync(userId, listId, dto));
    }

    [HttpDelete("{listId:guid}/items/{itemId:guid}")]
    public async Task<IActionResult> RemoveItem(Guid listId, Guid itemId)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();

        return await _lists.RemoveItemAsync(userId, listId, itemId) ? NoContent() : NotFound();
    }

    [HttpPost("{listId:guid}/clear-checked")]
    public async Task<ActionResult<ShoppingListDto>> ClearChecked(Guid listId)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();

        var list = await _lists.ClearCheckedAsync(userId, listId);
        return list is null ? NotFound() : Ok(list);
    }

    [HttpDelete("{listId:guid}")]
    public async Task<IActionResult> Delete(Guid listId)
    {
        if (User.GetUserId() is not { } userId)
            return Unauthorized();

        return await _lists.DeleteAsync(userId, listId) ? NoContent() : NotFound();
    }
}
