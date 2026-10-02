using System.Text.Json;
using FluentAssertions;

namespace OpenRebar.Cli.Tests;

public class CliProjectExampleTests
{
  [Fact]
  public async Task Main_DesignFile_RunsTheExampleLayer()
  {
    string dxf = Path.Combine(RepositoryRoot(), "examples", "dxf", "simple-slab", "input.dxf");
    var directory = Path.Combine(Path.GetTempPath(), $"OpenRebar-project-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    string projectPath = Path.Combine(directory, "slab.project.json");
    await File.WriteAllTextAsync(projectPath, $$"""
    {
      "slabId": "simple-slab",
      "norm": "ru.sp63.2018",
      "slab": { "widthMm": 6000, "heightMm": 4000, "thicknessMm": 220, "coverMm": 30, "concreteClass": "B25" },
      "layers": [ { "layer": "BottomX", "file": {{JsonPath(dxf)}} } ]
    }
    """);

    try
    {
      var exitCode = await global::OpenRebar.Cli.Program.Main(["--design", projectPath]);
      exitCode.Should().Be(2);
      File.Exists(Path.ChangeExtension(projectPath, ".result.json")).Should().BeTrue();
    }
    finally
    {
      if (Directory.Exists(directory))
        Directory.Delete(directory, recursive: true);
    }
  }

  [Fact]
  public async Task Main_MissingProfileOrUnknownNorm_ReturnsInputError()
  {
    string dxf = Path.Combine(RepositoryRoot(), "examples", "dxf", "simple-slab", "input.dxf");
    (await global::OpenRebar.Cli.Program.Main([dxf, "--layer", "BottomX", "--profile", "missing-profile.json"]))
        .Should().Be(1);
    (await global::OpenRebar.Cli.Program.Main([dxf, "--layer", "BottomX", "--norm", "aci318"]))
        .Should().Be(1);
  }

  private static string JsonPath(string path) =>
      JsonSerializer.Serialize(path);

  private static string RepositoryRoot()
  {
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
      if (File.Exists(Path.Combine(current.FullName, "OpenRebar.sln")))
        return current.FullName;
      current = current.Parent;
    }

    throw new InvalidOperationException("Repository root was not found.");
  }
}
