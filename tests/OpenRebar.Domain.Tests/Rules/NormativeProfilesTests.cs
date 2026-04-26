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

    profile.ProfileId.Should().Be("ru.sp63.2018");
    profile.Jurisdiction.Should().Be("RU");
    profile.DesignCode.Should().Be("SP 63.13330.2018");
    profile.TablesVersion.Should().Be("ru.sp63.2018.tables.v1");
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
  [InlineData("A400", 355)]
  [InlineData("A500C", 435)]
  [InlineData("A600", 520)]
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
  public void ReinforcementLimits_ShouldExposeVersionedStandardSets()
  {
    ReinforcementLimits.StandardDiameters.Should().ContainInOrder(6, 8, 10, 12, 14, 16, 18, 20, 22, 25, 28, 32, 36, 40);
    ReinforcementLimits.StandardSpacings.Should().ContainInOrder(100, 150, 200, 250, 300);
  }
}
