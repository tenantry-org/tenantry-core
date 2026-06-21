using AwesomeAssertions;
using Tenantry.AspNetCore.Internal;

namespace Tenantry.AspNetCore.Tests;

/// <summary>
/// White-box tests for internal builder guard clauses. These paths are unreachable
/// through the normal public configuration flow (which always registers the required
/// services), but the guards exist to fail fast on internal misuse, so we cover them
/// directly via the internal types.
/// </summary>
public sealed class InternalBuilderGuardTests
{
    [Fact]
    public void ValidationGroup_Build_WithNoValidators_Throws()
    {
        var group = new TenantAccessValidationGroupBuilder<string>();

        var act = () => group.Build();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*at least one validator*");
    }

    [Fact]
    public void ValidateTenantAccess_WhenOptionsNotRegistered_ThrowsInternalError()
    {
        // AddTenantry registers TenantResolutionOptions; constructing the builder over a
        // bare service collection bypasses that, tripping the defensive "internal error"
        // guard in GetResolutionOptions.
        var builder = new AspNetCoreTenantBuilder<string>(new ServiceCollection());

        var act = () => builder.ValidateTenantAccess((_, _, _) => ValueTask.FromResult(true));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*internal error*");
    }
}
