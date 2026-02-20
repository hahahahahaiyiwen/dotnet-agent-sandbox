namespace AgentSandbox.Core.Security;

/// <summary>
/// Host-defined policy constraints for command-level secret usage.
/// </summary>
public class SecretResolutionPolicy
{
    /// <summary>
    /// Optional global allowlist of secret references. When set, only these refs can be resolved.
    /// </summary>
    public ISet<string>? AllowedRefs { get; set; }

    /// <summary>
    /// Optional max age for resolved secrets. Requires broker to provide <see cref="ResolvedSecret.ResolvedAt"/>.
    /// </summary>
    public TimeSpan? MaxSecretAge { get; set; }

    /// <summary>
    /// Optional hook used by network-enabled commands to validate outbound egress host usage for secret-bearing requests.
    /// Return true to allow, false to deny.
    /// </summary>
    public Func<SecretEgressContext, bool>? EgressHostAllowlistHook { get; set; }
}

/// <summary>
/// Per-command secret access context passed by shell extensions.
/// </summary>
public sealed class SecretAccessRequest
{
    /// <summary>
    /// Optional command-level allowlist. When set, secret refs outside this set are denied.
    /// </summary>
    public ISet<string>? AllowedRefs { get; init; }

    /// <summary>
    /// Optional destination URI for network-backed secret usage.
    /// </summary>
    public Uri? DestinationUri { get; init; }

    /// <summary>
    /// Optional command name for diagnostics.
    /// </summary>
    public string? CommandName { get; init; }
}

/// <summary>
/// Egress evaluation payload for host-defined allowlist hooks.
/// </summary>
/// <param name="SecretRef">Secret reference requested by the command.</param>
/// <param name="DestinationUri">Outbound destination URI associated with secret usage.</param>
/// <param name="CommandName">Command name requesting secret usage.</param>
public readonly record struct SecretEgressContext(string SecretRef, Uri DestinationUri, string? CommandName = null);
