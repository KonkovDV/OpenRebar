using FluentAssertions;

namespace OpenRebar.Cli.Tests;

public class CliLayerAssignmentTests
{
  [Fact]
  public async Task Cli_MissingLayer_ReturnsError()
  {
    var root = ResolveRepositoryRoot();
    var source = Path.Combine(root, "examples", "dxf", "simple-slab", "input.dxf");
    var tempDirectory = Path.Combine(Path.GetTempPath(), $"OpenRebar-layer-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDirectory);
    var input = Path.Combine(tempDirectory, "input.dxf");
    File.Copy(source, input);

    try
    {
      var exitCode = await global::OpenRebar.Cli.Program.Main([
          input,
          "--thickness", "220",
          "--cover", "30",
          "--slab-width", "6000",
          "--slab-height", "4000"
      ]);

      exitCode.Should().Be(1);
    }
    finally
    {
      if (Directory.Exists(tempDirectory))
        Directory.Delete(tempDirectory, recursive: true);
    }
  }

  private static string ResolveRepositoryRoot()
  {
    var current = AppContext.BaseDirectory;
    while (!string.IsNullOrWhiteSpace(current))
    {
      if (File.Exists(Path.Combine(current, "OpenRebar.sln")))
        return current;

      current = Directory.GetParent(current)?.FullName ?? "";
    }

    throw new InvalidOperationException("Could not resolve repository root from test base directory.");
  }
}
