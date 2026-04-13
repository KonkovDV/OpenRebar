# OpenRebar-Reinforcement: Детальный План Задач

> **Для:** ИИ-программист
> **Дата:** 2026-04-12
> **Текущий статус:** OpenRebar rebrand complete. P1 Revit boundary и P3 ML pipeline подготовлены. Academic audit hardening внедрён: geometry evidence, HiGHS-backed restricted-master LP, optimizer TEVV, canonical provenance, exact small-instance CSP path, versioned normative tables. 158/158 .NET тестов проходят. Добавлены real-adapter batch benchmark harness на generated DXF fixtures и corpus-ready manifest rail для production slab batches. Дальше: Revit SDK live validation, production slab-batch corpora для cutting-quality benchmarks, ML training на реальных данных.

## Update 2026-04-12 — OpenRebar Rebrand + P1/P3 Preparation

### Что закрыто в этом wave

- **Full rebrand to OpenRebar**: namespace, projects, folders, solution file, 131 source files
- **P1 Revit boundary**: host floor structural validation, tag creation pass, bending detail tracking
- **P3 ML training pipeline**: dataset loader, augmentation, training loop, evaluation, ONNX export
- **P3 ML benchmarks**: inference latency, model size, batch throughput, ONNX exportability
- **P3 optimization master**: restricted-master LP via HiGHS with bounded-knapsack pricing, exact small-instance path, and optimizer provenance
- **Batch processing**: multi-slab reinforcement pipeline with aggregate KPIs and partial-failure capture
- **P3 batch benchmark rail**: real adapters + generated DXF fixtures + persisted report checks + FFD quality envelope
- **P3 corpus-ready batch rail**: optional manifest-driven fixture benchmark surface for production slab batches
- **Academic geometry hardening**: decomposition metrics and coverage evidence for complex zones
- **Academic optimization TEVV**: exact small-instance path + benchmark pack for score-gap and waste distribution
- **Canonical report provenance**: normative profile and analysis provenance persisted in JSON Schema contract
- **Normative data hardening**: SP 63 tables moved into versioned embedded resource with golden tests
- CLI parameterized slab geometry: `--slab-width`, `--slab-height`
- CLI boundary validation for all numeric args with clear error messages
- 8 new CLI integration tests covering happy path, edge cases, and validation
- 5 new report schema compliance tests (`ReportSchemaComplianceTests`)
- CHANGELOG.md (Keep a Changelog 1.1.0 format)
- Release workflow (`.github/workflows/release.yml`) with SBOM + attestation
- SBOM generation in CI (anchore/sbom-action)
- Artifact attestation (actions/attest-build-provenance)
- Full regression green: 158/158 .NET tests

### Осталось

- P0: Manual GitHub enablement (remote + admin)
- P1: End-to-end Revit 2025 testing (requires Revit SDK + live model)
- P3: Production slab-batch corpora for cutting-quality benchmarks
- P3: Training on real LIRA-SAPR datasets (requires annotated images)

### P0 — Manual GitHub Enablement After First Push

Это невозможно завершить локально без реального remote и прав администратора,
поэтому этот блок должен быть выполнен сразу после публикации репозитория:

1. Подключить GitHub remote и опубликовать ветку `main`
2. Включить private vulnerability reporting
3. Включить secret scanning и push protection
4. Настроить branch rulesets: PR review, required checks, linear history
5. Проверить `CODEOWNERS` и заменить `@KonkovDV`, если публикация идёт под другим owner/org

### P1 — Revit Production Boundary Completion — 🟡 IN PROGRESS

1. ~~Довести `RevitRebarPlacer` до полного production path с реальными tags и bending details~~ → Done (tag creation + bending detail tracking added)
2. ~~Добавить live validation на host element / slab selection boundary~~ → Done (structural category, compound structure, min thickness checks)
3. Протестировать end-to-end на реальной Revit 2025 среде, а не только через `StubRevitPlacer` → Requires Revit SDK

### P2 — Interop And Delivery Hardening — ✅ CLOSED

1. ~~Добавить IFC export path как официальный downstream contract~~ → Done (XbimIfcExporter)
2. ~~Закрыть AeroBIM integration loop на каноническом JSON / IFC boundary~~ → Done (AeroBimReportExporter + schema tests)
3. ~~Укрепить release lane: SBOM, artifact attestations, release notes discipline~~ → Done (release.yml + CHANGELOG.md)

### P3 — Optimization And ML Evolution — 🟡 IN PROGRESS

1. ~~Заменить текущий CG master heuristic на true LP / HiGHS-backed path~~ → Done (`ColumnGenerationOptimizer` uses `restricted-master-lp-highs` provenance with bounded-knapsack pricing and a documented fallback path)
2. ~~Добавить batch benchmark harness для раскроя~~ → Done (real adapters, generated DXF fixtures, persisted report assertions, and FFD envelope checks in `BatchReinforcementBenchmarkPackTests`)
3. Добавить production slab-batch corpora в уже подготовленный manifest-driven benchmark rail
4. ~~Собрать dataset и evaluation harness для project-specific isoline segmentation~~ → Done (training pipeline, evaluation, ONNX export, benchmarks)
5. Train on annotated LIRA-SAPR isoline dataset → Requires real data

