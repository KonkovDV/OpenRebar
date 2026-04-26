using System.Diagnostics;
using System.Threading;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;
using OpenRebar.Domain.Exceptions;
using Highs;

namespace OpenRebar.Infrastructure.Optimization;

/// <summary>
/// Production-oriented column-generation-style optimizer for the 1D cutting stock problem.
/// Uses restricted-master LP, bounded-knapsack pricing, heuristic integerization,
/// and an FFD non-regression floor instead of claiming exact branch-and-price optimality.
///
/// Algorithm:
///   1. Build initial patterns via FFD (one pattern per distinct length)
///   2. Solve LP relaxation (Revised Simplex on the pattern matrix)
///   3. Solve pricing subproblem: find the most profitable new pattern
///      via bounded knapsack DP
///   4. If reduced cost &lt; -ε, add the column and repeat from (2)
///   5. Round the LP solution to integers via largest-remainder rounding
///   6. Repair any unserved demand by greedy FFD
///
/// Complexity: O(I × S × C) per CG iteration, where
///   I = distinct item count, S = stock length, C = LP iterations.
///
/// References:
///   Gilmore &amp; Gomory (1961) — "A Linear Programming Approach to the
///     Cutting Stock Problem", Operations Research 9(6).
///   Vanderbeck (2000) — "On Dantzig-Wolfe decomposition in integer
///     programming and ways to exploit it".
///   ESICUP best practices (European Cutting &amp; Packing community).
/// </summary>
public sealed class ColumnGenerationOptimizer : IRebarOptimizer
{
    public const double DemandAggregationPrecisionMm = 0.1;
    private const int ExactSearchMaxItemCount = 8;
    private const int ExactSearchMaxStockVariants = 3;
    private static readonly AsyncLocal<bool> ForceFallbackMasterSolverOverride = new();

    /// <summary>Reduced-cost tolerance for column generation convergence.</summary>
    private const double Epsilon = 1e-6;

    /// <summary>Maximum CG iterations to avoid infinite loops.</summary>
    private const int MaxIterations = 500;

    public OptimizationResult Optimize(
        IReadOnlyList<double> requiredLengths,
        IReadOnlyList<StockLength> stockLengths,
        OptimizationSettings settings)
    {
        if (requiredLengths.Count == 0)
        {
            return new OptimizationResult
            {
                CuttingPlans = [],
                TotalStockBarsNeeded = 0,
                TotalWasteMm = 0,
                TotalWastePercent = 0,
                TotalRebarLengthMm = 0,
                Provenance = BuildColumnGenerationProvenance(usedFallbackMasterSolver: false)
            };
        }

        var availableStock = stockLengths
            .Where(s => s.InStock)
            .OrderBy(s => s.LengthMm)
            .ToList();

        if (availableStock.Count == 0)
            throw new OptimizationException("No in-stock bar lengths are available for optimization.");

        EnsureAllCutsFitStock(requiredLengths, availableStock, settings.SawCutWidthMm);

        var exactSmallInstance = TryOptimizeExactSmallInstance(requiredLengths, availableStock, settings);
        if (exactSmallInstance is not null)
            return exactSmallInstance;

        // Aggregate demand: distinct lengths → counts
        var demand = AggregrateDemand(requiredLengths, settings.SawCutWidthMm);
        int m = demand.Count; // number of distinct item types

        double[] itemLengths = demand.Select(d => d.EffectiveLength).ToArray();
        int[] itemDemand = demand.Select(d => d.Count).ToArray();

        var candidates = availableStock
            .Select(stock =>
            {
                var result = OptimizeForStockLength(requiredLengths, demand, itemLengths, itemDemand, stock.LengthMm, settings.SawCutWidthMm);
                double? costProxy = stock.PricePerTon.HasValue
                    ? result.CuttingPlans.Sum(plan => plan.StockLengthMm) * stock.PricePerTon.Value
                    : null;

                return new Candidate(stock, result, costProxy);
            })
            .ToList();

        var bestCandidate = ChooseBestCandidate(candidates, requiredLengths.Count, settings);
        var bestColumnGeneration = bestCandidate.Result;

        // Guard against pathological CG rounding/pricing behavior by keeping the simpler
        // best-fit-decreasing baseline as a floor for solution quality.
        var baseline = new FirstFitDecreasingOptimizer().Optimize(requiredLengths, availableStock, settings);
        double? baselineCostProxy = EstimateCostProxy(baseline, availableStock);
        if (IsBaselineBetter(
            baseline,
            bestColumnGeneration,
            requiredLengths.Count,
            settings,
            baselineCostProxy,
            bestCandidate.CostProxy))
            return baseline;

        return bestColumnGeneration;
    }

