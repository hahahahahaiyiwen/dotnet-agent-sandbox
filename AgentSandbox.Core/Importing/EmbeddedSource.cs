using System.Reflection;

namespace AgentSandbox.Core.Importing;

/// <summary>
/// Loads files from embedded resources in an assembly.
/// Can be used for importing any files (skills, data, templates, etc.) into the sandbox.
/// </summary>
public class EmbeddedSource : IFileSource
{
    private static readonly HashSet<string> CompoundTwoPartExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "tar.gz",
        "tar.bz2",
        "tar.xz",
        "tar.zst",
        "d.ts",
        "min.js",
        "min.css",
        "bundle.js",
        "bundle.css",
        "spec.js",
        "spec.ts",
        "test.js",
        "test.ts"
    };

    private static readonly HashSet<string> CompoundThreePartExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "d.ts.map",
        "min.js.map",
        "min.css.map"
    };

    private readonly Assembly _assembly;
    private readonly string _resourcePrefix;

    /// <summary>
    /// Creates a source from embedded resources.
    /// </summary>
    /// <param name="assembly">The assembly containing embedded resources.</param>
    /// <param name="resourcePrefix">
    /// The resource name prefix (e.g., "MyApp.Resources.Data").
    /// Resources should be named like "MyApp.Resources.Data.config.json".
    /// </param>
    public EmbeddedSource(Assembly assembly, string resourcePrefix)
    {
        _assembly = assembly;
        _resourcePrefix = resourcePrefix.TrimEnd('.');
    }

    public IEnumerable<FileData> GetFiles()
    {
        var prefix = _resourcePrefix + ".";
        var resourceNames = _assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        foreach (var resourceName in resourceNames)
        {
            using var stream = _assembly.GetManifestResourceStream(resourceName);
            if (stream == null) continue;

            using var ms = new MemoryStream();
            stream.CopyTo(ms);

            // Convert resource name to relative path
            // e.g., "MyApp.Resources.Data.scripts.setup.sh" -> "scripts/setup.sh"
            var relativePath = ConvertResourceNameToPath(resourceName, prefix);

            yield return new FileData
            {
                RelativePath = relativePath,
                Content = ms.ToArray()
            };
        }
    }

    private static string ConvertResourceNameToPath(string resourceName, string prefix)
    {
        // Remove prefix
        var path = resourceName[prefix.Length..];

        // Handle file extension - the last dot before extension is the real separator
        // e.g., "scripts.setup.sh" -> "scripts/setup.sh"
        // e.g., "config.json" -> "config.json"
        
        var parts = path.Split('.');
        if (parts.Length <= 2)
        {
            // Simple case: "config.json" or just "file"
            return path;
        }

        // Rebuild path: all parts except filename segments are directories.
        // Default filename shape is name.ext (2 segments), with support for common
        // compound extensions such as .tar.gz and .d.ts.
        var fileSegmentCount = 2;
        if (parts.Length >= 4)
        {
            var threePartExtension = $"{parts[^3]}.{parts[^2]}.{parts[^1]}";
            if (CompoundThreePartExtensions.Contains(threePartExtension))
            {
                fileSegmentCount = 4;
            }
        }

        if (fileSegmentCount == 2 && parts.Length >= 3)
        {
            var twoPartExtension = $"{parts[^2]}.{parts[^1]}";
            if (CompoundTwoPartExtensions.Contains(twoPartExtension))
            {
                fileSegmentCount = 3;
            }
        }

        var dirs = parts.Take(parts.Length - fileSegmentCount);
        var fileName = string.Join(".", parts.Skip(parts.Length - fileSegmentCount));

        var dirPath = string.Join("/", dirs);
        return string.IsNullOrEmpty(dirPath) ? fileName : $"{dirPath}/{fileName}";
    }
}
