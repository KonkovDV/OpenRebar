using System.Windows;
using A101.Application.UseCases;
using A101.Domain.Exceptions;
using A101.Domain.Models;
using A101.Domain.Ports;
using Microsoft.Win32;

namespace A101.RevitPlugin.UI;

/// <summary>
/// WPF dialog for the A101 reinforcement generation plugin.
/// Collects user parameters and launches the pipeline.
/// </summary>
public partial class ReinforcementDialog : Window
{
    private readonly Func<PipelineInput, Task<PipelineResult>>? _pipelineRunner;
    private readonly SlabGeometry? _selectedSlab;
    private readonly string? _hostElementId;
    private readonly double _elevationOffsetFeet;

    /// <summary>
    /// Production constructor — receives a pipeline runner from Revit command context.
    /// </summary>
    public ReinforcementDialog(
        Func<PipelineInput, Task<PipelineResult>> pipelineRunner,
        SlabGeometry? selectedSlab = null,
        string? hostElementId = null,
        double elevationOffsetFeet = 0)
    {
        InitializeComponent();
        _pipelineRunner = pipelineRunner;
        _selectedSlab = selectedSlab;
        _hostElementId = hostElementId;
        _elevationOffsetFeet = elevationOffsetFeet;

        ApplySelectedSlabDefaults();
    }

    /// <summary>
    /// Design-time / standalone constructor.
    /// </summary>
    public ReinforcementDialog()
    {
        InitializeComponent();
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Выберите файл изолиний",
            Filter = "DXF files (*.dxf)|*.dxf|Image files (*.png;*.jpg)|*.png;*.jpg;*.jpeg|All files (*.*)|*.*"
        };

        if (dlg.ShowDialog() == true)
            TxtIsolineFile.Text = dlg.FileName;
    }

    private void BtnBrowseCatalog_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Выберите каталог поставщика",
            Filter = "JSON files (*.json)|*.json|CSV files (*.csv)|*.csv|All files (*.*)|*.*"
        };

        if (dlg.ShowDialog() == true)
            TxtCatalogFile.Text = dlg.FileName;
    }

    private async void BtnExecute_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtIsolineFile.Text))
        {
            MessageBox.Show("Выберите файл изолиний.", "A101", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_pipelineRunner is null)
        {
            AppendStatus("Пайплайн не подключён (standalone режим).");
            return;
        }

        BtnExecute.IsEnabled = false;
        AppendStatus("Запуск...");

        try
        {
            var concreteClass = ((System.Windows.Controls.ComboBoxItem)CmbConcrete.SelectedItem).Content.ToString()!;
            var steelClass = ((System.Windows.Controls.ComboBoxItem)CmbSteel.SelectedItem).Content.ToString()!;

            if (!double.TryParse(TxtThickness.Text, out double thickness) || thickness <= 0)
            {
                MessageBox.Show("Некорректная толщина плиты.", "A101", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!double.TryParse(TxtCover.Text, out double cover) || cover <= 0)
            {
                MessageBox.Show("Некорректный защитный слой.", "A101", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Build a sample legend (will be replaced by real legend parser in production)
            var legend = BuildDefaultLegend(steelClass);

            var slab = _selectedSlab ?? new SlabGeometry
            {
                OuterBoundary = new Polygon(
                [
                    new Point2D(0, 0),
                    new Point2D(30000, 0),
                    new Point2D(30000, 20000),
                    new Point2D(0, 20000)
                ]),
                ThicknessMm = thickness,
                CoverMm = cover,
                ConcreteClass = concreteClass
            };

            var input = new PipelineInput
            {
                IsolineFilePath = TxtIsolineFile.Text,
                Legend = legend,
                Slab = slab,
                SupplierCatalogPath = string.IsNullOrWhiteSpace(TxtCatalogFile.Text) ? null : TxtCatalogFile.Text,
                PlaceInRevit = ChkPlaceInRevit.IsChecked == true,
                PlacementSettings = new PlacementSettings
                {
                    CreateTags = ChkCreateTags.IsChecked == true,
                    CreateBendingDetails = ChkBendingDetails.IsChecked == true,
                    HostElementId = _hostElementId,
                    ElevationOffsetFeet = _elevationOffsetFeet
                }
            };

            var result = await _pipelineRunner(input);

            AppendStatus($"Зон распознано: {result.ParsedZoneCount}");
            AppendStatus($"Зон классифицировано: {result.ClassifiedZones.Count}");
            AppendStatus($"Стержней: {result.TotalRebarSegments}");
            AppendStatus($"Средний отход: {result.TotalWastePercent:F1}%");
            AppendStatus($"Общая масса: {result.TotalMassKg:F1} кг");

            if (result.PlacementResult is not null)
            {
                AppendStatus($"Размещено: {result.PlacementResult.TotalRebarsPlaced} стержней");
                foreach (var w in result.PlacementResult.Warnings)
                    AppendStatus($"  ⚠ {w}");
            }

            AppendStatus("Готово.");
        }
        catch (A101DomainException ex)
        {
            AppendStatus($"ОШИБКА [{ex.ErrorCode}]: {ex.Message}");
            MessageBox.Show($"{ex.ErrorCode}\n{ex.Message}", "A101 — Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            AppendStatus($"ОШИБКА: {ex.Message}");
            MessageBox.Show(ex.Message, "A101 — Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnExecute.IsEnabled = true;
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void AppendStatus(string line)
    {
        TxtStatus.Text += $"\n{DateTime.Now:HH:mm:ss} | {line}";
    }

    private void ApplySelectedSlabDefaults()
    {
        if (_selectedSlab is null)
            return;

        TxtThickness.Text = _selectedSlab.ThicknessMm.ToString("F0");
        TxtCover.Text = _selectedSlab.CoverMm.ToString("F0");
        SelectComboBoxItem(CmbConcrete, _selectedSlab.ConcreteClass);
        AppendStatus("Геометрия плиты извлечена из выбранного элемента Revit.");
    }

    private static void SelectComboBoxItem(System.Windows.Controls.ComboBox comboBox, string value)
    {
        foreach (var item in comboBox.Items.OfType<System.Windows.Controls.ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                break;
            }
        }
    }

    private static ColorLegend BuildDefaultLegend(string steelClass)
    {
        return new ColorLegend(
        [
            new LegendEntry(new IsolineColor(0, 0, 255),
                new ReinforcementSpec { DiameterMm = 8,  SpacingMm = 200, SteelClass = steelClass }),
            new LegendEntry(new IsolineColor(0, 255, 255),
                new ReinforcementSpec { DiameterMm = 10, SpacingMm = 200, SteelClass = steelClass }),
            new LegendEntry(new IsolineColor(0, 255, 0),
                new ReinforcementSpec { DiameterMm = 12, SpacingMm = 200, SteelClass = steelClass }),
            new LegendEntry(new IsolineColor(255, 255, 0),
                new ReinforcementSpec { DiameterMm = 14, SpacingMm = 200, SteelClass = steelClass }),
            new LegendEntry(new IsolineColor(255, 165, 0),
                new ReinforcementSpec { DiameterMm = 16, SpacingMm = 150, SteelClass = steelClass }),
            new LegendEntry(new IsolineColor(255, 0, 0),
                new ReinforcementSpec { DiameterMm = 20, SpacingMm = 150, SteelClass = steelClass }),
            new LegendEntry(new IsolineColor(255, 0, 255),
                new ReinforcementSpec { DiameterMm = 25, SpacingMm = 150, SteelClass = steelClass }),
        ]);
    }
}
