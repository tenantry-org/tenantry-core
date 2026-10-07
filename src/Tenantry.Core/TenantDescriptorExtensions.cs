namespace Tenantry;

/// <summary>
/// Reads a tenant as the application's own tenant type.
/// </summary>
public static class TenantDescriptorExtensions
{
    /// <summary>
    /// Returns <paramref name="tenant"/> as <typeparamref name="TTenant"/>, the type your tenant store returns, so
    /// a delegate that receives an <see cref="ITenantDescriptor{TKey}"/> can read your tenant's own properties.
    /// </summary>
    /// <typeparam name="TTenant">Your tenant type, which implements <see cref="ITenantDescriptor{TKey}"/>.</typeparam>
    /// <param name="tenant">A tenant from your tenant store.</param>
    /// <returns>The same tenant, as <typeparamref name="TTenant"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// The tenant is not a <typeparamref name="TTenant"/>: the tenant store returns another type. The message names
    /// both types.
    /// </exception>
    /// <example>
    /// <code>
    /// tenant.UseConnectionStrings(options =&gt;
    ///     options.GetConnectionString = t =&gt; t.As&lt;AppTenant&gt;().ConnectionString);
    /// </code>
    /// </example>
    public static TTenant As<TTenant>(this ITenantDescriptor tenant)
        where TTenant : class, ITenantDescriptor
    {
        ArgumentNullException.ThrowIfNull(tenant);

        return tenant as TTenant ?? throw NotOfType(tenant, typeof(TTenant));
    }

    private static InvalidOperationException NotOfType(ITenantDescriptor tenant, Type expected) =>
        new($"Tenant '{tenant.Name}' is of type {DisplayName(tenant.GetType())}, not {DisplayName(expected)}. Tenants " +
            $"are what the tenant store returns: make it return {DisplayName(expected)}, or read the tenant as the type " +
            "it returns.");

    // TenantDescriptor`1 reads as TenantDescriptor<Guid>. A type nested in a generic type is generic too, with the
    // outer type's arguments, but its name has no arity.
    private static string DisplayName(Type type) =>
        type.IsGenericType
            ? $"{type.Name.Split('`')[0]}<{string.Join(", ", type.GetGenericArguments().Select(DisplayName))}>"
            : type.Name;
}
