using Microsoft.EntityFrameworkCore;
using Tenantry;

namespace TenantryWorker;

// Runs each message as its tenant. RunInScopeAsync looks the tenant up in the store and refuses one that does not
// exist (or that ValidateTenantActivity refuses), so a message cannot write as a tenant it only names; and each
// message gets a DI scope, and so a DbContext, of its own.
public sealed partial class OrderWorker(WorkQueue queue, ITenantScopeFactory<string> scopes, ILogger<OrderWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await scopes.RunInScopeAsync(message.TenantId, async (scope, ct) =>
                {
                    var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
                    db.Orders.Add(new Order { Description = message.Description });
                    await db.SaveChangesAsync(ct);

                    // Only this tenant's orders: the query filter adds the tenant.
                    Processed(logger, message.Description, message.TenantId, await db.Orders.CountAsync(ct));
                }, stoppingToken);
            }
            catch (TenantNotFoundException)
            {
                Dropped(logger, message.TenantId);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Processed '{Description}' for tenant {TenantId}, which has {Count} order(s)")]
    private static partial void Processed(ILogger logger, string description, string tenantId, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dropped a message for tenant {TenantId}, which does not exist")]
    private static partial void Dropped(ILogger logger, string tenantId);
}
