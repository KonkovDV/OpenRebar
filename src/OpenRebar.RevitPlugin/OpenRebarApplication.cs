namespace OpenRebar.RevitPlugin;

#if REVIT_SDK
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

/// <summary>
/// Registers the OpenRebar ribbon tab and command button in Revit.
/// </summary>
public sealed class OpenRebarApplication : IExternalApplication
{
    private const string TabName = "OpenRebar";
    private const string PanelName = "Automation";

    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            application.CreateRibbonTab(TabName);
        }
        catch
        {
            // Tab may already exist when reloading in development sessions.
        }

        var panel = application.GetRibbonPanels(TabName).FirstOrDefault(existing => existing.Name == PanelName)
            ?? application.CreateRibbonPanel(TabName, PanelName);

        var pushButton = panel.AddItem(new PushButtonData(
            "OpenRebar_Reinforcement",
            "Армирование\nплит",
            typeof(OpenRebarApplication).Assembly.Location,
            typeof(GenerateReinforcementCommand).FullName)) as PushButton;

        if (pushButton is not null)
        {
            pushButton.ToolTip = "Автоматическое размещение дополнительной арматуры";

            var iconPath = Path.Combine(
                Path.GetDirectoryName(typeof(OpenRebarApplication).Assembly.Location) ?? string.Empty,
                "Resources",
                "icon-32.png");

            if (File.Exists(iconPath))
                pushButton.LargeImage = new BitmapImage(new Uri(iconPath, UriKind.Absolute));
        }

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
}
#else
internal static class OpenRebarApplicationPlaceholder
{
  public const string Message = "Define REVIT_SDK and provide Autodesk Revit references to build the Revit application entry point.";
}
#endif
