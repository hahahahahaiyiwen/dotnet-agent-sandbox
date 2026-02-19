namespace AgentSandbox.Core.Security;

/// <summary>
/// Resolves host-managed secret references for sandbox commands at execution time.
/// </summary>
public interface ISecretBroker
{
    /// <summary>
    /// Resolves a secret value by reference.
    /// </summary>
    /// <param name="secretRef">Host-defined secret reference identifier.</param>
    /// <param name="secretValue">Resolved secret value when found.</param>
    /// <returns>True when resolved; otherwise false.</returns>
    bool TryResolve(string secretRef, out string secretValue);

    /// <summary>
    /// Resolves a secret value by reference, optionally including metadata used for policy checks.
    /// </summary>
    /// <param name="secretRef">Host-defined secret reference identifier.</param>
    /// <param name="secret">Resolved secret payload when found.</param>
    /// <returns>True when resolved; otherwise false.</returns>
    bool TryResolve(string secretRef, out ResolvedSecret secret)
    {
        if (TryResolve(secretRef, out string secretValue))
        {
            secret = new ResolvedSecret(secretValue);
            return true;
        }

        secret = default;
        return false;
    }
}

/// <summary>
/// Resolved secret payload and optional metadata for policy enforcement.
/// </summary>
/// <param name="Value">Resolved secret value.</param>
/// <param name="ResolvedAt">Timestamp when the secret was issued or refreshed by the broker.</param>
public readonly record struct ResolvedSecret(string Value, DateTimeOffset? ResolvedAt = null);