---

## Архитектурные Правила (ОБЯЗАТЕЛЬНО СОБЛЮДАТЬ)

1. **Dependency Rule:** Domain → ничего. Application → Domain. Infrastructure → Domain + Application. RevitPlugin → все.
2. **Ports:** Всё I/O определяется интерфейсом в `src/OpenRebar.Domain/Ports/`. Adapter — в `src/OpenRebar.Infrastructure/`.
3. **DI:** Всё через `Microsoft.Extensions.DependencyInjection` в `src/OpenRebar.RevitPlugin/Bootstrap.cs`. Никаких `new Service()` вне composition root.
4. **Constructor injection only.** Никаких service locator, static service, ambient context.
5. **Revit SDK code:** Только в `src/OpenRebar.RevitPlugin/` и только внутри `#if REVIT_SDK` guard.
6. **Тесты:** xUnit + FluentAssertions + NSubstitute. Каждая задача включает acceptance-тесты.
7. **Naming:** Русские комментарии в UI. English в коде, XML-doc, тестах.
8. **Все числа** — в миллиметрах (внутренняя система). Перевод feet ↔ mm только на boundary Revit.

---

## Граф Зависимостей Задач

```
T-01 ─────────────────────────────────────────────────┐
T-02 ──────────────────────────────────┐               │
T-03 ──────────────┐                   │               │
                   │                   │               │
T-04 ◄─────────── T-03                 │               │
T-05 ◄─────────── T-03                 │               │
T-06 (независимая) │                   │               │
T-07 (независимая) │                   │               │
T-08 (независимая) │                   │               │
T-09 ◄─────────── T-01 + T-02         │               │
T-10 ◄─────────── T-08                 │               │
T-11 ◄─────────── T-09                 │               │
T-12 (независимая) │                   │               │
T-13 ◄─────────── T-09                 │               │
T-14 ◄─────────── T-01                 │               │
T-15 ◄─────────── T-09 + T-10         │               │
```

---

## TIER 1 — Блокеры MVP (без них плагин не работает)

### T-01: Реализовать RevitRebarPlacer

**Приоритет:** 🔴 КРИТИЧЕСКИЙ
**Зависимости:** нет
**Файл:** `src/OpenRebar.RevitPlugin/Revit/RevitRebarPlacer.cs`
**Текущее состояние:** Scaffold с `#if REVIT_SDK`. Есть `FindExistingBarType()`. Тело размещения — TODO-placeholder.

#### Контекст

Revit API для арматуры предоставляет два подхода:
- `Rebar.CreateFromCurves()` — одиночные стержни (точный контроль)
- `AreaReinforcement.Create()` — площадное армирование (проще, но меньше контроля)

Для OpenRebar нужен **`Rebar.CreateFromCurves()`**, потому что мы уже вычислили точные `RebarSegment` с координатами start/end.

#### Что сделать

```csharp
// Внутри цикла foreach (var segment in zone.Rebars):

// 1. Конвертировать координаты мм → feet (Revit internal units)
double mmToFeet = 1.0 / 304.8;
var startPt = new XYZ(segment.Start.X * mmToFeet, segment.Start.Y * mmToFeet, 0);
var endPt = new XYZ(segment.End.X * mmToFeet, segment.End.Y * mmToFeet, 0);

// 2. Вычислить Z-координату (зависит от layer)
double z = zone.Layer == RebarLayer.Bottom
    ? slab.CoverMm * mmToFeet
    : (slab.ThicknessMm - slab.CoverMm) * mmToFeet;
startPt = new XYZ(startPt.X, startPt.Y, z);
endPt = new XYZ(endPt.X, endPt.Y, z);

// 3. Создать Line → CurveArray
var line = Line.CreateBound(startPt, endPt);
var curves = new List<Curve> { line };

// 4. Найти host element (Floor/Slab)
// var hostElement = ... ; // нужно передать через PlacementSettings или найти по geometry

// 5. Создать Rebar
var rebar = Rebar.CreateFromCurves(
    doc,
    RebarStyle.Standard,
    barType,
    hookTypeStart: null,
    hookTypeEnd: null,
    hostElement,
    normal: XYZ.BasisZ,
    curves,
    RebarHookOrientation.Left,  // не используется без hooks
    RebarHookOrientation.Left,
    useExistingShapeIfPossible: true,
    createNewShape: true);

// 6. Установить параметры
if (settings.GroupByZone)
    rebar.LookupParameter(settings.ZoneParameterName)?.Set(zone.Id);
```

#### Доработки к PlacementSettings

Добавить в `src/OpenRebar.Domain/Ports/IRevitPlacer.cs`:

