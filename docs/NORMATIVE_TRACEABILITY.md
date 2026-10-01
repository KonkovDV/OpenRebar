# Normative traceability

Generated from `ru.sp63.2018.tables.v2.json`. Edit the table file, then regenerate this note.

| Clause | Method | Test | Quote |
| --- | --- | --- | --- |
| SP63.13330.2018:10.3.24 | AnchorageRules.CalculateBasicAnchorageLength | AnchorageRulesTests.BasicAnchorage_MatchesIndependentFormula | l0,an = Rs·d / (4·η1·η2·Rbt). |
| SP63.13330.2018:10.3.24 | AnchorageRules.CalculateAnchorageLength | AnchorageRulesTests.AnchorageLength_MatchesIndependentRounding | l_an = α·l0,an·As,cal/As,ef, not less than max(0.3·l0,an, 15d, 200 mm), rounded up to 10 mm. |
| SP63.13330.2018:10.3.30 | AnchorageRules.CalculateLapLength | AnchorageRulesTests.LapLength_MatchesIndependentFormula | Lap length uses unrounded l0,an and α of 1.2, 2.0, or 0.9, not less than max(0.4·α·l0,an, 20d, 250 mm). |
| SP63.13330.2018:10.3.8 | ReinforcementLimits.MaxSpacing | ReinforcementLimitsTests.MaxSpacing_FollowsThicknessBands | Spacing limit is 200 mm when h ≤ 150 mm, otherwise the lesser of 1.5h and 400 mm. |
| SP63.13330.2018:10.3.5 | ReinforcementLimits.MinReinforcementArea | ReinforcementLimitsTests.MinReinforcementArea_UsesEffectiveDepth | Minimum steel area is 0.1% of b·h0. |
| SP63.13330.2018:6.14 | NormativeProfiles.GetDesignStrength | NormativeProfilesTests.DesignStrengthLookup_ShouldMatchGoldenValues | A400 stays 355 MPa and B500 stays 435 MPa until table 6.14 is quoted. |
| profile:topBarAnchorageFactor | AnchorageRules.CalculateAnchorageLength | AnchorageRulesTests.TopBarFactor_ScalesAnchorageOnly | Default top-bar factor is 1.0 and is not a code coefficient. |

Design strengths of A400 (355 MPa) and B500 (435 MPa) are unchanged. Table 6.14 was not available as a citable extract.

The clear distance between adjacent laps in the amendment to clause 10.3.30 is not enforced. That amendment text is not in this repository.

Additional bars extend past a zone boundary by the calculated anchorage length, and only inside the working area. The SP 63 clause on curtailment past the theoretical cutoff is not in this repository, so no distance beyond that anchorage length is added.

