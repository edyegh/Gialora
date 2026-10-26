// Gialora.Api/Services/DatabaseHealthCheck.cs
using Gialora.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Gialora.Api.Services;

/// <summary>
/// /health-ի համար — DB-ն հասանելի՞ է։ Սեփական փոքր check, որ
/// HealthChecks.EntityFrameworkCore package-ը չքաշենք մեկ CanConnectAsync-ի համար։
/// </summary>
public class DatabaseHealthCheck : IHealthCheck
{
    private readonly GialoraDbContext _db;

    public DatabaseHealthCheck(GialoraDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await _db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Database is unreachable.");
}