```csharp
public sealed record PlacementSettings
{
    // ... существующие поля ...

    /// <summary>Revit ElementId плиты-хоста (string для domain isolation).</summary>
    public string? HostElementId { get; init; }

    /// <summary>Elevation offset for slab (feet).</summary>
    public double ElevationOffsetFeet { get; init; }
}
```

#### Обработка ошибок

- Если `barType` не найден → добавить в `warnings`, пропустить стержень
- Если `Rebar.CreateFromCurves` бросает → добавить в `errors`, продолжить
- Если `Transaction.Commit()` бросает → `RollBack()` + error message

#### Acceptance Criteria

- [ ] При подключении Revit SDK (`#define REVIT_SDK`), код компилируется без ошибок
- [ ] Все стержни из `zone.Rebars` создаются как `Rebar` элементы
- [ ] Z-координата зависит от `RebarLayer` (Top/Bottom)
- [ ] Координаты корректно переведены мм → feet (÷304.8)
- [ ] `PlacementResult` отражает реальное количество созданных элементов
- [ ] Ошибки не роняют весь pipeline — фиксируются в `Errors`/`Warnings`

---

### T-02: Извлечение SlabGeometry Из Revit Модели

**Приоритет:** 🔴 КРИТИЧЕСКИЙ
**Зависимости:** нет
**Новый файл:** `src/OpenRebar.RevitPlugin/Revit/RevitSlabExtractor.cs`

#### Контекст

Сейчас `SlabGeometry` создаётся вручную в UI (фиксированный прямоугольник 30×20м). Нужно извлекать реальную геометрию из выбранного `Floor` элемента в Revit.

#### Что сделать

```csharp
namespace OpenRebar.RevitPlugin.Revit;

#if REVIT_SDK
using Autodesk.Revit.DB;
using OpenRebar.Domain.Models;

public static class RevitSlabExtractor
{
    private const double FeetToMm = 304.8;

    /// <summary>
    /// Extract SlabGeometry from a Revit Floor element.
    /// </summary>
    public static SlabGeometry Extract(Floor floor)
    {
        // 1. Outer boundary
        var options = new Options { DetailLevel = ViewDetailLevel.Fine };
        var geomElement = floor.get_Geometry(options);
        var outerVertices = ExtractOuterBoundary(geomElement);

        // 2. Openings (elevator shafts, column penetrations)
        var openings = ExtractOpenings(floor);

        // 3. Thickness
        var floorType = floor.FloorType;
        double thicknessMm = floorType.GetCompoundStructure()
            ?.GetLayers()
            .Sum(l => l.Width) * FeetToMm ?? 200;

        // 4. Cover — from Rebar Cover Type parameters
        double coverMm = GetRebarCover(floor);

        // 5. Concrete class — from material
        string concreteClass = GetConcreteClass(floor) ?? "B25";

        return new SlabGeometry
        {
            OuterBoundary = new Polygon(outerVertices),
            Openings = openings,
            ThicknessMm = thicknessMm,
            CoverMm = coverMm,
            ConcreteClass = concreteClass
        };
    }

    private static List<Point2D> ExtractOuterBoundary(GeometryElement geom)
    {
        // Получить нижнюю грань Solid → EdgeArray → List<Point2D>
        // Конвертировать XYZ → Point2D (× FeetToMm, отбросить Z)
        // ...
    }

    private static List<Polygon> ExtractOpenings(Floor floor)
    {
        // HostObject.FindInserts(addRectOpenings: true, ...)
        // Для каждого Opening → boundary → Polygon
        // ...
    }

    private static double GetRebarCover(Floor floor)
    {
        // floor.get_Parameter(BuiltInParameter.CLEAR_COVER_BOTTOM)
        // Default: 25mm
        // ...
    }

    private static string? GetConcreteClass(Floor floor)
    {
        // floor.FloorType → CompoundStructure → Material → Name inspection
        // Pattern match: "B25", "C25/30", etc.
        // ...
    }
}
#endif
```

#### Доработка UI

В `ReinforcementDialog.xaml.cs` заменить hardcoded slab:
```csharp
// БЫЛО: var slab = new SlabGeometry { OuterBoundary = new Polygon([...]), ... };
// СТАЛО: var slab = RevitSlabExtractor.Extract(selectedFloor);
```

#### Acceptance Criteria

- [ ] Outer boundary извлекается из реальной геометрии `Floor`
- [ ] Openings (проёмы, шахты) корректно идентифицируются
- [ ] Толщина берётся из `CompoundStructure`, а не вручную
- [ ] Cover берётся из `CLEAR_COVER_BOTTOM` параметра
- [ ] Все координаты в мм
- [ ] Fallback: если данные не найдены, используются defaults (h=200, a=25, B25)

---

### T-03: Доработать ExternalCommand + .addin Manifest

**Приоритет:** 🔴 КРИТИЧЕСКИЙ
**Зависимости:** нет
**Файлы:**
- `src/OpenRebar.RevitPlugin/Commands/GenerateReinforcementCommand.cs` (существует, scaffold)
- `src/OpenRebar.RevitPlugin/OpenRebar.addin` (НОВЫЙ)