    internal static IDisposable PushForceFallbackMasterSolverOverrideForTesting()
    {
        bool previousValue = ForceFallbackMasterSolverOverride.Value;
        ForceFallbackMasterSolverOverride.Value = true;
        return new DisposeAction(() => ForceFallbackMasterSolverOverride.Value = previousValue);
    }

    private static void EnsureAllCutsFitStock(
        IReadOnlyList<double> requiredLengths,
        IReadOnlyList<StockLength> availableStock,
        double sawCutWidthMm)
    {
        foreach (double length in requiredLengths)
        {
            double requiredEffectiveLength = length + sawCutWidthMm;
            bool hasCompatibleStock = availableStock.Any(stock => stock.LengthMm + 1e-6 >= requiredEffectiveLength);
            if (hasCompatibleStock)
                continue;

            double maxStock = availableStock.Max(stock => stock.LengthMm);
            throw new OptimizationException(
                $"Required cut {length:F1} mm (effective {requiredEffectiveLength:F1} mm with saw cut) exceeds all available stock lengths. Max in-stock length: {maxStock:F1} mm.");
        }
    }

    private static OptimizationResult OptimizeForStockLength(
        IReadOnlyList<double> requiredLengths,
        List<DemandItem> demand,
        double[] itemLengths,
        int[] itemDemand,
        double stockLength,
        double sawCutWidthMm)
    {
        int m = demand.Count;
        bool usedFallbackMasterSolver = false;

        // Build initial feasible patterns (one item type per pattern, max fit)
        var patterns = BuildInitialPatterns(itemLengths, stockLength, m);

        // Column Generation loop
        for (int iter = 0; iter < MaxIterations; iter++)
        {
            // Solve LP relaxation: min Σ x_j  s.t. A·x ≥ demand, x ≥ 0
            var master = SolveRestrictedMasterLP(patterns, itemDemand, m);
            usedFallbackMasterSolver |= master.UsedFallbackMasterSolver;
            if (master.Solution is null) break;

            // Pricing subproblem: find pattern with maximum Σ π_i · a_i - 1
            var newPattern = SolvePricingKnapsack(master.Duals, itemLengths, itemDemand, stockLength);
            double reducedCost = 1.0 - master.Duals.Zip(newPattern).Sum(p => p.First * p.Second);

            if (reducedCost > -Epsilon) break; // No improving column in the LP relaxation

            patterns.Add(newPattern);
        }

        // Final LP solve + integer rounding
        var finalMaster = SolveRestrictedMasterLP(patterns, itemDemand, m);
        usedFallbackMasterSolver |= finalMaster.UsedFallbackMasterSolver;
        var integerSolution = RoundSolution(finalMaster.Solution ?? [], patterns, itemDemand, m);

        // Build cutting plans from the integer solution
        return BuildResult(integerSolution, patterns, demand, stockLength, sawCutWidthMm, requiredLengths, usedFallbackMasterSolver, finalMaster.LpObjectiveValue);
    }

