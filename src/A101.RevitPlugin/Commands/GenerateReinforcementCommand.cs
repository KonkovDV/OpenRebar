namespace A101.RevitPlugin;

#if REVIT_SDK
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using A101.Application.UseCases;
using A101.RevitPlugin.Revit;
using Microsoft.Extensions.DependencyInjection;

[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class GenerateReinforcementCommand : IExternalCommand
{
    public Result Execute(
        ExternalCommandData commandData,
        ref string message,
        ElementSet elements)
    {
        try
        {
            var uiDoc = commandData.Application.ActiveUIDocument;
            var revitPlacer = new RevitRebarPlacer(uiDoc);
            var serviceProvider = Bootstrap.BuildServiceProvider(revitPlacer);

            var pipeline = serviceProvider.GetRequiredService<GenerateReinforcementPipeline>();

            // TODO: replace with real UI flow that builds PipelineInput from the current document/view.
            message = "GenerateReinforcementCommand is wired, but the interactive Revit UI flow is not implemented yet.";
            _ = pipeline;
            return Result.Cancelled;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return Result.Failed;
        }
    }
}
#else
internal static class GenerateReinforcementCommandPlaceholder
{
    public const string Message = "Define REVIT_SDK and provide Autodesk Revit references to build the real external command.";
}
#endif