#### Что сделать

##### 3.1. Доработка команды

В `GenerateReinforcementCommand.cs` добавить:
- Selection prompt: пользователь выбирает `Floor` элемент
- Передача `UIDocument` + выбранного `Floor` в `RevitSlabExtractor`
- Обработка `OperationCanceledException` если пользователь нажал Escape

```csharp
public Result Execute(...)
{
    var uiDoc = commandData.Application.ActiveUIDocument;

    // Prompt user to select a floor slab
    var reference = uiDoc.Selection.PickObject(
        ObjectType.Element,
        new FloorSelectionFilter(),
        "Выберите плиту для армирования");

    if (reference is null)
        return Result.Cancelled;

    var floor = uiDoc.Document.GetElement(reference) as Floor;
    // ... existing pipeline wiring ...
}
```

##### 3.2. Создать SelectionFilter

Новый файл: `src/OpenRebar.RevitPlugin/Revit/FloorSelectionFilter.cs`

```csharp
public class FloorSelectionFilter : ISelectionFilter
{
    public bool AllowElement(Element e) => e is Floor;
    public bool AllowReference(Reference r, XYZ p) => true;
}
```

##### 3.3. Создать .addin manifest

Новый файл: `src/OpenRebar.RevitPlugin/OpenRebar.addin`

```xml
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Command">
    <Name>OpenRebar Reinforcement</Name>
    <FullClassName>OpenRebar.RevitPlugin.GenerateReinforcementCommand</FullClassName>
    <Assembly>OpenRebar.RevitPlugin.dll</Assembly>
    <AddInId>OpenRebar-GUID-HERE</AddInId>
    <VendorId>OpenRebar</VendorId>
    <VendorDescription>OpenRebar Development</VendorDescription>
    <Text>Армирование плит</Text>
    <Description>Автоматическое размещение дополнительной арматуры по изолиниям LIRA-SAPR</Description>
    <VisibilityMode>AlwaysVisible</VisibilityMode>
  </AddIn>
</RevitAddIns>
```

#### Acceptance Criteria

- [ ] Пользователь может выбрать `Floor` мышью
- [ ] Только `Floor` элементы подсвечиваются при наведении
- [ ] Escape → `Result.Cancelled` (без ошибки)
- [ ] `.addin` файл содержит правильный assembly path и class name
- [ ] Pipeline запускается с реальной геометрией выбранной плиты

---

### T-04: Добавить Ribbon Tab + Button

**Приоритет:** 🟡 СРЕДНИЙ
**Зависимости:** T-03
**Новые файлы:**
- `src/OpenRebar.RevitPlugin/OpenRebarApplication.cs`
- `src/OpenRebar.RevitPlugin/Resources/icon-32.png`

#### Что сделать

Создать `IExternalApplication` для регистрации кнопки на Ribbon:

```csharp
#if REVIT_SDK
[Transaction(TransactionMode.Manual)]
public class OpenRebarApplication : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        var panel = application.CreateRibbonPanel("OpenRebar");

        var pushButton = panel.AddItem(new PushButtonData(
            "OpenRebar_Reinforcement",
            "Армирование\nплит",
            typeof(OpenRebarApplication).Assembly.Location,
            typeof(GenerateReinforcementCommand).FullName))
            as PushButton;

        pushButton.ToolTip = "Автоматическое размещение дополнительной арматуры";
        // pushButton.LargeImage = ... ; // 32x32 icon

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
}
#endif
```

Обновить `.addin`:
```xml
<AddIn Type="Application">
  <FullClassName>OpenRebar.RevitPlugin.OpenRebarApplication</FullClassName>
  ...
</AddIn>
```

#### Acceptance Criteria

- [ ] При загрузке Revit появляется вкладка "OpenRebar" с кнопкой "Армирование плит"
- [ ] Клик по кнопке открывает `ReinforcementDialog`

---

### T-05: Извлечение Legend из DXF

**Приоритет:** 🟡 СРЕДНИЙ
**Зависимости:** T-03
**Новый файл:** `src/OpenRebar.Infrastructure/DxfProcessing/DxfLegendExtractor.cs`

#### Контекст

Сейчас `ColorLegend` создаётся hardcoded в `ReinforcementDialog`. В production легенда должна читаться из DXF-файла или конфигурации.

#### Что сделать

Вариант A (конфигурационный JSON):
```json
{
  "legends": [
    { "color": [0,0,255], "diameter_mm": 8, "spacing_mm": 200, "steel_class": "A500C" },
    { "color": [0,255,0], "diameter_mm": 12, "spacing_mm": 200, "steel_class": "A500C" }
  ]
}
```

Вариант B (автоматическое извлечение из DXF): парсить текстовые entities рядом с цветными линиями.

**Рекомендация:** Начать с Вариант A (JSON legend config) — это надёжнее и покрывает 90% случаев.

#### Новый порт

