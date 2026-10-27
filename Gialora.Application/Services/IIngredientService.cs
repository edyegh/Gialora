// Gialora.Application/Services/IIngredientService.cs
using Gialora.Shared.Dtos;

namespace Gialora.Application.Services;

public interface IIngredientService
{
    Task<List<IngredientDto>> GetAllAsync(string? search);
    Task<IngredientDto?> GetByIdAsync(Guid id);
    Task<IngredientDto> CreateAsync(IngredientCreateDto dto);
    Task<IngredientDto?> UpdateAsync(Guid id, IngredientUpdateDto dto);

    /// <summary>Ջնջումը թույլատրվում է միայն եթե բաղադրիչը ոչ մի ռեցեպտում չի օգտագործվում։</summary>
    Task<bool> DeleteAsync(Guid id);

    Task<List<TagDto>> GetTagsAsync();

    /// <summary>Բոլոր լեզուների անունները (admin)։ null = բաղադրիչ չկա։</summary>
    Task<List<IngredientNameDto>?> GetTranslationsAsync(Guid id);
    Task<bool> SaveTranslationAsync(Guid id, string language, IngredientNameDto dto);
    Task<bool> DeleteTranslationAsync(Guid id, string language);

    /// <summary>Tag-երը՝ ամեն լեզվի ցուցադրվող անունով (admin)։</summary>
    Task<List<TagAdminDto>> GetTagsForAdminAsync();
    Task<bool> SaveTagTranslationAsync(Guid tagId, string language, TagNameDto dto);
}
