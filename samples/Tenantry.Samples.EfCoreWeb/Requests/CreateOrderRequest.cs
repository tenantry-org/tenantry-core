namespace Tenantry.Samples.EfCoreWeb.Requests;

internal record CreateOrderRequest(List<CreateOrderItemRequest> Items);
