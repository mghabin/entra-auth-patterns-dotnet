using System.ComponentModel.DataAnnotations;

namespace Ftgo.Auth;

/// <summary>
/// Config for workers that call a single downstream API with an app-only token.
/// Bound from the "Downstream" section by default.
/// </summary>
public sealed class DownstreamApiOptions
{
    public const string DefaultSectionName = "Downstream";

    [Required]
    [Url]
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// The <c>.default</c> scope for the downstream app, e.g.
    /// <c>api://11111111-2222-3333-4444-555555555555/.default</c>.
    /// </summary>
    [Required]
    public string Scope { get; init; } = string.Empty;

    [Required]
    public string ProbePath { get; init; } = "api/orders/system";
}
