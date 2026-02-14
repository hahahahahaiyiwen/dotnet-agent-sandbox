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
}
