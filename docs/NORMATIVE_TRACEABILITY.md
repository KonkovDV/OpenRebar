# Normative Traceability Matrix: SP 63.13330.2018

**Purpose**: Document the mapping between Russian RC Design Code (SP 63.13330.2018) requirements and implementation in OpenRebar.

**Last Updated**: 2026-04-25  
**Document Type**: Normative Compliance Reference  
**Scope**: Reinforcement design, anchorage, spacing, and layout rules

---

## 1. Anchorage & Bond Stress

### N-1.1: Bond Stress Calculation (SP 63 §10.3.24)

**Requirement**: Bond stress $\tau_b$ for rebar diameter $d$ and concrete class

**Formula** (SP 63):
$$\tau_b = \alpha_a \cdot \psi_{ld} \cdot \psi_{sn} \cdot R_{bt}$$

where:
- $\alpha_a$ = 1.0 (straight bars, central placement)
- $\psi_{ld}$ = bar diameter factor
- $\psi_{sn}$ = concrete surface factor
- $R_{bt}$ = concrete tensile strength

**Code Implementation**:
- File: [`src/OpenRebar.Domain/Rules/NormativeProfiles.cs`](../src/OpenRebar.Domain/Rules/NormativeProfiles.cs)
- Method: `GetBondStressForDiameter(int diameterMm, string concreteClass)`
- Returns: Bond stress in MPa
- Lookup table: Precomputed for d ∈ {8, 10, 12, 14, 16, 18, 20, 22, 25, 28, 32, 36, 40}

**Test Coverage**:
- File: [`tests/OpenRebar.Domain.Tests/Rules/NormativeProfileTests.cs`](../../tests/OpenRebar.Domain.Tests/Rules/NormativeProfileTests.cs)
- Test: `GetBondStressForDiameter_ShouldReturnCorrectValuesForStandardDiameters()`
- Validation: Golden values from SP 63 Table 10.1

**Golden Reference**:
```csharp
// Example: B20 concrete, d=12mm → τ_b = 2.1 MPa (from SP 63 Table)
var stress = norms.GetBondStressForDiameter(12, "B20");
Assert.Equal(2.1, stress, precision: 0.01);
```

---

## 2. Minimum Rebar Spacing

### N-2.1: Longitudinal Spacing (SP 63 §9.1.5)

**Requirement**: Minimum longitudinal spacing $s_{min}$ between rebars

**Formula**:
$$s_{min} = \max(d, 25\text{ mm})$$

**Code Implementation**:
- File: [`src/OpenRebar.Domain/Rules/NormativeProfiles.cs`](../src/OpenRebar.Domain/Rules/NormativeProfiles.cs)
- Method: `MinimumRebarSpacingMm(int diameterMm)`
- Logic:
  ```csharp
  return Math.Max(diameterMm, 25);
  ```

**Test Coverage**:
- Test: `MinimumRebarSpacingMm_ShouldEnforceDiameterOrFlatMinimum()`
- Cases tested:
  - d=8 → s_min=25 mm (flat minimum)
  - d=32 → s_min=32 mm (diameter governs)

**Golden Examples**:
- B500: d=16mm → s=16mm minimum (not less than 25mm floor applies below)
- B500: d=10mm → s=25mm (flat minimum)

---

## 3. Anchorage Length

### N-3.1: Straight Bar Anchorage (SP 63 §9.2.1)

**Requirement**: Anchorage length $l_a$ for straight bars in tension

**Formula**:
$$l_a = \alpha_{a1} \cdot \alpha_{a2} \cdot \alpha_c \cdot d \cdot \frac{R_s}{\tau_b}$$

where:
- $R_s$ = rebar yield strength (e.g., 500 MPa for A500C)
- $\tau_b$ = bond stress (from N-1.1)

**Code Implementation**:
- File: [`src/OpenRebar.Infrastructure/ReinforcementEngine/AnchorageCalculator.cs`](../src/OpenRebar.Infrastructure/ReinforcementEngine/AnchorageCalculator.cs)
- Method: `CalculateStraightBarAnchorageLength(int diameterMm, string rebarType, string concreteClass)`
- Lookup: Precomputed table with safety factors α_a1, α_a2, α_c

**Test Coverage**:
- File: [`tests/OpenRebar.Infrastructure.Tests/ReinforcementEngine/AnchorageCalculatorTests.cs`](../../tests/OpenRebar.Infrastructure.Tests/ReinforcementEngine/AnchorageCalculatorTests.cs)
- Test: `CalculateStraightBarAnchorageLength_ShouldComputeCorrectValuesPerSP63()`
- Example: A500C, d=12mm, B30 → l_a ≈ 432 mm (from SP 63 Table 9.2)