```csharp
// src/OpenRebar.Domain/Ports/ILegendLoader.cs
public interface ILegendLoader
{
    Task<ColorLegend> LoadAsync(string path, CancellationToken ct = default);
    ColorLegend GetDefaultLegend(string steelClass);
}
```

#### Acceptance Criteria

- [ ] JSON legend файл десериализуется в `ColorLegend`
- [ ] Default legend совпадает с текущей hardcoded таблицей
- [ ] Невалидный JSON → domain exception с понятным сообщением
- [ ] Тест: round-trip serialize → deserialize → assert equality

---

## TIER 2 — Hardening (качество production-grade)

### T-06: Structured Logging

**Приоритет:** 🟡 СРЕДНИЙ
**Зависимости:** нет
**Новые файлы:**
- `src/OpenRebar.Domain/Ports/IStructuredLogger.cs`
- `src/OpenRebar.Infrastructure/Logging/ConsoleStructuredLogger.cs`

#### Что сделать

```csharp
// src/OpenRebar.Domain/Ports/IStructuredLogger.cs
namespace OpenRebar.Domain.Ports;

public interface IStructuredLogger
{
    void Info(string message, params (string Key, object Value)[] context);
    void Warn(string message, params (string Key, object Value)[] context);
    void Error(string message, Exception? ex = null, params (string Key, object Value)[] context);
}
```

Adapter (для standalone/CLI):
```csharp
// src/OpenRebar.Infrastructure/Logging/ConsoleStructuredLogger.cs
public sealed class ConsoleStructuredLogger : IStructuredLogger
{
    public void Info(string message, params (string Key, object Value)[] context)
    {
        var ctx = string.Join(", ", context.Select(c => $"{c.Key}={c.Value}"));
        Console.WriteLine($"[INF] {DateTime.UtcNow:HH:mm:ss.fff} {message} {ctx}");
    }
    // ... Warn, Error аналогично
}
```

**Инжектировать** в:
- `GenerateReinforcementPipeline` (лог этапов pipeline)
- `ColumnGenerationOptimizer` (лог CG iterations, waste %)
- `StandardReinforcementCalculator` (!!предупреждение о spacing violation — сейчас комментарий)

#### Acceptance Criteria

- [ ] Logger зарегистрирован в DI (`Bootstrap.cs`)
- [ ] Pipeline логирует: начало, count зон, count стержней, waste %, время
- [ ] Calculator логирует warning при `spacing > maxSpacing` (сейчас — пустой if)
- [ ] Тест: mock logger, verify calls

---

### T-07: Fix Thread Safety — _markCounter

**Приоритет:** 🟢 ПРОСТАЯ
**Зависимости:** нет
**Файл:** `src/OpenRebar.Infrastructure/ReinforcementEngine/StandardReinforcementCalculator.cs`

#### Проблема

```csharp
public sealed class StandardReinforcementCalculator : IReinforcementCalculator
{
    private int _markCounter; // ← mutable state, not thread-safe
```

#### Решение

Передавать counter как `ref` параметр или использовать `Interlocked.Increment`:

```csharp
public IReadOnlyList<ReinforcementZone> CalculateRebars(
    IReadOnlyList<ReinforcementZone> zones,
    SlabGeometry slab)
{
    int markCounter = 0; // ← local variable, не поле класса

    foreach (var zone in zones)
    {
        var rebars = GenerateRebarsForZone(zone, slab, ref markCounter);
        zone.Rebars = rebars;
    }

    return zones;
}
```

#### Acceptance Criteria

- [ ] `_markCounter` удалён как поле класса
- [ ] Mark numbering по-прежнему последовательное (1, 2, 3, ...)
- [ ] Два вызова `CalculateRebars` дают независимую нумерацию
- [ ] Существующие тесты проходят

---

### T-08: Domain Exceptions + Error Handling

**Приоритет:** 🟡 СРЕДНИЙ
**Зависимости:** нет
**Новые файлы:**
- `src/OpenRebar.Domain/Exceptions/OpenRebarDomainException.cs`
- `src/OpenRebar.Domain/Exceptions/InvalidIsolineFileException.cs`
- `src/OpenRebar.Domain/Exceptions/OptimizationException.cs`
- `src/OpenRebar.Domain/Exceptions/NormativeViolationException.cs`

#### Что сделать

```csharp
namespace OpenRebar.Domain.Exceptions;

public abstract class OpenRebarDomainException : Exception
{
    public string ErrorCode { get; }
    protected OpenRebarDomainException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }
}

public class InvalidIsolineFileException : OpenRebarDomainException
{
    public InvalidIsolineFileException(string filePath, string reason)
        : base("ISOLINE_INVALID", $"Invalid isoline file '{filePath}': {reason}") { }
}

public class NormativeViolationException : OpenRebarDomainException
{
    public string Clause { get; }
    public NormativeViolationException(string clause, string message)
        : base("SP63_VIOLATION", $"SP 63 §{clause}: {message}")
    {
        Clause = clause;
    }
}

public class OptimizationException : OpenRebarDomainException
{
    public OptimizationException(string message)
        : base("OPTIMIZATION_FAILED", message) { }
}
```

