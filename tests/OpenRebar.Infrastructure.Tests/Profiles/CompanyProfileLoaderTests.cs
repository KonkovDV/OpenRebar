using System.Text.Json;
using FluentAssertions;
using Json.Schema;
using OpenRebar.Domain.Exceptions;
using OpenRebar.Infrastructure.Profiles;

namespace OpenRebar.Infrastructure.Tests.Profiles;

public class CompanyProfileLoaderTests
{
  [Fact]
  public void Profile_Inheritance_KeepsGenericSteelAndOverridesStock()
  {
    var loaded = CompanyProfileLoader.Load(Profile("examples", "residential-monolith.json"));

    loaded.Profile.Id.Should().Be("residential-monolith");
    loaded.Profile.Example.Should().BeTrue();
    loaded.Profile.SteelClass.Should().Be("A500C");
    loaded.Profile.Supply.StockLengthsMm.Should().Equal(11700);
    loaded.Profile.Safety.Factor.Should().Be(1);
    loaded.Profile.Safety.AnchorageLengthFactor.Should().Be(1);
    loaded.Profile.Legend.Should().HaveCount(7);
  }

  [Fact]
  public void Profile_UnknownField_Fails()
  {
    string path = Write("""
      {
        "id": "bad",
        "version": "1",
        "extends": "generic",
        "favoriteColor": "blue"
      }
      """);

    var act = () => CompanyProfileLoader.Load(path);
    act.Should().Throw<CompanyProfileLoadException>().WithMessage("*favoriteColor*");
  }

  [Theory]
  [InlineData("safety", """{ "factor": 0.9 }""", "*safety.factor*")]
  [InlineData("safety-spacing", """{ "factor": 1, "anchorageLengthFactor": 1, "lapLengthFactor": 1, "maxSpacingMm": 500 }""", "*maxSpacing*")]
  [InlineData("anchorage", """{ "factor": 1, "anchorageLengthFactor": 0.8, "lapLengthFactor": 1 }""", "*anchorageLengthFactor*")]
  public void Profile_CannotWeakenNormative(string name, string safety, string message)
  {
    string path = Write($$"""
      {
        "id": "{{name}}",
        "version": "1",
        "extends": "generic",
        "safety": {{safety}}
      }
      """);

    var act = () => CompanyProfileLoader.Load(path);
    act.Should().Throw<CompanyProfileLoadException>().WithMessage(message);
  }

  [Fact]
  public void Profile_Diff_ShowsOverriddenStock()
  {
    var lines = CompanyProfileLoader.Diff(
        Profile("generic.json"),
        Profile("examples", "residential-monolith.json"));

    lines.Should().Contain(line => line.Contains("stockLengthsMm", StringComparison.Ordinal));
  }

  [Fact]
  public void GenericProfile_MatchesSchema()
  {
    var schema = JsonSchema.FromFile(Path.Combine(RepoRoot(), "contracts", "company-profile.schema.json"));
    using var document = JsonDocument.Parse(File.ReadAllText(Profile("generic.json")));
    schema.Evaluate(document.RootElement).IsValid.Should().BeTrue();
  }

  private static string Profile(params string[] parts)
  {
    return Path.Combine(new[] { RepoRoot(), "profiles" }.Concat(parts).ToArray());
  }

  private static string RepoRoot()
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

  private static string Write(string json)
  {
    string path = Path.Combine(Path.GetTempPath(), $"openrebar-profile-{Guid.NewGuid():N}.json");
    File.WriteAllText(path, json);
    return path;
  }
}
