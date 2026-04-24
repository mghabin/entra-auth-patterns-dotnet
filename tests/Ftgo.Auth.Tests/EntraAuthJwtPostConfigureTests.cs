using Ftgo.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Ftgo.Auth.Tests;

public sealed class EntraAuthJwtPostConfigureTests
{
    private static IConfiguration BuildConfig(string clientId, string? tenantId = null)
    {
        var dict = new Dictionary<string, string?>
        {
            ["AzureAd:ClientId"] = clientId,
            ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
        };
        if (tenantId is not null) dict["AzureAd:TenantId"] = tenantId;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static EntraAuthJwtPostConfigure CreateSut(EntraAuthOptions opts, IConfiguration config) =>
        new(Options.Create(opts), config);

    [Fact]
    public void PostConfigure_PinsClientIdAsValidAudience()
    {
        var clientId = Guid.NewGuid().ToString();
        var sut = CreateSut(new EntraAuthOptions(), BuildConfig(clientId));
        var jwtOptions = new JwtBearerOptions();

        sut.PostConfigure(JwtBearerDefaults.AuthenticationScheme, jwtOptions);

        jwtOptions.TokenValidationParameters.ValidAudiences.ShouldContain(clientId);
    }

    [Fact]
    public void PostConfigure_AddsAdditionalAudiencesAlongsideClientId()
    {
        var clientId = Guid.NewGuid().ToString();
        var extra = "api://legacy-app-id-uri";
        var sut = CreateSut(
            new EntraAuthOptions { AdditionalAudiences = [extra] },
            BuildConfig(clientId));
        var jwtOptions = new JwtBearerOptions();

        sut.PostConfigure(JwtBearerDefaults.AuthenticationScheme, jwtOptions);

        jwtOptions.TokenValidationParameters.ValidAudiences.ShouldBe(new[] { clientId, extra }, ignoreOrder: true);
    }

    [Fact]
    public void PostConfigure_IgnoresOtherAuthenticationSchemes()
    {
        var sut = CreateSut(new EntraAuthOptions(), BuildConfig(Guid.NewGuid().ToString()));
        var jwtOptions = new JwtBearerOptions();

        sut.PostConfigure("SomeOtherScheme", jwtOptions);

        jwtOptions.TokenValidationParameters.ValidAudiences.ShouldBeNull();
    }

    [Fact]
    public void PostConfigure_DoesNotInstallIssuerValidator_ForSingleTenant()
    {
        var sut = CreateSut(
            new EntraAuthOptions { Tenancy = TenancyMode.SingleTenant },
            BuildConfig(Guid.NewGuid().ToString()));
        var jwtOptions = new JwtBearerOptions();

        sut.PostConfigure(JwtBearerDefaults.AuthenticationScheme, jwtOptions);

        jwtOptions.TokenValidationParameters.IssuerValidator.ShouldBeNull();
    }

    [Fact]
    public void PostConfigure_InstallsIssuerValidator_ForMultiTenant()
    {
        var sut = CreateSut(
            new EntraAuthOptions
            {
                Tenancy = TenancyMode.MultiTenant,
                AllowedTenantIds = [Guid.NewGuid().ToString()],
            },
            BuildConfig(Guid.NewGuid().ToString()));
        var jwtOptions = new JwtBearerOptions();

        sut.PostConfigure(JwtBearerDefaults.AuthenticationScheme, jwtOptions);

        jwtOptions.TokenValidationParameters.IssuerValidator.ShouldNotBeNull();
    }

    [Fact]
    public void PostConfigure_Throws_WhenMultiTenantHasEmptyAllowList()
    {
        var sut = CreateSut(
            new EntraAuthOptions
            {
                Tenancy = TenancyMode.MultiTenant,
                AllowedTenantIds = [],
            },
            BuildConfig(Guid.NewGuid().ToString()));
        var jwtOptions = new JwtBearerOptions();

        Should.Throw<InvalidOperationException>(
            () => sut.PostConfigure(JwtBearerDefaults.AuthenticationScheme, jwtOptions));
    }
}

public sealed class RequireClientAppAttributeTests
{
    [Fact]
    public void Attribute_DerivesAuthorizeAttribute_AndHardcodesPolicyName()
    {
        var attr = new RequireClientAppAttribute();
        attr.ShouldBeAssignableTo<AuthorizeAttribute>();
        attr.Policy.ShouldBe("EntraAuth:RequireClientApp");
    }
}