**Внедрить** в:
- `DxfIsolineParser` — если DXF невалидный
- `PngIsolineParser` — если формат нераспознан
- `ColumnGenerationOptimizer` — если нет in-stock lengths
- `GenerateReinforcementPipeline` — если spacing > SP 63 limit → NormativeViolation (warning, не exception)

#### Acceptance Criteria

- [ ] Exception hierarchy с `ErrorCode` для программного анализа
- [ ] Каждый adapter бросает domain exception (не raw ArgumentException)
- [ ] UI (`ReinforcementDialog`) ловит `OpenRebarDomainException` и показывает `ErrorCode` + `Message`
- [ ] Тесты: verify exception types thrown on invalid input

---

### T-09: Integration Test — Full Pipeline E2E ✅

**Приоритет:** 🟡 СРЕДНИЙ
**Зависимости:** T-01, T-02
**Новый файл:** `tests/OpenRebar.Application.Tests/FullPipelineIntegrationTests.cs`

#### Что сделать

End-to-end test: DXF файл → parse → classify → calculate → optimize → verify output.

```csharp
public class FullPipelineIntegrationTests
{
    [Fact]
    public async Task SampleDxfFile_ProducesValidCuttingPlan()
    {
        // Arrange: real DXF parser, real calculator, real optimizer, stub placer
        var sp = Bootstrap.BuildServiceProvider(new StubRevitPlacer());
        var pipeline = sp.GetRequiredService<GenerateReinforcementPipeline>();

        var input = new PipelineInput
        {
            IsolineFilePath = "TestData/sample_isoline.dxf",
            Legend = TestLegends.Standard7ColorA500C(),
            Slab = TestSlabs.Standard200mmB25(),
            PlaceInRevit = false
        };

        // Act
        var result = await pipeline.ExecuteAsync(input);

        // Assert
        result.ParsedZoneCount.Should().BeGreaterThan(0);
        result.ClassifiedZones.Should().NotBeEmpty();
        result.TotalRebarSegments.Should().BeGreaterThan(0);
        result.TotalWastePercent.Should().BeLessThan(20);
        result.OptimizationResults.Should().NotBeEmpty();
        // SP 63: anchorage lengths should be ≥ 200mm
        result.ClassifiedZones
            .SelectMany(z => z.Rebars)
            .Should().OnlyContain(r => r.AnchorageLengthStart >= 200);
    }
}
```

#### Реализованный подход

Вместо коммита бинарного fixture DXF создаётся программно прямо в тесте через `IxMilia.Dxf`, что делает сценарий self-contained и упрощает поддержку.

#### Acceptance Criteria

- [x] E2E тест проходит с реальными (не mock) адаптерами
- [x] Проверяется: parsing → classification → rebar count → waste % → anchorage ≥ 200mm
- [x] Покрыт persistence path: JSON report реально записывается и валидируется по содержимому
- [x] DXF fixture создаётся программно внутри теста, без внешнего тестового файла

---

## TIER 3 — Production Features

### T-10: Result Persistence — JSON Report Export

**Приоритет:** 🟡 СРЕДНИЙ
**Зависимости:** T-08
**Новые файлы:**
- `src/OpenRebar.Domain/Ports/IReportExporter.cs`
- `src/OpenRebar.Infrastructure/Reporting/JsonReportExporter.cs`

#### Что сделать

```csharp
// src/OpenRebar.Domain/Ports/IReportExporter.cs
public interface IReportExporter
{
    Task ExportAsync(PipelineResult result, PipelineInput input,
        string outputPath, CancellationToken ct = default);
}
```

Формат JSON:
```json
{
  "timestamp": "2026-04-11T18:00:00Z",
  "input": {
    "isoline_file": "floor_03.dxf",
    "slab_thickness_mm": 200,
    "concrete_class": "B25",
    "steel_class": "A500C"
  },
  "zones": [ { "id": "Z-001", "type": "Simple", ... } ],
  "optimization": {
    "by_diameter": [
      { "diameter_mm": 12, "bars_needed": 15, "waste_percent": 3.2, "mass_kg": 120.5 }
    ],
    "total_waste_percent": 4.1,
    "total_mass_kg": 450.0,
    "total_stock_bars": 42
  }
}
```

Зарегистрировать в DI. Вызвать в конце pipeline (если `outputPath != null`).

#### Acceptance Criteria

- [ ] JSON файл создаётся на диск
- [ ] Содержит все зоны, стержни, cutting plans, метрики
- [ ] Round-trip: десериализация → проверка полей
- [ ] Кириллические пути работают (`System.Text.Json` с `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`)

---

### T-11: Rebar Schedule Export (Ведомость Арматуры)

**Приоритет:** 🟢 ПРОСТАЯ
**Зависимости:** T-09
**Новые файлы:**
- `src/OpenRebar.Domain/Ports/IScheduleExporter.cs`
- `src/OpenRebar.Infrastructure/Reporting/CsvScheduleExporter.cs`

