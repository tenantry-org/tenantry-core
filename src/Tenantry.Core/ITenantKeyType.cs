using System.ComponentModel;

namespace Tenantry;

/// <summary>
/// The tenant key type the application registered Tenantry with, for code that has only a service provider, such as
/// a health check registration or a host extension, so its callers never repeat the key type.
/// </summary>
/// <remarks>
/// <c>AddTenantry</c> registers it as a singleton. While services are being registered, read it with
/// <c>services.FindTenantKeyType()</c>. <see cref="Accept{TResult}"/> calls a generic method with the key type known at
/// compile time, so Native AOT compiles it, unlike <c>MakeGenericType</c> on <see cref="Type"/>.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ITenantKeyType
{
    /// <summary>The tenant key type, such as <see cref="Guid"/>.</summary>
    Type Type { get; }

    /// <summary>Calls <paramref name="visitor"/> with the tenant key type.</summary>
    /// <typeparam name="TResult">What the visitor returns.</typeparam>
    /// <param name="visitor">The code that needs the key type.</param>
    /// <returns>What <paramref name="visitor"/> returns.</returns>
    TResult Accept<TResult>(ITenantKeyTypeVisitor<TResult> visitor);
}

/// <summary>Code that needs the tenant key type, given it by <see cref="ITenantKeyType"/>.</summary>
/// <typeparam name="TResult">What the code returns.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ITenantKeyTypeVisitor<out TResult>
{
    /// <summary>Runs with the tenant key type as <typeparamref name="TKey"/>.</summary>
    /// <typeparam name="TKey">The tenant key type.</typeparam>
    /// <returns>The result.</returns>
    TResult Visit<TKey>()
        where TKey : IEquatable<TKey>, IParsable<TKey>;
}
