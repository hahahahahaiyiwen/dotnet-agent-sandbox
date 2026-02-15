using AgentSandbox.Core.Telemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace AgentSandbox.Extensions.Observability;

/// <summary>
/// Extension methods for integrating AgentSandbox with OpenTelemetry.
/// </summary>
public static class OpenTelemetryExtensions
{
    /// <summary>
    /// Adds AgentSandbox tracing instrumentation to the TracerProviderBuilder.
    /// This enables distributed tracing for sandbox command execution.
    /// </summary>
    /// <param name="builder">The TracerProviderBuilder.</param>
    /// <returns>The builder for chaining.</returns>
    /// <example>
    /// <code>
    /// var tracerProvider = Sdk.CreateTracerProviderBuilder()
    ///     .AddSandboxInstrumentation()
    ///     .AddOtlpExporter()
    ///     .Build();
    /// </code>
    /// </example>
    public static TracerProviderBuilder AddSandboxInstrumentation(this TracerProviderBuilder builder)
    {
        return builder.AddSource(SandboxTelemetryHelper.ServiceName);
    }

    /// <summary>
    /// Adds AgentSandbox metrics instrumentation to the MeterProviderBuilder.
    /// This enables metrics collection for sandbox operations.
    /// </summary>
    /// <param name="builder">The MeterProviderBuilder.</param>
    /// <returns>The builder for chaining.</returns>
    /// <example>
    /// <code>
    /// var meterProvider = Sdk.CreateMeterProviderBuilder()
    ///     .AddSandboxInstrumentation()
    ///     .AddOtlpExporter()
    ///     .Build();
    /// </code>
    /// </example>
    public static MeterProviderBuilder AddSandboxInstrumentation(this MeterProviderBuilder builder)
    {
        return builder.AddMeter(SandboxTelemetryHelper.ServiceName);
    }
}