#### Формат CSV

```csv
Марка;Диаметр, мм;Длина, мм;Количество;Масса 1 шт, кг;Масса всего, кг;Класс стали
1;12;2450;15;2.18;32.7;A500C
2;16;3800;8;5.92;47.3;A500C
```

#### Acceptance Criteria

- [ ] CSV файл с разделителем `;` (Russian Excel default)
- [ ] Заголовок на русском
- [ ] Масса рассчитывается через `ReinforcementLimits.GetLinearMass()`
- [ ] Тест: verify CSV content line by line

---

### T-12: LP Solver Upgrade → HiGHS

**Приоритет:** 🟡 СРЕДНИЙ
**Зависимости:** нет (можно делать параллельно)
**Файл:** `src/OpenRebar.Infrastructure/Optimization/ColumnGenerationOptimizer.cs`
**NuGet:** `Highs.Native` (MIT license)

#### Контекст

Текущий `SolveRestrictedMasterLP` — coordinate descent (приближённый). Нужно заменить на полноценный LP solver.

#### Что сделать

1. Добавить NuGet: `Highs.Native`
2. Заменить `SolveRestrictedMasterLP`:

```csharp
private static (double[]? Solution, double[] Duals) SolveRestrictedMasterLP(
    List<int[]> patterns, int[] demand, int m)
{
    int n = patterns.Count;
    var highs = new Highs();

    // Objective: min Σ x_j (minimize number of stock bars used)
    // Variables: x_0 ... x_{n-1} ≥ 0
    // Constraints: Σ a_{ij} * x_j ≥ demand_i  for all i

    // Add variables
    for (int j = 0; j < n; j++)
        highs.AddVariable(0, double.PositiveInfinity, 1.0); // cost = 1 per bar

    // Add constraints
    for (int i = 0; i < m; i++)
    {
        var coeffs = new double[n];
        for (int j = 0; j < n; j++)
            coeffs[j] = patterns[j][i];
        highs.AddRow(demand[i], double.PositiveInfinity, coeffs);
    }

    highs.Solve();

    // Extract primal + dual
    var x = highs.GetSolution();
    var duals = highs.GetDualValues();

    return (x, duals);
}
```

3. **Оставить fallback:** если HiGHS недоступен → вернуться к coordinate descent.

#### Acceptance Criteria

- [ ] Все существующие тесты `ColumnGenerationOptimizerTests` проходят
- [ ] `MixedLengths_ShouldBeatFFD` — waste % ≤ FFD waste %
- [ ] `TypicalSlabScenario_ShouldComplete` — ≤ FFD bars needed (строго)
- [ ] Performance: 50 rebars → ≤ 1 секунда

---

### T-13: IFC Export для интеграции с AeroBIM — ✅ CLOSED

**Приоритет:** 🟡 СРЕДНИЙ
**Зависимости:** T-09
**Новые файлы:**
- `src/OpenRebar.Domain/Ports/IIfcExporter.cs`
- `src/OpenRebar.Infrastructure/Export/XbimIfcExporter.cs`
**NuGet:** `Xbim.Essentials`, `Xbim.Geometry`

#### Что сделано

```csharp
// src/OpenRebar.Domain/Ports/IIfcExporter.cs
namespace OpenRebar.Domain.Ports;

public interface IIfcExporter
{
    Task ExportAsync(
        IReadOnlyList<ReinforcementZone> zones,
        SlabGeometry slab,
        string outputPath,
        CancellationToken ct = default);
}
```

IFC entities:
- `IfcReinforcingBar` per segment
- `IfcReinforcingBarType` per diameter+steel class combination
- `IfcPropertySet` → `Pset_ReinforcingBarBendingsBECCommon` (NominalDiameter, BarLength, SteelGrade)
- `IfcQuantityArea` → Reinforcement area per zone
- `IfcRelAssociatesMaterial` → steel material

#### Статус

- `IIfcExporter` и `XbimIfcExporter` реализованы и подключены в infrastructure layer
- DI wiring закрыт в `src/OpenRebar.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- Acceptance coverage закрыта в `tests/OpenRebar.Infrastructure.Tests/Export/XbimIfcExporterTests.cs`
- Генерируется валидный IFC4 файл
- Каждый `RebarSegment` экспортируется как `IfcReinforcingBar`
- xBIM повторно открывает сгенерированный файл и валидирует material reuse

---

### T-14: Multi-Slab Batch Processing — ✅ CLOSED

**Приоритет:** 🟢 ПРОСТАЯ
**Зависимости:** T-01
**Новый файл:** `src/OpenRebar.Application/UseCases/BatchReinforcementPipeline.cs`

#### Что сделано

```csharp
public sealed class BatchReinforcementPipeline
{
    private readonly GenerateReinforcementPipeline _singlePipeline;

