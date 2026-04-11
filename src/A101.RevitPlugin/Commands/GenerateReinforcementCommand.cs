namespace A101.RevitPlugin;

#if REVIT_SDK
using System.Globalization;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using A101.Application.UseCases;
using A101.RevitPlugin.Revit;
using A101.RevitPlugin.UI;
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
            var reference = uiDoc.Selection.PickObject(
                ObjectType.Element,
                new FloorSelectionFilter(),
                "Выберите плиту для армирования");

            if (reference is null)
                return Result.Cancelled;

            var floor = uiDoc.Document.GetElement(reference) as Floor;
            if (floor is null)
            {
                message = "Selected element is not a valid floor slab.";
                return Result.Failed;
            }

            var slab = RevitSlabExtractor.Extract(floor);
            double elevationOffsetFeet = floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)?.AsDouble() ?? 0;
            var revitPlacer = new RevitRebarPlacer(uiDoc);
            var serviceProvider = Bootstrap.BuildServiceProvider(revitPlacer);
            var pipeline = serviceProvider.GetRequiredService<GenerateReinforcementPipeline>();

            // Show the WPF dialog — it drives the pipeline
            var dialog = new ReinforcementDialog(input =>
                pipeline.ExecuteAsync(input),
                slab,
                floor.Id.IntegerValue.ToString(CultureInfo.InvariantCulture),
                elevationOffsetFeet);

            dialog.ShowDialog();
            return Result.Succeeded;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
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
