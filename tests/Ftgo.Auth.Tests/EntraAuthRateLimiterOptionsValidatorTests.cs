using Ftgo.Auth;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Ftgo.Auth.Tests;

public sealed class EntraAuthRateLimiterOptionsValidatorTests
{
    [Fact]
    public void AddEntraAuthRateLimiter_Throws_WhenPermitLimit_NonPositive()
    {
        var services = new ServiceCollection();

        var ex = Should.Throw<ArgumentException>(() =>
            services.AddEntraAuthRateLimiter(o => o.PermitLimit = 0));

        ex.Message.ShouldContain(nameof(EntraAuthRateLimiterOptions.PermitLimit));
    }

    [Fact]
    public void AddEntraAuthRateLimiter_Throws_WhenAppPermitLimit_NonPositive()
    {
        var services = new ServiceCollection();

        var ex = Should.Throw<ArgumentException>(() =>
            services.AddEntraAuthRateLimiter(o => o.AppPermitLimit = -1));

        ex.Message.ShouldContain(nameof(EntraAuthRateLimiterOptions.AppPermitLimit));
    }

    [Fact]
    public void AddEntraAuthRateLimiter_Throws_WhenWindow_Zero()
    {
        var services = new ServiceCollection();

        var ex = Should.Throw<ArgumentException>(() =>
            services.AddEntraAuthRateLimiter(o => o.Window = TimeSpan.Zero));

        ex.Message.ShouldContain(nameof(EntraAuthRateLimiterOptions.Window));
    }

    [Fact]
    public void AddEntraAuthRateLimiter_Succeeds_WithDefaults()
    {
        var services = new ServiceCollection();
        Should.NotThrow(() => services.AddEntraAuthRateLimiter());
    }
}