    public async Task<BatchResult> ExecuteAsync(
        IReadOnlyList<PipelineInput> inputs,
        CancellationToken ct = default)
    {
        var results = new List<(string SlabId, PipelineResult Result)>();

        foreach (var input in inputs)
        {
            ct.ThrowIfCancellationRequested();
            var result = await _singlePipeline.ExecuteAsync(input, ct);
            results.Add((Path.GetFileNameWithoutExtension(input.IsolineFilePath), result));
        }

        return new BatchResult
        {
            SlabResults = results,
            TotalMassKg = results.Sum(r => r.Result.TotalMassKg),
            AverageWastePercent = results.Average(r => r.Result.TotalWastePercent),
            TotalStockBars = results.Sum(r =>
                r.Result.OptimizationResults.Values.Sum(o => o.TotalStockBarsNeeded))
        };
    }
}
```

#### Статус

- `BatchReinforcementPipeline` реализован в `src/OpenRebar.Application/UseCases/BatchReinforcementPipeline.cs`
- DI wiring добавлен в `src/OpenRebar.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- Acceptance tests закрыты в `tests/OpenRebar.Application.Tests/BatchReinforcementPipelineTests.cs`
- Batch из 3 плит агрегирует результаты и KPI
- `CancellationToken` прерывает между плитами
- Ошибка одной плиты не роняет весь batch и сохраняется в `Failures`

---

### T-15: AeroBIM Integration Adapter — ✅ CLOSED

**Приоритет:** 🟢 ПРОСТАЯ
**Зависимости:** T-09, T-10
**Новый файл:** `src/OpenRebar.Infrastructure/Export/AeroBimReportExporter.cs`

#### Что сделано

`AeroBimReportExporter` реализован в `src/OpenRebar.Infrastructure/Export/AeroBimReportExporter.cs` и генерирует downstream-friendly JSON по каноническому report contract.

#### Формат

```json
{
  "$schema": "aerobim-OpenRebar-reinforcement-report/v1",
  "project_id": "рк-25-0042",
  "slab_id": "Плита_Этаж_03",
  "concrete_class": "B25",
  "zones": [
    {
      "zone_id": "Z-001",
      "boundary_mm": [[0,0],[5000,0],[5000,3000],[0,3000]],
      "diameter_mm": 12,
      "spacing_mm": 200,
      "steel_class": "A500C",
      "direction": "X",
      "layer": "Bottom",
      "anchorage_mm": 500,
      "lap_splice_mm": 1000,
      "rebar_count": 15,
      "total_length_mm": 36750
    }
  ],
  "optimization": {
    "optimizer": "ColumnGeneration",
    "cutting_plans": [
      { "stock_mm": 11700, "cuts_mm": [5000, 5000], "waste_mm": 1700 }
    ],
    "total_waste_percent": 4.2,
    "total_mass_kg": 1250.0,
    "total_stock_bars": 42
  },
  "normative_basis": "SP 63.13330.2018"
}
```

#### Статус

- `AeroBimReportExporter` реализован и зарегистрирован в DI
- Schema/report coverage закрыта в `tests/OpenRebar.Infrastructure.Tests/Export/AeroBimReportExporterTests.cs`
- Канонический JSON report store и schema compliance закрыты в `tests/OpenRebar.Infrastructure.Tests/Reporting/JsonFileReportStoreTests.cs` и `tests/OpenRebar.Infrastructure.Tests/Reporting/ReportSchemaComplianceTests.cs`
- JSON генерируется по schema и включает зоны, cutting plans, нормативный профиль и provenance

---

## Checklist Для Каждой Задачи

Перед тем как считать задачу завершённой, ИИ-программист должен:

- [ ] Написать код в правильном слое (Domain/Application/Infrastructure/Plugin)
- [ ] Зарегистрировать в DI (`Bootstrap.cs`) если это новый service
- [ ] Написать unit/integration тесты
- [ ] Убедиться что `dotnet build OpenRebar.sln` проходит без errors/warnings
- [ ] Убедиться что `dotnet test OpenRebar.sln` проходит (все тесты зелёные)
- [ ] Добавить XML-doc комментарии к public API
- [ ] Не нарушать dependency rule (проверить: Domain не импортирует Infrastructure)

---

## Порядок Выполнения (Рекомендация)

```
Неделя 1:  T-06 (logging) → T-07 (thread safety) → T-08 (exceptions)
Неделя 2:  T-05 (legend loader) → T-10 (JSON report) → T-11 (CSV schedule)
Неделя 3:  T-01 (RevitRebarPlacer) → T-02 (SlabExtractor)
Неделя 4:  T-03 (Command + .addin) → T-04 (Ribbon)
Неделя 5:  T-09 (E2E test) → T-12 (HiGHS LP)
Неделя 6:  T-13 (IFC export) → T-14 (batch) → T-15 (AeroBIM adapter)
```

**Логика:** сначала hardening (T-06..T-08, не требуют Revit SDK), потом features (T-05, T-10, T-11), потом Revit-specific (T-01..T-04, требуют SDK), затем интеграция.
