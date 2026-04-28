using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace Ftgo.Auth;

public static class EntraAuthOpenApiExtensions
{
    public static IServiceCollection AddEntraAuthOpenApi(
        this IServiceCollection services,
        IConfiguration configuration,
        string scopeName)
    {
        var tenantId = configuration["AzureAd:TenantId"]!;
        var clientId = configuration["AzureAd:ClientId"]!;

        var fullScope = $"api://{clientId}/{scopeName}";

        services.AddOpenApi("v1", options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
                document.Components.SecuritySchemes["entra"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.OAuth2,
                    Description = "Microsoft Entra ID — Authorization Code + PKCE",
                    Flows = new OpenApiOAuthFlows
                    {
                        AuthorizationCode = new OpenApiOAuthFlow
                        {
                            AuthorizationUrl = new System.Uri($"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize"),
                            TokenUrl = new System.Uri($"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token"),
                            Scopes = new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                [fullScope] = scopeName,
                            },
                        },
                    },
                };

                document.Security ??= [];
                document.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("entra", document)] = [fullScope],
                });
                return Task.CompletedTask;
            });
        });

        return services;
    }

    public static IEndpointRouteBuilder MapEntraAuthScalar(
        this IEndpointRouteBuilder endpoints,
        IConfiguration configuration,
        string scopeName)
    {
        var clientId = configuration["AzureAd:ClientId"]!;
        // OpenAPI doc + Scalar UI are documentation surfaces; FallbackPolicy would otherwise
        // make them require auth. Mark anonymous so the "browse to /scalar/v1" demo flow
        // works. The protected-by-Entra resources still require a token before any operation.
        endpoints.MapOpenApi().AllowAnonymous();
        endpoints.MapScalarApiReference(opt =>
        {
            opt.AddPreferredSecuritySchemes("entra")
               .AddAuthorizationCodeFlow("entra", flow =>
               {
                   flow.ClientId = clientId;
                   flow.Pkce = Pkce.Sha256;
                   flow.SelectedScopes = [$"api://{clientId}/{scopeName}"];
               });
        }).AllowAnonymous();
        return endpoints;
    }
}
