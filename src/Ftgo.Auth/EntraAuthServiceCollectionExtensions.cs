using System.Globalization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.Validators;

namespace Ftgo.Auth;

/// <summary>
/// One-line bootstrap for Entra-protected APIs in this sample. In a real org this would
/// live in a NuGet package (e.g. <c>EntraAuth.Auth</c>). Every service calls:
/// <code>builder.Services.AddEntraAuth(builder.Configuration);</code>
/// </summary>
public static class EntraAuthServiceCollectionExtensions
{
    public static IServiceCollection AddEntraAuth(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<MicrosoftIdentityWebApiAuthenticationBuilder>? configureAuthentication = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<EntraAuthOptions>()
            .Bind(configuration.GetSection(EntraAuthOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<EntraAuthOptions>, EntraAuthOptionsValidator>();

        var authBuilder = services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAd"));

        configureAuthentication?.Invoke(authBuilder);

        services.AddSingleton<IPostConfigureOptions<JwtBearerOptions>, EntraAuthJwtPostConfigure>();

        services.AddAuthorization(o =>
        {
            o.AddPolicy(ClientAppPolicy.Name, p =>
            {
                p.RequireAuthenticatedUser();
                p.Requirements.Add(new RequireClientAppRequirement());
            });
        });
        services.AddSingleton<IAuthorizationHandler, RequireClientAppHandler>();

        return services;
    }
}

/// <summary>
/// Post-configures the JwtBearer options set up by Microsoft.Identity.Web to:
///   1. Pin audience to the API's client ID (v2 default).
///   2. Enforce a tenant allow-list for multi-tenant APIs.
/// </summary>
internal sealed class EntraAuthJwtPostConfigure(
    IOptions<EntraAuthOptions> options,
    IConfiguration configuration)
    : IPostConfigureOptions<JwtBearerOptions>
{
    public void PostConfigure(string? name, JwtBearerOptions bearerOptions)
    {
        if (!string.Equals(name, JwtBearerDefaults.AuthenticationScheme, StringComparison.Ordinal))
        {
            return;
        }

        var opts = options.Value;
        var clientId = configuration["AzureAd:ClientId"];

        var validAudiences = new List<string>();
        if (!string.IsNullOrWhiteSpace(clientId)) validAudiences.Add(clientId);
        validAudiences.AddRange(opts.AdditionalAudiences);
        if (validAudiences.Count > 0)
        {
            bearerOptions.TokenValidationParameters.ValidAudiences = validAudiences;
        }

        if (opts.Tenancy == TenancyMode.MultiTenant)
        {
            if (opts.AllowedTenantIds.Count == 0)
            {
                throw new InvalidOperationException(
                    "EntraAuth.AllowedTenantIds must contain at least one tenant id when Tenancy is MultiTenant.");
            }

            var allowed = new HashSet<string>(opts.AllowedTenantIds, StringComparer.OrdinalIgnoreCase);
            var instance = (configuration["AzureAd:Instance"] ?? "https://login.microsoftonline.com/").TrimEnd('/');
            var aadAuthority = $"{instance}/{configuration["AzureAd:TenantId"] ?? "organizations"}/v2.0";
            var aadIssuerValidator = AadIssuerValidator.GetAadIssuerValidator(aadAuthority);

            bearerOptions.TokenValidationParameters.IssuerValidator = (issuer, token, parameters) =>
            {
                // 1. Delegate the cryptographic + structural issuer check to Microsoft.IdentityModel.Validators.
                //    This enforces issuer is a known Microsoft signing authority AND that the issuer's
                //    {tenantid} segment matches the token's tid claim.
                var validatedIssuer = aadIssuerValidator.Validate(issuer, token, parameters);

                // 2. Then enforce our tenant allow-list with exact-match equality.
                if (token is not JsonWebToken jwt)
                {
                    throw new SecurityTokenInvalidIssuerException(
                        string.Create(CultureInfo.InvariantCulture,
                            $"Unsupported token type '{token?.GetType().FullName}' for multi-tenant validation."));
                }

                var tid = jwt.GetPayloadValue<string>("tid");
                if (string.IsNullOrEmpty(tid) || !allowed.Contains(tid))
                {
                    throw new SecurityTokenInvalidIssuerException(
                        string.Create(CultureInfo.InvariantCulture, $"Tenant '{tid}' is not provisioned."));
                }

                return validatedIssuer;
            };
        }
    }
}
