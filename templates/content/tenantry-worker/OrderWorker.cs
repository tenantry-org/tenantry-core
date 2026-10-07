using Microsoft.EntityFrameworkCore;
using Tenantry;

namespace TenantryWorker;

// Runs each message as its tenant. RunInScopeAsync looks the tenant up in the store and refuses one that does not
// exist or that ValidateTenantActivity refuses, so a message cannot write as a tenant it only names; and each message
// gets a DI scope, and so a DbContext, of its own. A message that fails is logged, and the worker goes on.
public sealed partial class OrderWorker(WorkQueue queue, ITenantScopeFactory<string> scopes, ILogger<OrderWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.ReadAllAsync(stoppingToken))
        {
            // RunInScopeAsync opens no log scope, so this one adds TenantId to every entry written for the message,
            // EF Core's included, where the logging provider records scopes. The messages name the tenant too, for the
            // console logger, which leaves scopes out by default.
            using var logScope = logger.BeginScope(TenantTelemetry.CreateLogScope(message.TenantId));

            try
            {
                await scopes.RunInScopeAsync(message.TenantId, async (scope, ct) =>
                {
                    if (string.IsNullOrWhiteSpace(message.Description))
                        throw new InvalidOperationException("An order needs a description.");

                    var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
                    db.Orders.Add(new Order { Description = message.Description });
                    await db.SaveChangesAsync(ct);

                    // Only this tenant's orders: the query filter adds the tenant.
                    Processed(logger, message.Description, message.TenantId, await db.Orders.CountAsync(ct));
                }, stoppingToken);
            }
            catch (TenantNotResolvedException refused)
            {
                // The tenant does not exist (TenantNotFoundException) or is suspended (TenantInactiveException).
                Dropped(logger, message.TenantId, refused.Message);
            }
            catch (Exception failed) when (failed is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                // One message's failure must not stop the worker. Retry or dead-letter it here, as your broker allows.
                Failed(logger, failed, message.TenantId);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Processed '{Description}' for tenant {TenantId}, which has {Count} order(s)")]
    private static partial void Processed(ILogger logger, string description, string tenantId, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dropped a message for tenant {TenantId}: {Reason}")]
    private static partial void Dropped(ILogger logger, string tenantId, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to process a message for tenant {TenantId}")]
    private static partial void Failed(ILogger logger, Exception exception, string tenantId);
}
