using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;

namespace Tenantry.Options.Internal;

/// <summary>
/// The <see cref="IOptionsFactory{TOptions}"/> of an options type configured per tenant. It builds a value as
/// <see cref="OptionsFactory{TOptions}"/> does, and runs the tenant's steps after every <c>Configure</c> and before every
/// <c>PostConfigure</c>, so a post-configuration sees the tenant's values: ASP.NET Core's authentication handlers build
/// their metadata managers and data protectors there, from the scheme's settings.
/// </summary>
internal sealed class TenantOptionsFactory<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
    IEnumerable<IConfigureOptions<TOptions>> setups,
    IEnumerable<ITenantOptionsStep<TOptions>> tenantSteps,
    IEnumerable<IPostConfigureOptions<TOptions>> postConfigures,
    IEnumerable<IValidateOptions<TOptions>> validations)
    : IOptionsFactory<TOptions>
    where TOptions : class
{
    private readonly IConfigureOptions<TOptions>[] _setups = [.. setups];
    private readonly ITenantOptionsStep<TOptions>[] _tenantSteps = [.. tenantSteps];
    private readonly IPostConfigureOptions<TOptions>[] _postConfigures = [.. postConfigures];
    private readonly IValidateOptions<TOptions>[] _validations = [.. validations];

    public TOptions Create(string name)
    {
        name ??= Microsoft.Extensions.Options.Options.DefaultName;
        var options = Activator.CreateInstance<TOptions>();

        foreach (var setup in _setups)
        {
            if (setup is IConfigureNamedOptions<TOptions> named)
                named.Configure(name, options);
            else if (name == Microsoft.Extensions.Options.Options.DefaultName)
                setup.Configure(options);
        }

        foreach (var step in _tenantSteps)
            step.Apply(name, options);

        foreach (var postConfigure in _postConfigures)
            postConfigure.PostConfigure(name, options);

        if (_validations.Length > 0)
        {
            List<string> failures = [];

            foreach (var validation in _validations)
            {
                if (validation.Validate(name, options) is { Failed: true } result)
                    failures.AddRange(result.Failures);
            }

            if (failures.Count > 0)
                throw new OptionsValidationException(name, typeof(TOptions), failures);
        }

        return options;
    }
}
