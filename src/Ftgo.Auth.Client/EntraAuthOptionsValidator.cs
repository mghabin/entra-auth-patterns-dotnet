using Microsoft.Extensions.Options;

namespace Ftgo.Auth;

internal sealed class EntraAuthOptionsValidator : IValidateOptions<EntraAuthOptions>
{
    public ValidateOptionsResult Validate(string? name, EntraAuthOptions options)
    {
        var failures = new List<string>();

        if (options.Tenancy == TenancyMode.MultiTenant && options.AllowedTenantIds.Count == 0)
        {
            failures.Add(
                "EntraAuth: Tenancy=MultiTenant requires at least one entry in AllowedTenantIds. " +
                "Do NOT run a multi-tenant API with an empty allow-list — it accepts every tenant on earth.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
