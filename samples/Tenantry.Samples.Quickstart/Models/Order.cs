namespace Tenantry.Samples.Quickstart.Models;

internal class Order
{
    public string TenantId { get; set; } = string.Empty;
    // System.Text.Json reads it when the orders are returned.
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public string Description { get; set; } = string.Empty;
}
