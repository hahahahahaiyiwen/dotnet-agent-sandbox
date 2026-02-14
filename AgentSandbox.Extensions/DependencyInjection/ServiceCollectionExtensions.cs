using AgentSandbox.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentSandbox.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering AgentSandbox services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds AgentSandbox services to the service collection.
    /// Registers SandboxManager as singleton and Sandbox as scoped.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional action to configure sandbox options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAgentSandbox(
        this IServiceCollection services,
        Action<SandboxOptions>? configure = null)
    {
        var options = new SandboxOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(_ => new SandboxManager(options));
        services.TryAddScoped<Sandbox>(sp =>
        {
            var manager = sp.GetRequiredService<SandboxManager>();
            return manager.Get();
        });

        return services;
    }

    /// <summary>
    /// Adds AgentSandbox services with a factory for dynamic options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="optionsFactory">Factory to create sandbox options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAgentSandbox(
        this IServiceCollection services,
        Func<IServiceProvider, SandboxOptions> optionsFactory)
    {
        services.TryAddSingleton(sp =>
        {
            var options = optionsFactory(sp);
            return new SandboxManager(options);
        });

        services.TryAddScoped<Sandbox>(sp =>
        {
            var manager = sp.GetRequiredService<SandboxManager>();
            return manager.Get();
        });

        return services;
    }

    /// <summary>
    /// Adds a transient Sandbox (new instance per request, not managed by SandboxManager).
    /// Use this when you need independent sandbox instances.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional action to configure sandbox options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTransientSandbox(
        this IServiceCollection services,
        Action<SandboxOptions>? configure = null)
    {
        services.AddTransient<Sandbox>(sp =>
        {
            var options = new SandboxOptions();
            configure?.Invoke(options);
            return new Sandbox(options: options);
        });

        return services;
    }

    /// <summary>
    /// Adds SandboxManager as a singleton without scoped Sandbox registration.
    /// Use this when you need manual control over sandbox lifecycle.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional action to configure default sandbox options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSandboxManager(
        this IServiceCollection services,
        Action<SandboxOptions>? configure = null)
    {
        var options = new SandboxOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(_ => new SandboxManager(options));

        return services;
    }
}
