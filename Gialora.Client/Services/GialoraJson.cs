// Gialora.Client/Services/GialoraJson.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gialora.Client.Services;

/// <summary>
/// Client-ի և API-ի JSON պայմանավորվածությունը ՄԵԿ տեղում։
///
/// API-ն enum-երը գրում է ԱՆՈՒՆՈՎ ("Dinner", ոչ թե 2)։ Առանց այս converter-ի
/// ReadFromJsonAsync-ը լուռ չի կարողանում deserialize անել և ամեն ռեցեպտի
/// mealType-ը ձախողում է ամբողջ պատասխանը։
/// </summary>
public static class GialoraJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
