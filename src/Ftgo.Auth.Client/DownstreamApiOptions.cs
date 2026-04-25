using System.ComponentModel.DataAnnotations;

namespace Ftgo.Auth;

public sealed class DownstreamApiOptions
{
    public const string DefaultSectionName = "Downstream";

    [Required]
    [Url]
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>The <c>.default</c> scope for the downstream app, e.g. <c>api://&lt;app-id&gt;/.default</c>.</summary>
    [Required]
    public string Scope { get; init; } = string.Empty;

    [Required]
    public string ProbePath { get; init; } = "api/orders/system";
}
