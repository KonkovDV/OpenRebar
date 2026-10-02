using OpenRebar.Domain.Models;
using FluentAssertions;
using OpenRebar.Domain.Rules;

namespace OpenRebar.Domain.Tests.Rules;

public class NormativeProfilesTests
{
  [Fact(DisplayName = "SP 63 §1.1 — Normative Profile Metadata")]
  public void DefaultProfile_ShouldExposeStableMetadata()
  {
    var profile = NormativeProfiles.Sp63_2018;

    profile.ProfileId.Should().Be("ru.sp63.2018+A1+A2");
    NormativeProfiles.DefaultProfileId.Should().Be("ru.sp63.2018");
    profile.Jurisdiction.Should().Be("RU");
    profile.DesignCode.Should().Be("SP 63.13330.2018");
    profile.TablesVersion.Should().Be("ru.sp63.2018.tables.v3");
    profile.DesignStrengthClauseId.Should().Be("SP63.13330.2018+A1:table6.14");
    profile.DesignStrengthAccessedUtc.Should().Be("2026-10-02");
    profile.DesignStrengthSourceUrl.Should().Be("https://nav.tn.ru/documents/regulatory/ast_s_sp_63_13330_2018_izm1/");
  }

  [Fact(DisplayName = "SP 63 §N-9.1 — Normative Profile Version Tracking")]
  public void PipelineExecutionMetadata_DefaultsShouldTrackNormativeRegistry()
  {
    var metadata = new PipelineExecutionMetadata();

    metadata.NormativeProfileId.Should().Be(NormativeProfiles.DefaultProfileId);
    metadata.NormativeTablesVersion.Should().Be(NormativeProfiles.DefaultTablesVersion);
  }

  [Theory(DisplayName = "SP 63 §10.3.24 — Bond Stress by Concrete Class")]
  [InlineData("B15", 0.75)]
  [InlineData("C12/15", 0.75)]
  [InlineData("B25", 1.05)]
  [InlineData("C20/25", 1.05)]
  [InlineData("B60", 1.70)]
  public void BondStressLookup_ShouldMatchGoldenValues(string concreteClass, double expected)
  {
    AnchorageRules.GetBondStress(concreteClass).Should().Be(expected);
  }

  [Theory(DisplayName = "SP 63 §5.2.1 — Rebar Design Strength by Steel Class")]
  [InlineData("A240", 210)]
  [InlineData("A-I", 210)]
  [InlineData("A400", 340)]
  [InlineData("A-III", 340)]
  [InlineData("A500C", 435)]
  [InlineData("A600", 520)]
  [InlineData("B500", 415)]
  [InlineData("B500C", 415)]
  public void DesignStrengthLookup_ShouldMatchGoldenValues(string steelClass, double expected)
  {
    AnchorageRules.GetDesignStrength(steelClass).Should().Be(expected);
  }

  [Theory(DisplayName = "SP 63 §5.1.7 — Periodic Profile Detection")]
  [InlineData("A240", false)]
  [InlineData("A-I", false)]
  [InlineData("A400", true)]
  [InlineData("A500C", true)]
  [InlineData("B500C", true)]
  [InlineData("B500", true)]
  public void PeriodicProfileLookup_ShouldMatchGoldenValues(string steelClass, bool expected)
  {
    AnchorageRules.IsPeriodicProfile(steelClass).Should().Be(expected);
  }

  [Theory(DisplayName = "SP 63 Table 1.2 — Rebar Linear Mass")]
  [InlineData(6, 0.222)]
  [InlineData(12, 0.888)]
  [InlineData(25, 3.850)]
  [InlineData(40, 9.870)]
  public void LinearMassLookup_ShouldMatchGoldenValues(int diameterMm, double expected)
  {
    ReinforcementLimits.GetLinearMass(diameterMm).Should().Be(expected);
  }

  [Fact]
  public void TraceabilityDocument_MatchesGenerator()
  {
    var root = ResolveRepositoryRoot();
    var path = Path.Combine(root, "docs", "NORMATIVE_TRACEABILITY.md");
    string document = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
    document.Should().Be(NormativeTraceability.Render());
  }

  [Fact]
  public void ReinforcementLimits_ShouldExposeVersionedStandardSets()
  {
    ReinforcementLimits.StandardDiameters.Should().ContainInOrder(6, 8, 10, 12, 14, 16, 18, 20, 22, 25, 28, 32, 36, 40);
    ReinforcementLimits.StandardSpacings.Should().ContainInOrder(100, 150, 200, 250, 300);
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
