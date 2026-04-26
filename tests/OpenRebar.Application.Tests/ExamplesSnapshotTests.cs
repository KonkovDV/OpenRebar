using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;

namespace OpenRebar.Application.Tests;

public class ExamplesSnapshotTests
{
  [Theory]
  [InlineData("examples/dxf/simple-slab/input.dxf", "--thickness", "220", "--cover", "30", "--slab-width", "6000", "--slab-height", "4000")]
  [InlineData("examples/png/simple-slab/input.png", "--thickness", "220", "--cover", "30", "--slab-width", "6000", "--slab-height", "4000")]
  public async Task CliExample_ShouldMatchExpectedSnapshots(string relativeInputPath, params string[] extraArgs)
  {
    var repoRoot = ResolveRepositoryRoot();
    var sourceInputPath = Path.Combine(repoRoot, relativeInputPath);
    var expectedDirectory = Path.Combine(Path.GetDirectoryName(sourceInputPath)!, "expected");
    var expectedResultPath = Path.Combine(expectedDirectory, "input.result.json");
    var expectedSchedulePath = Path.Combine(expectedDirectory, "input.schedule.csv");

    File.Exists(sourceInputPath).Should().BeTrue("example input must exist in repository");
    File.Exists(expectedResultPath).Should().BeTrue("expected canonical report snapshot must exist");
    File.Exists(expectedSchedulePath).Should().BeTrue("expected schedule snapshot must exist");

    var tempDirectory = Path.Combine(Path.GetTempPath(), $"OpenRebar-example-snapshot-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDirectory);

    try
    {
      var tempInputPath = Path.Combine(tempDirectory, Path.GetFileName(sourceInputPath));
      File.Copy(sourceInputPath, tempInputPath, overwrite: true);

      var args = new[] { tempInputPath }.Concat(extraArgs).ToArray();
      var exitCode = await global::OpenRebar.Cli.Program.Main(args);
      exitCode.Should().Be(0, "CLI example execution should succeed");

      var actualResultPath = Path.ChangeExtension(tempInputPath, ".result.json");
      var actualSchedulePath = Path.ChangeExtension(tempInputPath, ".schedule.csv");

      File.Exists(actualResultPath).Should().BeTrue();
      File.Exists(actualSchedulePath).Should().BeTrue();

      var expectedResultText = await File.ReadAllTextAsync(expectedResultPath);
      var actualResultText = await File.ReadAllTextAsync(actualResultPath);

      NormalizeDynamicUtcFields(expectedResultText)
        .Should().Be(NormalizeDynamicUtcFields(actualResultText));

      var expectedScheduleText = await File.ReadAllTextAsync(expectedSchedulePath);
      var actualScheduleText = await File.ReadAllTextAsync(actualSchedulePath);

      NormalizeLineEndings(expectedScheduleText)
        .Should().Be(NormalizeLineEndings(actualScheduleText));
    }
    finally
    {
      if (Directory.Exists(tempDirectory))
        Directory.Delete(tempDirectory, recursive: true);
    }
  }

  private static string NormalizeDynamicUtcFields(string json)
  {
    var root = JsonNode.Parse(json) ?? throw new InvalidOperationException("Invalid JSON snapshot.");
    NormalizeDynamicUtcFieldsRecursive(root);
    return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
  }

  private static void NormalizeDynamicUtcFieldsRecursive(JsonNode node)
  {
    if (node is JsonObject jsonObject)
    {
      foreach (var property in jsonObject.ToList())
      {
        if (property.Key.EndsWith("Utc", StringComparison.OrdinalIgnoreCase))
        {
          jsonObject[property.Key] = "__UTC_DYNAMIC__";
          continue;
        }

        if (property.Key.Equals("stackTrace", StringComparison.OrdinalIgnoreCase))
        {
          jsonObject[property.Key] = "__STACKTRACE_DYNAMIC__";
          continue;
        }

        if (property.Value is not null)
          NormalizeDynamicUtcFieldsRecursive(property.Value);
      }

      return;
    }

    if (node is JsonArray jsonArray)
    {
      foreach (var item in jsonArray)
      {
        if (item is not null)
          NormalizeDynamicUtcFieldsRecursive(item);
      }
    }
  }

  private static string NormalizeLineEndings(string value)
  {
    return value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
  }

  private static string ResolveRepositoryRoot()
  {
    var current = AppContext.BaseDirectory;
    while (!string.IsNullOrWhiteSpace(current))
    {
      if (File.Exists(Path.Combine(current, "OpenRebar.sln")))
        return current;

      var parent = Directory.GetParent(current);
      if (parent is null)
        break;

      current = parent.FullName;
    }

    throw new InvalidOperationException("Could not resolve repository root from test base directory.");
  }
}