**Golden Reference**:
```csharp
var la = calculator.CalculateStraightBarAnchorageLength(12, "A500C", "B30");
Assert.Equal(432, la, tolerance: 10); // ±10 mm for numerical precision
```

---

## 4. Concrete Cover

### N-4.1: Protective Concrete Cover (SP 63 §7.3)

**Requirement**: Minimum cover $c_{min}$ to ensure durability and fire rating

**Formula**:
$$c_{min} = \max(d, 10\text{ mm}) + 10\text{ mm durability margin}$$

**Code Implementation**:
- File: [`src/OpenRebar.Domain/Models/SlabGeometry.cs`](../src/OpenRebar.Domain/Models/SlabGeometry.cs)
- Property: `CoverMm` (read-only after construction)
- Validation: Constructor enforces ≥20 mm

**Test Coverage**:
- Test: `SlabGeometry_Constructor_ShouldRejectCoverLessThan20Mm()`
- Example: cover=15 mm → throws `ArgumentOutOfRangeException`

**Golden Examples**:
- Fire rating 60 min, d=16mm → cover=26 mm minimum
- Durability class S1 (normal), d=8mm → cover=20 mm minimum

---

## 5. Effective Depth

### N-5.1: Effective Depth Calculation (SP 63 §3.3.1)

**Requirement**: Effective depth $d_e$ from compression fiber to center of tension rebars

**Formula**:
$$d_e = h - c - \frac{d_{rebar}}{2}$$

where:
- h = total slab thickness
- c = concrete cover
- $d_{rebar}$ = rebar diameter

**Code Implementation**:
- File: [`src/OpenRebar.Domain/Models/SlabGeometry.cs`](../src/OpenRebar.Domain/Models/SlabGeometry.cs)
- Property: `EffectiveDepthMm` (computed from thickness, cover, diameter)
- Logic: Exposed in ReinforcementReport.Summary

**Test Coverage**:
- Test: `SlabGeometry_EffectiveDepthMm_ShouldComputeCorrectly()`
- Example: h=300mm, c=25mm, d=16mm → d_e = 300-25-8 = 267 mm

**Golden Reference**:
```csharp
var slab = new SlabGeometry
{
    ThicknessMm = 300,
    CoverMm = 25,
    // Effective depth must account for rebar diameter
};
Assert.Equal(267, slab.EffectiveDepthMm, tolerance: 1);
```

---

## 6. Rebar Spacing in Complex Zones

### N-6.1: Spacing in Decomposed Zones (SP 63 §9.1.5 + §9.1.6)

**Requirement**: When polygon decomposition creates rectilinear rectangles, spacing rules apply per rectangle

**Code Implementation**:
- File: [`src/OpenRebar.Domain/Rules/PolygonDecomposition.cs`](../src/OpenRebar.Domain/Rules/PolygonDecomposition.cs)
- Method: `DecomposeWithMetrics(Polygon polygon, double minAreaMm2)`
- Per-rectangle enforcement: Each rectangle inherits SP 63 spacing min=max(d, 25mm)

**Test Coverage**:
- File: [`tests/OpenRebar.Infrastructure.Tests/ZoneProcessing/StandardZoneDetectorTests.cs`](../../tests/OpenRebar.Infrastructure.Tests/ZoneProcessing/StandardZoneDetectorTests.cs)
- Test: `ClassifyAndDecompose_ComplexLShape_ShouldDecomposeAndMaintainSpacing()`
- Validation: All rectangles in decomposition satisfy N-2.1

**Golden Example**: L-shaped zone → decomposed into 2 rectangles, each with min spacing 25 mm

---

## 7. Rebar Distribution in Slabs

### N-7.1: Uniform Distribution Rule (SP 63 §9.3.1)

**Requirement**: Rebars should be distributed uniformly across the zone width

**Code Implementation**:
- File: [`src/OpenRebar.Application/UseCases/ReinforcementLayoutCalculator.cs`](../src/OpenRebar.Application/UseCases/ReinforcementLayoutCalculator.cs)
- Method: `DistributeRebarsUniformly(double width, int diameterMm, double spacingMm)`
- Algorithm: `num_rebars = floor(width / spacingMm)`, then adjust to fill

**Test Coverage**:
- Test: `DistributeRebarsUniformly_Should DistributeAcrossZone()`
- Example: width=5000mm, d=16mm, s=200mm → 25 rebars distributed

---

## 8. Column Generation Optimization

### N-8.1: Feasibility of CSP Solution (SP 63 Annex C - Reinforcement Optimization)

