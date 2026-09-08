// Gialora.Application/Common/AppExceptions.cs
namespace Gialora.Application.Common;

/// <summary>
/// Application-layer-ի սխալները տիպավորված են, որ API-ի middleware-ը կարողանա դրանք
/// ճիշտ HTTP status-ի վերածել։ Նախկինում ամեն սխալ InvalidOperationException էր և
/// controller-ը ստիպված էր message-ի տեքստով կռահել՝ 400 է, 404, թե 409։
/// </summary>
public abstract class AppException : Exception
{
    protected AppException(string message) : base(message) { }
}

/// <summary>404 — ռեսուրսը գոյություն չունի կամ չի պատկանում այս user-ին։</summary>
public class NotFoundException : AppException
{
    public NotFoundException(string message = "The requested resource was not found.") : base(message) { }
}

/// <summary>400 — մուտքային տվյալը սխալ է։</summary>
public class ValidationFailedException : AppException
{
    public Dictionary<string, string[]> Errors { get; }

    public ValidationFailedException(string message, Dictionary<string, string[]>? errors = null)
        : base(message)
    {
        Errors = errors ?? new Dictionary<string, string[]>();
    }
}

/// <summary>409 — բախում ընթացիկ վիճակի հետ (օր. email-ն արդեն զբաղված է)։</summary>
public class ConflictException : AppException
{
    public ConflictException(string message) : base(message) { }
}

/// <summary>403 — user-ը authenticated է, բայց այս ռեսուրսը իրենը չէ։</summary>
public class ForbiddenException : AppException
{
    public ForbiddenException(string message = "You do not have access to this resource.") : base(message) { }
}
