namespace Tenantry.AspNetCore.Attributes;

/// <summary>
/// Allows an endpoint or controller action to execute without a resolved tenant.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowMissingTenantAttribute : Attribute;
