using System.Reflection;

namespace FullRumble;

internal static class ProductInfo
{
    public static string ReleaseName =>
        Assembly.GetEntryAssembly()?.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "FullRumble.ReleaseName")?.Value
        ?? "Development build";
}
