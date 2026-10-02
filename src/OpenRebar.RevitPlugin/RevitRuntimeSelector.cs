namespace OpenRebar.RevitPlugin;

/// <summary>
/// Chooses the add-in folder from the Revit process runtime.
/// A .addin file cannot branch, and this repo does not reference the Revit API,
/// so the install is two folders. This rule is what a loader would apply.
/// </summary>
public static class RevitRuntimeSelector
{
  public const string Net8Folder = "RevitNet8";
  public const string Net10Folder = "RevitNet10";

  /// <summary>
  /// .NET 10 and later (Revit 2027, and 2025/2026 after the runtime update) use RevitNet10.
  /// An un-updated Revit 2025/2026 reports major 8 and uses RevitNet8.
  /// </summary>
  public static string Select(Version version)
  {
    ArgumentNullException.ThrowIfNull(version);
    return version.Major >= 10 ? Net10Folder : Net8Folder;
  }
}
