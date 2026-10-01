using System.Reflection;

namespace OpenRebar.Domain;

/// <summary>
/// Product version taken from <see cref="AssemblyInformationalVersionAttribute"/>.
/// </summary>
public static class ProductVersion
{
  public static string Current { get; } = Read();

  private static string Read()
  {
    string? raw = typeof(ProductVersion).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion;
    if (string.IsNullOrWhiteSpace(raw))
      return "1.0.0";

    int plus = raw.IndexOf('+', StringComparison.Ordinal);
    return plus >= 0 ? raw[..plus] : raw;
  }
}
