using FluentAssertions;
using OpenRebar.RevitPlugin;

namespace OpenRebar.Application.Tests;

public class RevitRuntimeSelectorTests
{
  [Fact]
  public void Select_Net8Runtime_UsesRevitNet8()
  {
    RevitRuntimeSelector.Select(new Version(8, 0, 21)).Should().Be(RevitRuntimeSelector.Net8Folder);
  }

  [Fact]
  public void Select_Net10Runtime_UsesRevitNet10()
  {
    RevitRuntimeSelector.Select(new Version(10, 0, 0)).Should().Be(RevitRuntimeSelector.Net10Folder);
  }

  [Fact]
  public void AddInManifests_NameThePluginAssembly()
  {
    string root = RepoRoot();
    foreach (string folder in new[] { RevitRuntimeSelector.Net8Folder, RevitRuntimeSelector.Net10Folder })
    {
      string text = File.ReadAllText(Path.Combine(
          root, "src", "OpenRebar.RevitPlugin", "addin", folder, "OpenRebar.addin"));
      string other = folder == RevitRuntimeSelector.Net8Folder
          ? RevitRuntimeSelector.Net10Folder
          : RevitRuntimeSelector.Net8Folder;
      text.Should().Contain(folder);
      text.Should().NotContain(other);
      text.Should().Contain("<Assembly>OpenRebar.RevitPlugin.dll</Assembly>");
      text.Should().Contain("OpenRebar.RevitPlugin.OpenRebarApplication");
      text.Should().Contain("OpenRebar.RevitPlugin.GenerateReinforcementCommand");
    }
  }

  private static string RepoRoot()
  {
    var current = AppContext.BaseDirectory;
    while (!string.IsNullOrWhiteSpace(current))
    {
      if (File.Exists(Path.Combine(current, "OpenRebar.sln")))
        return current;
      current = Path.GetDirectoryName(current);
    }

    throw new InvalidOperationException("Repository root was not found.");
  }
}
