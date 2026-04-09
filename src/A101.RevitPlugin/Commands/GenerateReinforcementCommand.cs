namespace A101.RevitPlugin;

// ─────────────────────────────────────────────────────────────
// Revit ExternalCommand entry point.
// This is the main command registered in the Revit ribbon.
// Uncomment when building against Revit SDK.
// ─────────────────────────────────────────────────────────────

/*
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using A101.RevitPlugin.Revit;
using Microsoft.Extensions.DependencyInjection;

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class GenerateReinforcementCommand : IExternalCommand
{
    public Result Execute(
        ExternalCommandData commandData,
        ref string message,
        ElementSet elements)
    {
        try
        {
            var uiDoc = commandData.Application.ActiveUIDocument;

            // Bootstrap DI with real Revit placer
            var revitPlacer = new RevitRebarPlacer(uiDoc);
            var sp = Bootstrap.BuildServiceProvider(revitPlacer);

            // Show settings dialog
            var dialog = new UI.SettingsDialog();
            if (dialog.ShowDialog() != true)
                return Result.Cancelled;

            // Execute pipeline
            var pipeline = sp.GetRequiredService<GenerateReinforcementPipeline>();
            var result = pipeline.ExecuteAsync(dialog.BuildPipelineInput()).GetAwaiter().GetResult();

            // Show results
            TaskDialog.Show("A101 Reinforcement",
                $"Completed!\n" +
                $"Zones: {result.ParsedZoneCount}\n" +
                $"Rebars placed: {result.PlacementResult?.TotalRebarsPlaced ?? 0}\n" +
                $"Average waste: {result.TotalWastePercent:F1}%\n" +
                $"Total mass: {result.TotalMassKg:F0} kg");

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return Result.Failed;
        }
    }
}
*/
