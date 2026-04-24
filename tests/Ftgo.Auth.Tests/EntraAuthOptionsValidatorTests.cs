using Ftgo.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Ftgo.Auth.Tests;

public sealed class EntraAuthOptionsValidatorTests
{
    private static IOptions<EntraAuthOptions> Build(Dictionary<string, string?> settings)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services
            .AddOptions<EntraAuthOptions>()
            .Bind(config.GetSection(EntraAuthOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<EntraAuthOptions>, EntraAuthOptionsValidator>();
        return services.BuildServiceProvider().GetRequiredService<IOptions<EntraAuthOptions>>();
    }

    [Fact]
    public void MultiTenant_without_allow_list_throws_on_access()
    {
        var opts = Build(new()
        {
            ["EntraAuth:Tenancy"] = "MultiTenant",
        });

        Should.Throw<OptionsValidationException>(() => _ = opts.Value)
              .Message.ShouldContain("AllowedTenantIds");
    }

    [Fact]
    public void SingleTenant_with_empty_allow_list_is_fine()
    {
        var opts = Build(new()
        {
            ["EntraAuth:Tenancy"] = "SingleTenant",
        });

        _ = opts.Value;
    }
}
