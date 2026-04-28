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

/// <summary>One-line bootstrap (<c>AddEntraAuth</c>) so every Entra-protected API in the sample wires the same way.</summary>
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
            // Deny-by-default: every endpoint requires an authenticated user unless it
            // explicitly opts out with [AllowAnonymous] (e.g. health probes, OpenAPI doc,
            // Scalar UI in dev). Eng-guide SECURITY/must.
            o.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

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
                // AadIssuerValidator enforces the issuer is a known Microsoft signing authority and that
                // the issuer's {tenantid} segment matches the token's tid. We then enforce our allow-list.
                var validatedIssuer = aadIssuerValidator.Validate(issuer, token, parameters);

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
