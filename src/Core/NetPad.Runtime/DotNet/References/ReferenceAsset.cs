using NetPad.Assemblies;

namespace NetPad.DotNet.References;

/// <summary>
/// Represents a file asset that belongs to a <see cref="Reference"/>. It may or may not be an assembly.
/// </summary>
/// <param name="Path">The absolute path to the asset.</param>
public record ReferenceAsset(string Path)
{
    private bool? _isManagedAssembly;

    public bool IsAnalyzerAsset
    {
        get
        {
            if (!Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return false;

            var segments = Path.Replace('\\', '/').Split('/');
            for (var index = 0; index + 2 < segments.Length; index++)
            {
                if (segments[index].Equals("analyzers", StringComparison.OrdinalIgnoreCase)
                    && segments[index + 1].Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                    && segments[index + 2].Equals("cs", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Gets a value indicating whether this asset is a managed .NET assembly.
    /// </summary>
    public bool IsManagedAssembly => _isManagedAssembly ??= AssemblyInfoReader.IsManaged(Path);
}