    private static OptimizationResult? TryOptimizeExactSmallInstance(
        IReadOnlyList<double> requiredLengths,
        IReadOnlyList<StockLength> availableStock,
        OptimizationSettings settings)
    {
        if (requiredLengths.Count > ExactSearchMaxItemCount ||
            availableStock.Count > ExactSearchMaxStockVariants ||
            availableStock.Any(stock => stock.PricePerTon.HasValue))
        {
            return null;
        }

        var sortedLengths = requiredLengths
            .OrderByDescending(length => length)
            .ToArray();

        var stockOptions = availableStock
            .Select(stock => stock.LengthMm)
            .Distinct()
            .OrderBy(length => length)
            .ToArray();

        double totalRequired = requiredLengths.Sum();
        var bins = new List<ExactBinState>();
        ExactSearchSolution? best = null;
        bool timedOut = false;
        var stopwatch = Stopwatch.StartNew();

        void Search(int index)
        {
            if (timedOut)
                return;

            if (stopwatch.Elapsed > settings.MaxComputationTime)
            {
                timedOut = true;
                return;
            }

            if (best is not null)
            {
                double optimisticInstallOnlyScore = settings.InstallationWeight * ((double)bins.Count / sortedLengths.Length);
                if (optimisticInstallOnlyScore > best.Score + 1e-9)
                    return;
            }

            if (index == sortedLengths.Length)
            {
                double totalStockLength = bins.Sum(bin => bin.StockLengthMm);
                double totalWasteMm = totalStockLength - totalRequired - sortedLengths.Length * settings.SawCutWidthMm;
                double totalWastePercent = totalStockLength > 0
                    ? totalWasteMm / totalStockLength * 100.0
                    : 0;
                double score = settings.WasteWeight * (totalWastePercent / 100.0)
                    + settings.InstallationWeight * ((double)bins.Count / sortedLengths.Length);

                if (IsBetterExactSolution(score, totalStockLength, bins.Count, best))
                {
                    best = new ExactSearchSolution(
                        score,
                        totalStockLength,
                        bins.Count,
                        bins.Select(bin => new ExactBinSnapshot(bin.StockLengthMm, bin.Cuts.ToList())).ToList());
                }

                return;
            }

            double rawPieceLength = sortedLengths[index];
            double effectivePieceLength = rawPieceLength + settings.SawCutWidthMm;
            var seenStates = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < bins.Count; i++)
            {
                if (bins[i].UsedEffectiveLengthMm + effectivePieceLength > bins[i].StockLengthMm + 1e-6)
                    continue;

                string stateKey = string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"{bins[i].StockLengthMm:F3}:{bins[i].UsedEffectiveLengthMm:F3}");
                if (!seenStates.Add(stateKey))
                    continue;

                bins[i].UsedEffectiveLengthMm += effectivePieceLength;
                bins[i].Cuts.Add(rawPieceLength);
                Search(index + 1);
                bins[i].Cuts.RemoveAt(bins[i].Cuts.Count - 1);
                bins[i].UsedEffectiveLengthMm -= effectivePieceLength;
            }

