namespace Tenantry.AspNetCore.Attributes;

/// <summary>
/// Requires Tenantry to resolve a tenant before the endpoint or controller action executes.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireTenantAttribute : Attribute;
