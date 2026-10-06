using System.Globalization;
using AwesomeAssertions;

namespace Tenantry.Core.Tests;

/// <summary>
/// <see cref="TenantIds"/>, <see cref="TenantTelemetry"/> and <see cref="TenantPropagation"/>: how a tenant id is
/// written and read wherever it leaves or enters the process, whatever the machine's culture.
/// </summary>
public sealed class TenantIdsTests
{
    [Fact]
    public void Format_UsesTheInvariantCulture()
    {
        using var _ = UseCultureWithNegativeSign("~");

        (-5).ToString(CultureInfo.CurrentCulture).Should().Be("~5", "the test's culture must format differently");
        TenantIds.Format(-5).Should().Be("-5");
    }

    [Fact]
    public void TheMessagesOfTenantNotFoundAndTenantInactive_FormatTheIdWithTheInvariantCulture()
    {
        using var _ = UseCultureWithNegativeSign("~");

        new TenantNotFoundException(-5).Message.Should().Be("Tenant '-5' was not found in the tenant store.");
        new TenantInactiveException(-5).Message.Should().Be("Tenant '-5' is not active, so no work runs for it.");
    }

    [Fact]
    public void Format_WritesAGuidInItsDFormat_AndAStringAsItIs()
    {
        var id = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

        TenantIds.Format(id).Should().Be("6f9619ff-8b86-d011-b42d-00c04fc964ff");
        TenantIds.Format("Acme Ltd").Should().Be("Acme Ltd");
    }

    [Fact]
    public void Format_RefusesNull() =>
        FluentActions.Invoking(() => TenantIds.Format<string>(null!)).Should().Throw<ArgumentNullException>()
            .WithParameterName("tenantId");

    [Fact]
    public void TryParse_ReadsWhatFormatWrites_InAnyCulture()
    {
        using var _ = UseCultureWithNegativeSign("~");

        TenantIds.TryParse<int>(TenantIds.Format(-5), out var id).Should().BeTrue();
        id.Should().Be(-5);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not-a-number")]
    public void TryParse_RefusesTextThatDoesNotParse_AndTheIdsReservedForNoTenant(string? text)
    {
        TenantIds.TryParse<int>(text, out var id).Should().BeFalse();
        id.Should().Be(0);
    }

    [Fact]
    public void TryParse_RefusesTheEmptyGuid_AndAnEmptyString()
    {
        TenantIds.TryParse<Guid>(Guid.Empty.ToString(), out _).Should().BeFalse();
        TenantIds.TryParse<string>("", out var text).Should().BeFalse();
        text.Should().BeNull();
    }

    [Fact]
    public void IsUnset_IsTrueForNull_TheKeyTypesDefault_AndAnEmptyString()
    {
        TenantIds.IsReserved<string>(null).Should().BeTrue();
        TenantIds.IsReserved("").Should().BeTrue();
        TenantIds.IsReserved(0).Should().BeTrue();
        TenantIds.IsReserved(Guid.Empty).Should().BeTrue();

        TenantIds.IsReserved(" ").Should().BeFalse();
        TenantIds.IsReserved(7).Should().BeFalse();
        TenantIds.IsReserved(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void TheLogScope_HasOneProperty_TenantId()
    {
        var scope = TenantTelemetry.CreateLogScope("acme");

        scope.Count.Should().Be(1);
        scope[0].Key.Should().Be("TenantId");
        scope[0].Value.Should().Be("acme");
        scope.Single().Should().Be(scope[0]);
        scope.ToString().Should().Be("TenantId:acme");
        FluentActions.Invoking(() => scope[1]).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TheLogScope_FormatsAnIdOfAnyKeyType_AsTenantryDoes()
    {
        var id = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff");

        TenantTelemetry.CreateLogScope(id)[0].Value.Should().Be(TenantIds.Format(id));
        TenantTelemetry.CreateLogScope(1234567)[0].Value.Should().Be("1234567");
    }

    [Fact]
    public void TheLogScope_RefusesNull() =>
        FluentActions.Invoking(() => TenantTelemetry.CreateLogScope(null!)).Should().Throw<ArgumentNullException>()
            .WithParameterName("tenantId");

    [Fact]
    public void TheNames_AreTheOnesEveryTenantryPackageRecordsAndCarries()
    {
        TenantTelemetry.TenantIdTag.Should().Be("tenant.id");
        TenantTelemetry.LogScopeName.Should().Be("TenantId");
        TenantPropagation.HeaderName.Should().Be("tenantry-tenant-id");
    }

    private static CultureScope UseCultureWithNegativeSign(string negativeSign)
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NegativeSign = negativeSign;
        return new CultureScope(culture);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public CultureScope(CultureInfo culture) => CultureInfo.CurrentCulture = culture;

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}
