namespace A101.RevitPlugin.Revit;

#if REVIT_SDK
using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

/// <summary>
/// Restricts user selection to floor elements only.
/// </summary>
public sealed class FloorSelectionFilter : ISelectionFilter
{
    public bool AllowElement(Element element) => element is Floor;

    public bool AllowReference(Reference reference, XYZ position) => true;
}
#else
internal static class FloorSelectionFilterPlaceholder
{
    public const string Message = "Define REVIT_SDK and provide Autodesk Revit references to build the floor selection filter.";
}
#endif