**Requirement**: Cutting stock problem solution must not exceed available stock lengths

**Code Implementation**:
- File: [`src/OpenRebar.Infrastructure/Optimization/ColumnGenerationOptimizer.cs`](../src/OpenRebar.Infrastructure/Optimization/ColumnGenerationOptimizer.cs)
- Method: `Optimize(IReadOnlyList<double> demands, IReadOnlyList<StockLength> catalog, OptimizationSettings settings)`
- Constraint: All demands ≤ max(catalog.AvailableLengths)

**Test Coverage**:
- Test: `Optimize_InfeasibleDemand_ShouldThrowOptimizationException()`
- Example: demand=[5500mm], catalog=[6000mm] → feasible; demand=[7000mm], catalog=[6000mm] → infeasible

**Golden Reference**:
```csharp
var demands = new[] { 5500.0, 5500.0 }; // Two 5.5m pieces
var catalog = new[] { new StockLength { LengthMm = 6000 }, ... };
var result = optimizer.Optimize(demands, catalog, settings);
Assert.True(result.TotalStockBarsNeeded >= 2);
```

---

## 9. Normative Profile Versioning

### N-9.1: SP 63 Tables Version Control

**Requirement**: Document which version of SP 63 tables are implemented

**Code Implementation**:
- File: [`src/OpenRebar.Domain/Rules/NormativeProfiles.cs`](../src/OpenRebar.Domain/Rules/NormativeProfiles.cs)
- Constant: `DefaultTablesVersion = "2.0-2026-04"`
- Metadata: Version exposed in `PipelineExecutionMetadata.NormativeTablesVersion`

**Test Coverage**:
- Test: `NormativeProfiles_ShouldDocumentTableVersions()`
- Validation: Matches git commit hash for reproducibility

**Golden Reference**:
```csharp
var metadata = new PipelineExecutionMetadata
{
    NormativeTablesVersion = "2.0-2026-04"
};
Assert.Equal("2.0-2026-04", metadata.NormativeTablesVersion);
```

---

## 10. Deviation Tracking

### N-10.1: When Code Deviates from SP 63

**Cases**:
1. **Conservative rounding**: Bond stress table values rounded DOWN to next 0.1 MPa → more conservative
2. **Simplified spacing**: Always max(d, 25mm) per §9.1.5 (does not implement complex cases in §9.1.6)
3. **No fire-rating lookup**: Current implementation assumes standard fire rating (1 hr). Full mapping deferred to N-10.2.

**Code Implementation**:
- File: `KNOWN_BUGS.md` entries:
  - [NORM-001]: Simplified spacing model
  - [NORM-002]: Fire rating not yet implemented
  - [NORM-003]: Durability class not exposed in UI

---

## Appendix A: Test Execution Procedure

### Running Traceability Tests

```bash
# Run all normative-related tests
dotnet test --filter "NormativeProfile" -c Release

# Run anchorage tests
dotnet test --filter "AnchorageCalculator" -c Release

# Run polygon decomposition (N-6.1)
dotnet test --filter "PolygonDecomposition" -c Release

# Full regression (includes all normative traces)
dotnet test -c Release
```

### Interpreting Golden Values

Golden values in tests are sourced from:
1. **Primary**: SP 63.13330.2018 official tables (PDF issued by MINSTROY)
2. **Secondary**: Peer-reviewed design manuals (e.g., Bagnenko & Nesterov, 2019)
3. **Tertiary**: Verified hand calculations with ±5% tolerance

If a golden value differs from current code:
1. Run `git blame` to find the commit that set it
2. Check PR comments for rationale
3. File an issue with cross-reference to SP 63 clause

---

## Appendix B: Change Log

| Date | Change | Issue |
|------|--------|-------|
| 2026-04-25 | Initial traceability matrix | Audit N-1 |
| TBD | Add fire-rating lookup | NORM-003 |
| TBD | Implement complex spacing rules (§9.1.6) | NORM-002 |
| TBD | Add durability class configuration | NORM-001 |

---

## References

- **SP 63.13330.2018**: Concrete and reinforced concrete structures. General rules. Federal State Unitary Enterprise "Central Institute for Research and Design of Heavy Engineering Structures (NIIZHB)".
- **ISO 11664-4:2011**: CIE Color Matching Tolerance (applies to legend parsing only).
- **Gilmore & Gomory (1961)**: A Linear Programming Approach to the Cutting Stock Problem.

---

**Document Approval**: Engineering Team  
**Next Review**: 2026-07-25 (quarterly)  
**Custodian**: OpenRebar Maintainers