            foreach (double stockLength in stockOptions)
            {
                if (effectivePieceLength > stockLength + 1e-6)
                    continue;

                bins.Add(new ExactBinState
                {
                    StockLengthMm = stockLength,
                    UsedEffectiveLengthMm = effectivePieceLength,
                    Cuts = [rawPieceLength]
                });

                Search(index + 1);
                bins.RemoveAt(bins.Count - 1);
            }
        }

        Search(0);

        if (timedOut || best is null)
            return null;

        var plans = best.Bins
            .Select(bin => new CuttingPlan
            {
                StockLengthMm = bin.StockLengthMm,
                Cuts = bin.Cuts,
                SawCutWidthMm = settings.SawCutWidthMm
            })
            .ToList();

        double totalStock = plans.Sum(plan => plan.StockLengthMm);
        double totalWaste = plans.Sum(plan => plan.WasteMm);

        return new OptimizationResult
        {
            CuttingPlans = plans,
            TotalStockBarsNeeded = plans.Count,
            TotalWasteMm = totalWaste,
            TotalWastePercent = totalStock > 0 ? totalWaste / totalStock * 100 : 0,
            TotalRebarLengthMm = totalRequired,
            Provenance = BuildExactSmallInstanceProvenance()
        };
    }

    private static bool IsBetterExactSolution(
        double score,
        double totalStockLength,
        int barCount,
        ExactSearchSolution? best)
    {
        if (best is null)
            return true;

        if (score < best.Score - 1e-9)
            return true;

        if (score > best.Score + 1e-9)
            return false;

        if (totalStockLength < best.TotalStockLengthMm - 1e-6)
            return true;

        if (totalStockLength > best.TotalStockLengthMm + 1e-6)
            return false;

        return barCount < best.BarCount;
    }

    #region Demand Aggregation

    private sealed record DemandItem(double OriginalLength, double EffectiveLength, int Count);

    private static List<DemandItem> AggregrateDemand(IReadOnlyList<double> lengths, double sawCut)
    {
        return lengths
            .GroupBy(l => Math.Round(l, 1)) // group within 0.1mm tolerance
            .Select(g => new DemandItem(g.Key, g.Key + sawCut, g.Count()))
            .OrderByDescending(d => d.EffectiveLength)
            .ToList();
    }

    #endregion

    #region Initial Patterns

    private static List<int[]> BuildInitialPatterns(double[] itemLengths, double stockLength, int m)
    {
        var patterns = new List<int[]>();
        for (int i = 0; i < m; i++)
        {
            var pattern = new int[m];
            pattern[i] = (int)(stockLength / itemLengths[i]);
            if (pattern[i] > 0)
                patterns.Add(pattern);
        }

        // Ensure we have at least one pattern per item type
        if (patterns.Count == 0)
        {
            var fallback = new int[m];
            fallback[0] = 1;
            patterns.Add(fallback);
        }

        return patterns;
    }

    #endregion

    #region Restricted Master LP (Revised Simplex)

    /// <summary>
    /// Solve: min Σ x_j  subject to Σ a_ij·x_j ≥ b_i for all i, x_j ≥ 0.
    /// Returns (primal solution x[], dual prices π[]).
    /// Uses simple iterative proportional fitting for the LP relaxation.
    /// </summary>
    private static MasterSolveResult SolveRestrictedMasterLP(
        List<int[]> patterns, int[] demand, int m)
    {
        var highs = TrySolveRestrictedMasterLpWithHighs(patterns, demand, m);
        if (highs is not null)
            return new MasterSolveResult(highs.Value.Solution, highs.Value.Duals, false, highs.Value.ObjectiveValue);

        var fallback = SolveRestrictedMasterLpFallback(patterns, demand, m);
        return new MasterSolveResult(fallback.Solution, fallback.Duals, true, fallback.ObjectiveValue);
    }

    private sealed record MasterSolveResult(double[]? Solution, double[] Duals, bool UsedFallbackMasterSolver, double? LpObjectiveValue);

    private static (double[]? Solution, double[] Duals, double? ObjectiveValue)? TrySolveRestrictedMasterLpWithHighs(
        List<int[]> patterns,
        int[] demand,
        int m)
    {
        if (ForceFallbackMasterSolverOverride.Value)
            return null;

        try
        {
            int n = patterns.Count;
            if (n == 0)
                return (null, new double[m], null);

            double infinity = double.PositiveInfinity;
            var colCost = Enumerable.Repeat(1.0, n).ToArray();
            var colLower = new double[n];
            var colUpper = Enumerable.Repeat(infinity, n).ToArray();
            var rowLower = demand.Select(value => (double)value).ToArray();
            var rowUpper = Enumerable.Repeat(infinity, m).ToArray();

            var starts = new int[m + 1];
            var indices = new List<int>();
            var values = new List<double>();

            for (int i = 0; i < m; i++)
            {
                starts[i] = indices.Count;
                for (int j = 0; j < n; j++)
                {
                    int coefficient = patterns[j][i];
                    if (coefficient == 0)
                        continue;

                    indices.Add(j);
                    values.Add(coefficient);
                }
            }

            starts[m] = indices.Count;

            var model = new HighsModel(
                colCost,
                colLower,
                colUpper,
                rowLower,
                rowUpper,
                starts,
                indices.ToArray(),
                values.ToArray(),
                null,
                0,
                HighsMatrixFormat.kRowwise,
                HighsObjectiveSense.kMinimize);

            using var solver = new HighsLpSolver();
            solver.setBoolOptionValue("output_flag", 0);
            var passStatus = solver.passLp(model);
            if (passStatus != HighsStatus.kOk)
                return null;

            var runStatus = solver.run();
            if (runStatus != HighsStatus.kOk)
                return null;

            var modelStatus = solver.GetModelStatus();
            if (modelStatus != HighsModelStatus.kOptimal && modelStatus != HighsModelStatus.kModelEmpty)
                return null;

            var solution = solver.getSolution();
            // Objective value is the sum of solution (all costs are 1.0 in the column generation master)
            double objectiveValue = solution.colvalue.Sum();
            return (solution.colvalue, solution.rowdual.Select(Math.Abs).ToArray(), objectiveValue);
        }
        catch
        {
            return null;
        }
    }

    private static (double[]? Solution, double[] Duals, double? ObjectiveValue) SolveRestrictedMasterLpFallback(
        List<int[]> patterns,
        int[] demand,
        int m)
    {
        int n = patterns.Count;
        if (n == 0) return (null, new double[m], null);

        double[] x = new double[n];
        double[] duals = new double[m];

        for (int i = 0; i < m; i++)
        {
            int bestJ = -1;
            int bestCover = 0;
            for (int j = 0; j < n; j++)
            {
                if (patterns[j][i] > bestCover)
                {
                    bestCover = patterns[j][i];
                    bestJ = j;
                }
            }

            if (bestJ >= 0 && bestCover > 0)
            {
                double need = demand[i];
                double currentCoverage = 0;
                for (int j = 0; j < n; j++)
                    currentCoverage += patterns[j][i] * x[j];

                double deficit = need - currentCoverage;
                if (deficit > 0)
                    x[bestJ] += deficit / bestCover;
            }
        }

        for (int iter = 0; iter < 200; iter++)
        {
            bool changed = false;

            for (int j = 0; j < n; j++)
            {
                double minNeeded = 0;
                for (int i = 0; i < m; i++)
                {
                    if (patterns[j][i] == 0) continue;

                    double coverWithout = -patterns[j][i] * x[j];
                    for (int k = 0; k < n; k++)
                        coverWithout += patterns[k][i] * x[k];

                    double deficit = demand[i] - coverWithout;
                    if (deficit > 0)
                        minNeeded = Math.Max(minNeeded, deficit / patterns[j][i]);
                }

                if (Math.Abs(x[j] - minNeeded) > 1e-8)
                {
                    x[j] = minNeeded;
                    changed = true;
                }
            }

            if (!changed) break;
        }

        for (int i = 0; i < m; i++)
        {
            double maxCover = 0;
            for (int j = 0; j < n; j++)
            {
                if (x[j] > 1e-8 && patterns[j][i] > 0)
                    maxCover = Math.Max(maxCover, patterns[j][i]);
            }

            duals[i] = maxCover > 0 ? 1.0 / maxCover : 0;
        }

        double objectiveValue = x.Sum();
        return (x, duals, objectiveValue);
    }

    #endregion

    #region Pricing Subproblem (Bounded Knapsack via DP)

    /// <summary>
    /// Solve: max Σ π_i·a_i  subject to Σ l_i·a_i ≤ L, 0 ≤ a_i ≤ demand_i.
    /// Returns the best new cutting pattern.
    /// </summary>
    private static int[] SolvePricingKnapsack(
        double[] dualPrices, double[] itemLengths, int[] demand, double stockLength)
    {
        int m = itemLengths.Length;

        // DP capacity in discrete units (0.1mm resolution for rebar)
        int capacity = (int)(stockLength * 10);
        double[] dp = new double[capacity + 1];
        int[,] choices = new int[m, capacity + 1]; // tracks count of each item at each capacity

        for (int i = 0; i < m; i++)
        {
            int itemCap = (int)(itemLengths[i] * 10);
            if (itemCap <= 0) continue;

            int maxCount = Math.Min(demand[i], capacity / itemCap);

            // Bounded knapsack: iterate copies
            for (int c = capacity; c >= itemCap; c--)
            {
                for (int k = 1; k <= maxCount; k++)
                {
                    int needed = k * itemCap;
                    if (needed > c) break;

                    double value = k * dualPrices[i];
                    if (dp[c - needed] + value > dp[c] + 1e-10)
                    {
                        dp[c] = dp[c - needed] + value;
                        choices[i, c] = k;
                    }
                }
            }
        }

        // Trace back the best pattern
        var pattern = new int[m];
        int remaining = capacity;
        for (int i = m - 1; i >= 0; i--)
        {
            if (remaining <= 0) break;
            pattern[i] = choices[i, remaining];
            remaining -= pattern[i] * (int)(itemLengths[i] * 10);
        }

        return pattern;
    }

    #endregion

    #region Integer Rounding

    /// <summary>
    /// Round LP solution to integers using largest-remainder method,
    /// then repair any unserved demand via greedy assignment.
    /// </summary>
    private static int[] RoundSolution(
        double[] lpSolution, List<int[]> patterns, int[] demand, int m)
    {
        int n = patterns.Count;
        var intSol = new int[n];

        // Floor + collect fractional remainders
        var remainders = new (double Frac, int Index)[n];
        for (int j = 0; j < n; j++)
        {
            double val = j < lpSolution.Length ? lpSolution[j] : 0;
            intSol[j] = (int)Math.Floor(val);
            remainders[j] = (val - intSol[j], j);
        }

        // Check which items are still uncovered
        var covered = new int[m];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < m; i++)
                covered[i] += patterns[j][i] * intSol[j];

        // Round up patterns with largest remainders to cover deficit
        foreach (var (_, j) in remainders.OrderByDescending(r => r.Frac))
        {
            bool stillNeeded = false;
            for (int i = 0; i < m; i++)
            {
                if (covered[i] < demand[i] && patterns[j][i] > 0)
                {
                    stillNeeded = true;
                    break;
                }
            }

            if (stillNeeded)
            {
                intSol[j]++;
                for (int i = 0; i < m; i++)
                    covered[i] += patterns[j][i];
            }
        }

        // Final repair: if still uncovered, add single-item patterns
        for (int i = 0; i < m; i++)
        {
            while (covered[i] < demand[i])
            {
                // Find a pattern that covers item i
                int bestJ = -1;
                int bestCover = 0;
                for (int j = 0; j < n; j++)
                {
                    if (patterns[j][i] > bestCover)
                    {
                        bestCover = patterns[j][i];
                        bestJ = j;
                    }
                }

                if (bestJ >= 0)
                {
                    intSol[bestJ]++;
                    for (int k = 0; k < m; k++)
                        covered[k] += patterns[bestJ][k];
                }
                else
                {
                    break; // Cannot cover — should not happen with valid input
                }
            }
        }

        return intSol;
    }

    #endregion

    #region Result Construction

    private static OptimizationResult BuildResult(
        int[] integerSolution,
        List<int[]> patterns,
        List<DemandItem> demand,
        double stockLength,
        double sawCutWidthMm,
        IReadOnlyList<double> originalLengths,
        bool usedFallbackMasterSolver,
        double? lpObjectiveValue)
    {
        var plans = new List<CuttingPlan>();
        var remainingDemand = demand.Select(item => item.Count).ToArray();

        for (int j = 0; j < integerSolution.Length; j++)
        {
            for (int copy = 0; copy < integerSolution[j]; copy++)
            {
                var cuts = new List<double>();
                for (int i = 0; i < demand.Count; i++)
                {
                    int cutsStillNeeded = Math.Min(patterns[j][i], remainingDemand[i]);
                    for (int k = 0; k < cutsStillNeeded; k++)
                        cuts.Add(demand[i].OriginalLength);

                    remainingDemand[i] -= cutsStillNeeded;
                }

                if (cuts.Count > 0)
                {
                    plans.Add(new CuttingPlan
                    {
                        StockLengthMm = stockLength,
                        Cuts = cuts,
                        SawCutWidthMm = sawCutWidthMm
                    });
                }
            }
        }

        double totalRequired = originalLengths.Sum();
        double totalStock = plans.Sum(plan => plan.StockLengthMm);
        double totalWaste = plans.Sum(plan => plan.WasteMm);

        // Calculate gap: (primal - dual) / dual * 100
        double primalObjective = plans.Count;
        double? gap = null;
        if (lpObjectiveValue.HasValue && lpObjectiveValue.Value > 1e-10)
        {
            gap = (primalObjective - lpObjectiveValue.Value) / lpObjectiveValue.Value * 100.0;
        }

        var provenance = BuildColumnGenerationProvenance(usedFallbackMasterSolver);
        if (provenance != null && gap.HasValue)
        {
            provenance = provenance with { QualityGapPercent = gap.Value };
        }

        return new OptimizationResult
        {
            CuttingPlans = plans,
            TotalStockBarsNeeded = plans.Count,
            TotalWasteMm = totalWaste,
            TotalWastePercent = totalStock > 0 ? totalWaste / totalStock * 100 : 0,
            TotalRebarLengthMm = totalRequired,
            DualBound = lpObjectiveValue,
            Gap = gap,
            Provenance = provenance
        };
    }

    #endregion

    private sealed record Candidate(StockLength Stock, OptimizationResult Result, double? CostProxy);

    private static Candidate ChooseBestCandidate(
        IReadOnlyList<Candidate> candidates,
        int itemCount,
        OptimizationSettings settings)
    {
        double minCost = candidates
            .Where(c => c.CostProxy.HasValue)
            .Select(c => c.CostProxy!.Value)
            .DefaultIfEmpty(0)
            .Min();

        double maxCost = candidates
            .Where(c => c.CostProxy.HasValue)
            .Select(c => c.CostProxy!.Value)
            .DefaultIfEmpty(0)
            .Max();

        return candidates
            .OrderBy(c => ScoreCandidate(c, itemCount, settings, minCost, maxCost))
            .ThenBy(c => c.Result.TotalWastePercent)
            .ThenBy(c => c.Result.TotalStockBarsNeeded)
            .First();
    }

    private static double ScoreCandidate(
        Candidate candidate,
        int itemCount,
        OptimizationSettings settings,
        double minCost,
        double maxCost)
    {
        double wasteScore = candidate.Result.TotalWastePercent / 100.0;
        double installScore = itemCount > 0
            ? (double)candidate.Result.TotalStockBarsNeeded / itemCount
            : 0;

        double costScore = 0;
        if (candidate.CostProxy.HasValue && maxCost > minCost)
            costScore = (candidate.CostProxy.Value - minCost) / (maxCost - minCost);

        return settings.WasteWeight * wasteScore
             + settings.InstallationWeight * installScore
             + settings.CostWeight * costScore;
    }

    private static bool IsBaselineBetter(
        OptimizationResult baseline,
        OptimizationResult candidate,
        int itemCount,
        OptimizationSettings settings,
        double? baselineCostProxy,
        double? candidateCostProxy)
    {
        double minCost = new[] { baselineCostProxy, candidateCostProxy }
            .Where(cost => cost.HasValue)
            .Select(cost => cost!.Value)
            .DefaultIfEmpty(0)
            .Min();

        double maxCost = new[] { baselineCostProxy, candidateCostProxy }
            .Where(cost => cost.HasValue)
            .Select(cost => cost!.Value)
            .DefaultIfEmpty(0)
            .Max();

        double baselineScore = ScoreResultForGuard(
            baseline,
            itemCount,
            settings,
            baselineCostProxy,
            minCost,
            maxCost);

        double candidateScore = ScoreResultForGuard(
            candidate,
            itemCount,
            settings,
            candidateCostProxy,
            minCost,
            maxCost);

        if (baselineScore < candidateScore - 1e-6)
            return true;

        if (baselineScore > candidateScore + 1e-6)
            return false;

        if (baseline.TotalWastePercent < candidate.TotalWastePercent - 1e-6)
            return true;

        if (baseline.TotalWastePercent > candidate.TotalWastePercent + 1e-6)
            return false;

        return baseline.TotalStockBarsNeeded < candidate.TotalStockBarsNeeded;
    }

    private static double? EstimateCostProxy(
        OptimizationResult result,
        IReadOnlyList<StockLength> availableStock)
    {
        if (!availableStock.Any(stock => stock.PricePerTon.HasValue))
            return null;

        var priceByLength = availableStock
            .Where(stock => stock.PricePerTon.HasValue)
            .GroupBy(stock => stock.LengthMm)
            .ToDictionary(
                group => group.Key,
                group => group.Min(stock => stock.PricePerTon!.Value));

        double total = 0;
        bool hasAnyPricedPlan = false;

        foreach (var plan in result.CuttingPlans)
        {
            if (!priceByLength.TryGetValue(plan.StockLengthMm, out double pricePerTon))
                continue;

            total += plan.StockLengthMm * pricePerTon;
            hasAnyPricedPlan = true;
        }

        return hasAnyPricedPlan ? total : null;
    }

    private static double ScoreResultForGuard(
        OptimizationResult result,
        int itemCount,
        OptimizationSettings settings,
        double? costProxy,
        double minCost,
        double maxCost)
    {
        double wasteScore = result.TotalWastePercent / 100.0;
        double installScore = itemCount > 0
            ? (double)result.TotalStockBarsNeeded / itemCount
            : 0;

        double costScore = 0;
        if (costProxy.HasValue && maxCost > minCost)
            costScore = (costProxy.Value - minCost) / (maxCost - minCost);

        return settings.WasteWeight * wasteScore
             + settings.InstallationWeight * installScore
             + settings.CostWeight * costScore;
    }

    private static OptimizationProvenance BuildColumnGenerationProvenance(bool usedFallbackMasterSolver)
    {
        return new OptimizationProvenance
        {
            OptimizerId = "column-generation-relaxation-v1",
            MasterProblemStrategy = usedFallbackMasterSolver
                ? "restricted-master-lp-highs-with-fallback"
                : "restricted-master-lp-highs",
            PricingStrategy = "bounded-knapsack-dp",
            IntegerizationStrategy = "largest-remainder-plus-repair",
            DemandAggregationPrecisionMm = DemandAggregationPrecisionMm,
            QualityFloor = "ffd-non-regression-floor",
            UsedFallbackMasterSolver = usedFallbackMasterSolver
        };
    }

    private static OptimizationProvenance BuildExactSmallInstanceProvenance()
    {
        return new OptimizationProvenance
        {
            OptimizerId = "exact-small-instance-search-v1",
            MasterProblemStrategy = "complete-enumeration",
            PricingStrategy = "not-applicable",
            IntegerizationStrategy = "exact-discrete-search",
            DemandAggregationPrecisionMm = 0,
            QualityFloor = "exact-small-instance-optimum",
            UsedFallbackMasterSolver = false
        };
    }

    private sealed class ExactBinState
    {
        public required double StockLengthMm { get; init; }
        public required List<double> Cuts { get; init; }
        public required double UsedEffectiveLengthMm { get; set; }
    }

    private sealed record ExactBinSnapshot(double StockLengthMm, IReadOnlyList<double> Cuts);
    private sealed record ExactSearchSolution(double Score, double TotalStockLengthMm, int BarCount, IReadOnlyList<ExactBinSnapshot> Bins);
    private sealed class DisposeAction(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
