using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace TenantryApi;

/// <summary>
/// Token validation settings. In production the tokens come from your identity provider and only the
/// issuer, audience and signing keys are configured here; <see cref="IssueDevelopmentToken"/> exists so the
/// sample can be run and tested without one.
/// </summary>
public sealed class AuthSettings
{
    /// <summary>The claim that lists the tenants a caller may select.</summary>
    public const string TenantClaim = "tenant_id";

    public string Issuer { get; init; } = string.Empty;

    public string Audience { get; init; } = string.Empty;

    public string SigningKey { get; init; } = string.Empty;

    public SymmetricSecurityKey GetSigningKey() =>
        SigningKey.Length >= 32
            ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey))
            : throw new InvalidOperationException(
                "Auth:SigningKey must be configured with at least 32 characters.");

    public string IssueDevelopmentToken(string subject, IEnumerable<string> tenants) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Expires = DateTime.UtcNow.AddHours(1),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = subject,
                [TenantClaim] = tenants.ToArray(),
            },
            SigningCredentials = new SigningCredentials(GetSigningKey(), SecurityAlgorithms.HmacSha256),
        });
}

public sealed record DevelopmentTokenRequest(string Subject, string[] Tenants);
