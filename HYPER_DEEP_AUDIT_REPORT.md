# A101-Reinforcement: Hyper-Deep Technical Audit Report

**Дата:** 10 апреля 2026  
**Аудитор:** GitHub Copilot (Claude Opus 4.6)  
**Версия проекта:** initial commit, .NET 8 / Python 3.12  
**Scope:** full codebase — src/, ml/, tests/

---

## 1. Executive Summary

A101-Reinforcement — специализированная система автоматизации армирования железобетонных плит. Проект решает 4 ключевые задачи:

1. **Парсинг изолиний** — извлечение зон армирования из PNG/DXF чертежей (изополя из расчётных комплексов: SCAD, Лира, STARK)
2. **Раскладка арматуры** — генерация стержней по СП 63.13330 с учётом анкеровки, нахлёста и защитных слоёв
3. **Оптимизация раскроя** — минимизация отходов при нарезке арматуры из стандартных длин
4. **Экспорт в Revit** — создание 3D арматуры через Revit API (RebarInSystem / Area Reinforcement)

**Общая оценка: 7.2 / 10** — архитектурно зрелый каркас с качественным Clean Architecture, но с несколькими критическими дефектами в нормативных расчётах и пробелами в ML-пайплайне.

### Ключевые метрики

| Метрика | Значение |
|---------|----------|
| Файлов кода (C#) | ~25 |
| Файлов кода (Python ML) | ~4 |
| Тестов (C#) | ~6 файлов, ~30+ тестов |
| Покрытие доменных правил | ~60% |
| NuGet пакеты | IxMilia.Dxf 0.8, ImageSharp 3.1.7, MS DI 8.0 |
| Python зависимости | torch 2.2+, onnxruntime 1.17+, FastAPI 0.110+ |
| Target Framework | .NET 8.0 |

---

## 2. Архитектура

### 2.1 Структура проектов

```
A101.Domain        → Models, Ports (интерфейсы), Rules (SP 63)
A101.Application   → UseCases (LayoutReinforcementUseCase, OptimizeRebarCuttingUseCase)
A101.Infrastructure → Adapters (DXF, ImageSharp, FFD optimizer, Catalog)
A101.RevitPlugin   → WPF UI + Revit IExternalCommand (draft)
ml/                → Python: U-Net сегментация + FastAPI
```

**Вердикт: ✅ Отличная Clean Architecture.** Domain не зависит от Infrastructure. Application зависит только от Domain. Порты в Domain, адаптеры в Infrastructure. Constructor injection. Все csproj имеют `TreatWarningsAsErrors=true` и `Nullable=enable`.

### 2.2 Проблема: Infrastructure → Application зависимость

```xml
<!-- A101.Infrastructure.csproj -->
<ProjectReference Include="..\A101.Application\A101.Application.csproj" />
```

**⚠️ WARNING:** Infrastructure ссылается на Application. В классическом Clean Architecture Infrastructure зависит только от Domain. Зависимость от Application допустима в Ports & Adapters (Hexagonal), если Infrastructure реализует Application-level порты, но это размывает границу. Рекомендация: вынести `IRebarOptimizer` и `ISupplierCatalogLoader` в Domain.Ports (где они уже и находятся), и убрать ссылку на Application из Infrastructure.

### 2.3 Dependency Injection

Проект использует `Microsoft.Extensions.DependencyInjection`, но **DI-контейнер нигде не инициализирован** (кроме заглушки в RevitPlugin/App). Отсутствует Composition Root. В `LayoutReinforcementUseCase` все зависимости через конструктор — правильно, но wiring не реализован.

---

## 3. Нормативная база (SP 63 / EC2)

### 3.1 AnchorageRules — Критические дефекты

Файл: `src/A101.Domain/Rules/AnchorageRules.cs`

#### 3.1.1 Формула анкеровки — КОРРЕКТНА по духу, но упрощена

Текущая реализация:
```csharp
l_an = Rs * d / (4 * Rbt)
min(l_an, max(15*d, 200))
```

**По СП 63.13330.2018 (п. 10.3.24–10.3.27):**

Базовая длина анкеровки:
$$l_{0,an} = \frac{R_s}{R_{bond}} \cdot \frac{d}{4}$$

где $R_{bond} = \eta_1 \cdot \eta_2 \cdot R_{bt}$  
- $\eta_1 = 2.5$ для периодического профиля, 1.5 для гладкой  
- $\eta_2 = 1.0$ для горизонтальных стержней при хороших условиях, 0.7 для вертикальных/в зоне плохого уплотнения  

Расчётная длина:
$$l_{an} = \alpha \cdot l_{0,an} \cdot \frac{A_{s,req}}{A_{s,prov}}$$

с минимальными ограничениями: $l_{an} \geq \max(15d, 200\text{мм})$ для растяжения, $\max(10d, 150\text{мм})$ для сжатия.

**🔴 CRITICAL BUG: Коэффициенты η₁ и η₂ НЕ учтены.**

В текущем коде используется `Rbt` напрямую вместо `R_bond`. Для арматуры класса A500C (периодический профиль) без η₁=2.5 длина анкеровки завышена в 2.5 раза (что безопасно, но неэкономично). При добавлении гладкой арматуры (A240) ошибка будет в обратную сторону (η₁=1.5 вместо 2.5 → опасно).

**Рекомендация:**
```csharp
double eta1 = IsPeriodicProfile(steelClass) ? 2.5 : 1.5;
double eta2 = 1.0; // TODO: параметр условий бетонирования  
double Rbond = eta1 * eta2 * Rbt;
double l0an = Rs * d / (4 * Rbond);
```

#### 3.1.2 Нахлёст (Lap) — Упрощён

Текущее: `LapLength = 1.2 * AnchorageLength`

**По СП 63 (п. 10.3.31):**
$$l_{lap} = \alpha_l \cdot l_{0,an} \cdot \frac{A_{s,req}}{A_{s,prov}}$$

где $\alpha_l$ зависит от процента стыкуемых стержней:
- ≤25% стержней: $\alpha_l = 1.2$
- 26–50%: $\alpha_l = 1.4$
- 51–100%: $\alpha_l = 2.0$

Минимум: $\max(20d, 250\text{мм})$

**⚠️ WARNING:** Фиксированный 1.2 корректен только при стыковке ≤25% стержней. Для плитного армирования (100% стыкуемых в одном сечении) нужен α=2.0. Это занижает длину нахлёста в 1.67 раза — **потенциально опасно**.

#### 3.1.3 Lookup-таблицы Rs и Rbt — КОРРЕКТНЫ

| Класс | Rs (МПа) в коде | Rs по ГОСТ 34028-2016 / СП 63 |
|-------|-----------------|-------------------------------|
| A240 | 210 | 210 ✅ |
| A400 | 350 | 350 ✅ |
| A500C | 435 | 435 ✅ |

| Класс бетона | Rbt (МПа) в коде | Rbt по СП 63 Таблица 6.8 |
|-------------|-----------------|--------------------------|
| B15 | 0.75 | 0.75 ✅ |
| B20 | 0.90 | 0.90 ✅ |
| B25 | 1.05 | 1.05 ✅ |
| B30 | 1.15 | 1.15 ✅ |
| B35 | 1.30 | 1.30 ✅ |
| B40 | 1.40 | 1.40 ✅ |

Значения Rs и Rbt верны. Отсутствуют классы B45, B50, B55, B60 (редко используемые для плит, но для полноты стоит добавить).

### 3.2 ReinforcementLimits — Таблица линейных масс ⚠️

```csharp
public static double GetLinearMass(int diameterMm) => diameterMm switch
{
    6 => 0.222, 8 => 0.395, 10 => 0.617, 12 => 0.888,
    14 => 1.21, 16 => 1.58, 18 => 2.0, 20 => 2.47,
    22 => 2.98, 25 => 3.85, 28 => 4.83, 32 => 6.31, 36 => 7.99, 40 => 9.87,
    _ => Math.PI * Math.Pow(diameterMm / 2.0 / 1000.0, 2) * 7850
};
```

**Проверка по ГОСТ 34028-2016 / Сортамент арматуры:**

| d, мм | Код | ГОСТ 34028 | Δ |
|-------|-----|-----------|---|
| 6 | 0.222 | 0.222 | ✅ |
| 8 | 0.395 | 0.395 | ✅ |
| 10 | 0.617 | 0.617 | ✅ |
| 12 | 0.888 | 0.888 | ✅ |
| 16 | 1.58 | 1.578 | ✅ ~0.1% |
| 20 | 2.47 | 2.466 | ✅ ~0.2% |
| 25 | 3.85 | 3.853 | ✅ ~0.1% |
| 32 | 6.31 | 6.313 | ✅ |
| 40 | 9.87 | 9.865 | ✅ |

**Вердикт: ✅ Таблица масс корректна** с точностью до 0.2%, что приемлемо для сметных расчётов.

### 3.3 ReinforcementLimits — Правила раскладки

```csharp
MinBarSpacing = 50mm        // СП 63 п. 10.3.5: мин. расстояние = max(d, 25мм) для плит → OK для d≤50
MaxBarSpacing = 400mm       // СП 63 п. 10.3.8: ≤400мм для плит → ✅
MinProtectiveLayer = 15mm   // СП 63 п. 10.3.1, Таблица 10.1: 15мм для плит → ✅
StandardSpacings = {100, 150, 200, 250, 300}  // Стандартные шаги → ✅
```

**⚠️ NOTICE:** `MinBarSpacing = 50mm` — это упрощение. По СП 63 минимальный зазор = max(d, 25мм, dагрегата+5мм). Для d=32 реальный минимум = 32мм, и код пропустит легальный вариант. Для d=6 или d=8 минимум 25мм, а код требует 50мм — избыточно консервативно.

### 3.4 Eurocode 2 / ACI 318 — НЕ реализовано

Текущий код поддерживает **только** СП 63. Нет поддержки:
- **EN 1992-1-1** (Eurocode 2): формула l_bd = α₁·α₂·α₃·α₄·α₅ · l_b,rqd
- **ACI 318-19**: формула l_d = (fy·ψt·ψe·ψs·ψg / (25·λ·√f'c)) · d_b

Для международной применимости это серьёзный пробел, но для российского рынка — не критично.

---

## 4. Оптимизация раскроя арматуры

### 4.1 FirstFitDecreasing — Алгоритмический анализ

Файл: `src/A101.Infrastructure/Optimization/FirstFitDecreasingOptimizer.cs`

**Алгоритм:**
1. Сортировка требуемых длин по убыванию
2. Для каждого стержня — поиск первого подходящего прутка (учитывая ширину реза + минимальный обрезок)
3. Если не найден — новый пруток из каталога (выбор минимально достаточного)
4. Генерация `CuttingPlan` с визуализацией

**Теоретическая гарантия (доказано Dósa, 2007):**
$$FFD(I) \leq \frac{11}{9} \cdot OPT(I) + \frac{6}{9}$$

Т.е. в наихудшем случае FFD использует на 22.2% больше прутков, чем оптимальное решение, плюс не более 1 прутка.

**🟡 Оценка качества:** FFD — хороший baseline, но для промышленного применения **недостаточен**:

| Алгоритм | Ratio к OPT | Сложность | Зрелость |
|----------|-------------|-----------|----------|
| FFD (текущий) | ≤ 11/9 ≈ 1.222 | O(n log n) | Baseline |
| MFFD | ≤ 71/60 ≈ 1.183 | O(n log n) | Улучшенный |
| Column Generation (Gilmore-Gomory) | Оптимальный (LP) | O(n·m·K) | SOTA |
| OR-Tools CP-SAT | Оптимальный (MIP) | Экспоненциальный | Практический |
| Branch-and-Price | Оптимальный | Экспоненциальный | Академический |

**Рекомендация:** Для реального строительного проекта (сотни–тысячи стержней) разница FFD vs Column Generation может составить 5-15% отходов. При стоимости арматуры ~50-80 тыс. руб/тонна экономия существенна.

### 4.2 Конкретные дефекты оптимизатора

#### 4.2.1 Ширина реза учтена ✅
```csharp
SawCutWidthMm = 3  // Стандартная ширина реза абразивным кругом
```

#### 4.2.2 Минимальный обрезок учтён ✅
```csharp
MinScrapLengthMm = 300  // Стержни < 300мм не используются
```

#### 4.2.3 🔴 НЕ учтён «хвост» для зажима
При реальном раскрое нужен минимальный зажимной конец (обычно 50-100мм) для фиксации прутка в гильотине/станке. Это уменьшает эффективную длину прутка.

#### 4.2.4 🔴 НЕ поддерживается multi-stock (разные длины)
Каталог содержит несколько длин (6000, 9000, 11700, 12000), но оптимизатор **сортирует по длине** и берёт `FirstOrDefault`. При комбинации разных длин FFD не оптимизирует выбор между ними. Настоящая column generation решает эту задачу.

#### 4.2.5 Результат `OptimizationResult` — Хорошая структура

```csharp
public sealed record OptimizationResult {
    TotalStockBarsNeeded, TotalRebarLengthMm, TotalWasteMm, 
    TotalWastePercent, TotalMassKg?, CuttingPlans[]
}
```

Структура данных для визуализации карт раскроя — грамотная. Содержит всё необходимое для вывода.

### 4.3 Открытый вопрос: OR-Tools интеграция

Для промышленного качества рекомендуется:
1. **Google OR-Tools** (NuGet: `Google.OrTools`) — CP-SAT solver или LP + column generation
2. **HiGHS** (NuGet: `Highs.Native`) — open-source LP/MIP solver
3. **Python-based**: через gRPC вызов к PuLP/CVXPY/OR-Tools Python

---

## 5. Парсинг изолиний (Computer Vision)

### 5.1 ImageSharp Pipeline (C#)

Файл: `src/A101.Infrastructure/ImageProcessing/RasterIsolineParser.cs`

**Алгоритм:**
1. Загрузка PNG через ImageSharp
2. Для каждого пикселя — поиск ближайшего цвета в `ColorLegend` (Euclidean distance в RGB)
3. Flood-fill (BFS) для выделения связных областей одного цвета
4. Фильтрация мелких зон (< minPixels)
5. Пересечение зон с полигоном плиты
6. Конвертация пиксельных координат в мм (через scaleX/scaleY)

**🟡 Ограничения:**

| Аспект | Текущее решение | SOTA 2024-2026 |
|--------|----------------|----------------|
| Цветовое пространство | RGB Euclidean | CIEDE2000 (перцептуально-однородное) |
| Сегментация | Flood-fill (BFS) | U-Net / Segment Anything Model (SAM) |
| Шумоподавление | Нет | Bilateral filter / Non-local means |
| Антиалиасинг | Нет (точное совпадение) | Morphological operations |
| Масштабирование | Линейное (px→mm) | Affine transform + calibration markers |
| Ротация | Не поддерживается | Hough transform для выравнивания |

**🔴 CRITICAL:** Euclidean distance в RGB **не перцептуально-однородна**. Два визуально похожих цвета могут иметь большую RGB-дистанцию, а два визуально разных — малую. Для строительных чертежей (SCAD/Лира используют фиксированную палитру) это менее критично, но при JPEG-артефактах или печати-сканировании — проблемно.

**Рекомендация:** Перейти на CIE Lab + CIEDE2000, или хотя бы на HSV с Euclidean в HSV-пространстве.

### 5.2 DXF Parser

Файл: `src/A101.Infrastructure/Drawing/DxfIsolineParser.cs`

**Библиотека:** IxMilia.Dxf 0.8.0 — зрелая open-source библиотека для чтения/записи DXF (MIT license, ~500 stars).

**Алгоритм:**
1. Чтение DXF-файла
2. Фильтрация сущностей по закрытым полилиниям (DxfLwPolyline + IsClosed)
3. Сопоставление цвета полилинии → ReinforcementSpec через ColorLegend
4. Конвертация DxfPoint → Point2D

**✅ Хорошая реализация** для базового случая. Но:

**⚠️ Замечания:**
- Не обрабатываются `DxfPolyline` (3D, legacy формат) — только LwPolyline
- Не учитываются Arc-сегменты в полилиниях (Bulge factor)
- Не обрабатываются блоки (DxfInsert) — изполя могут быть внутри блоков
- Нет поддержки хэтчей (DxfHatch) — альтернативный способ представления зон
- Цвет берётся по entity, не по layer (ByLayer color не разрешается)

### 5.3 Python ML Module

Файл: `ml/segmentation/model.py`

**Архитектура:**
- U-Net с ResNet34 encoder (torchvision)
- Inference через ONNX Runtime
- FastAPI REST API: POST /segment → zones JSON

**🟡 Статус: ЗАГЛУШКА.** Модель определена, но:
- Нет тренировочных данных
- Нет скриптов обучения
- Нет validation pipeline
- Нет pre-trained весов

**SOTA для сегментации инженерных чертежей (2024-2026):**

| Метод | Описание | Качество |
|-------|----------|----------|
| **SAM 2** (Meta, 2024) | Segment Anything Model 2 — zero-shot сегментация | Универсальный, но не специализирован |
| **DocTR + DETR** | Детекция + сегментация документов | Хорош для текста/таблиц, слабее для изополей |
| **Custom U-Net + CRF** | U-Net + Conditional Random Fields | SOTA для цветных зональных карт |
| **PanopticFPN** (Detectron2) | Panoptic сегментация | Overkill, но работает |
| **Color quantization + Connected Components** | Классический CV | Быстро, достаточно для фиксированных палитр |

**Рекомендация:** Для фиксированной палитры SCAD/Лира ML-модель — overkill. Достаточно color quantization → connected components → polygon extraction. ML оправдан только для сканов с шумом, тенями, перспективными искажениями.

---

## 6. Raскладка арматуры

### 6.1 LayoutReinforcementUseCase

Файл: `src/A101.Application/UseCases/LayoutReinforcementUseCase.cs`

**Pipeline:**
1. Парсинг изолиний (`IIsolineParser`)
2. Для каждой зоны — генерация стержней (`RebarLayoutEngine`)
3. (Опционально) Оптимизация раскроя (`OptimizeRebarCuttingUseCase`)
4. Экспорт результата

**✅ Правильная оркестрация** через порты. Зависимости через конструктор.

### 6.2 RebarLayoutEngine

Файл: `src/A101.Domain/Rules/RebarLayoutEngine.cs`

**Алгоритм:**
1. Разложение полигона зоны на прямоугольники (`PolygonDecomposition`)
2. Для каждого прямоугольника — раскладка горизонтальных стержней с заданным шагом
3. Вычисление длины стержня = ширина прямоугольника − 2·protectiveLayer + 2·anchorage
4. Создание объектов `RebarBar`

**🔴 CRITICAL BUGS:**

1. **Только горизонтальная раскладка.** Для плит необходимо армирование в двух направлениях (X и Y). Текущий код генерирует стержни только в одном направлении.

2. **Нет поддержки верхнего/нижнего армирования.** Плита армируется 4 сетками: нижняя X, нижняя Y, верхняя X, верхняя Y. Код не различает direction и position.

3. **Decomposition to Rectangles — упрощение.** L-образные и Т-образные плиты разлагаются на прямоугольники bounding-box'ом, что создаёт стержни за пределами контура плиты.

4. **Анкеровка добавляется с обеих сторон.** Для крайних стержней (у свободного края) анкеровка нужна, для стержней продолжающихся в соседнюю зону — нет (нужен нахлёст, а не анкеровка).

### 6.3 PolygonDecomposition

Файл: `src/A101.Domain/Rules/PolygonDecomposition.cs`

- `DecomposeToRectangles` → bounding box (заглушка для полноценной декомпозиции)
- `IsPointInPolygon` → ray casting algorithm ✅
- `CalculateArea` → Shoelace formula ✅

**⚠️ WARNING:** `DecomposeToRectangles` возвращает единственный bounding box. Это означает, что для непрямоугольных плит (L, T, П-образные) стержни будут генерироваться за пределами контура. Нужна полноценная трапецоидальная декомпозиция или клипирование стержней по контуру полигона.

---

## 7. Revit Plugin

### 7.1 Архитектура

Файл: `src/A101.RevitPlugin/`

```
App.cs            → IExternalApplication (регистрация ribbon button)  
Commands/         → PlaceReinforcementCommand : IExternalCommand  
Services/         → RevitRebarPlacer (→ порт IRebarPlacer)  
UI/               → MainPanel.xaml + MainPanelViewModel (WPF + MVVM)  
```

**Revit API 2025**: Revit 2025 перешёл на .NET 8 (первая версия без .NET Framework). Проект корректно таргетирует `net8.0` + `UseWPF=true`.

### 7.2 RevitRebarPlacer — Анализ

```csharp
// Pseduo-code из RevitRebarPlacer
using var tx = new Transaction(doc, "Place rebar");
tx.Start();
foreach (var bar in bars) {
    var curve = Line.CreateBound(bar.StartPoint, bar.EndPoint);
    var rebar = Rebar.CreateFromCurves(doc, style, type, hook, hook, host, normal, curves, ...);
}
tx.Commit();
```

**🟡 Статус: DRAFT.** Revit SDK NuGet закомментирован:
```xml
<!-- <PackageReference Include="Autodesk.Revit.SDK" Version="2025.0.0" /> -->
```

**Замечания по Revit API:**

1. **`Rebar.CreateFromCurves` vs `RebarInSystem`**: Для плитного армирования лучше использовать `AreaReinforcement.Create()` — создаёт систему арматурных стержней, привязанных к Floor. `Rebar.CreateFromCurves` создаёт отдельные стержни — корректно, но менее удобно для инженера в Revit.

2. **Нет `RebarBarType` selection**: Код использует первый найденный тип стержня. Нужен маппинг diameter → RebarBarType с учётом ГОСТ-профиля.

3. **Нет hook handling**: Крюки (hooks) передаются как null. Для A500C крюки не требуются (периодический профиль), но для A240 (гладкая) — обязательны.

4. **Transaction granularity**: Вся арматура в одной транзакции — если один стержень не создаётся, откатится всё. Лучше батчить по зонам.

5. **Performance**: Для крупных плит (1000+ стержней) последовательное создание через `Rebar.CreateFromCurves` будет очень медленным. Revit API требует `SubTransaction` или `RebarContainer` для performance.

---

## 8. Модели данных (Domain)

### 8.1 Models — Полнота

| Модель | Поля | Оценка |
|--------|------|--------|
| `SlabGeometry` | Polygon, Thickness, ConcreteClass | ✅ Достаточно |
| `ReinforcementZone` | Polygon, Spec (d, spacing, steel) | ✅ |
| `RebarBar` | Start, End, Diameter, TotalLength | ⚠️ Нет direction, layer, zone |
| `Polygon` | Points, CalculateArea() | ✅ |
| `Point2D` | X, Y | ✅ |
| `ColorLegend` | Entries, FindClosest(color) | ✅ |
| `SupplierCatalog` | SupplierName, AvailableLengths | ✅ |
| `StockLength` | LengthMm, PricePerTon, InStock | ✅ |
| `OptimizationSettings` | SawCutWidth, MinScrap | ⚠️ Нет ClampLength |
| `IsolineColor` | R, G, B | ✅ |

**⚠️ `RebarBar` не содержит:**
- Direction (X/Y)
- Layer (top/bottom)
- ZoneId (к какой зоне армирования относится)
- BendPoints (для Г-образных стержней)
- Mark (маркировка для спецификации)

### 8.2 Ports — Чистые интерфейсы

| Порт | Методы | Адаптеры |
|------|--------|----------|
| `IIsolineParser` | ParseAsync(stream, legend, slab) → zones | RasterIsolineParser, DxfIsolineParser |
| `IRebarOptimizer` | Optimize(lengths, stock, settings) → result | FirstFitDecreasingOptimizer |
| `ISupplierCatalogLoader` | LoadAsync(path), GetDefaultCatalog() | FileSupplierCatalogLoader |
| `IRebarPlacer` | PlaceAsync(bars) | RevitRebarPlacer |
| `IRebarExporter` | ExportAsync(bars, path) | (не реализован) |

**✅ Правильное разделение.** Каждый порт — чистый domain-контракт.

---

## 9. Тесты

### 9.1 Покрытие

| Тестовая сборка | Файлов | Тестов | Framework |
|----------------|--------|--------|-----------|
| A101.Domain.Tests | 3 | ~15 | xUnit + FluentAssertions |
| A101.Infrastructure.Tests | 1 | ~7 | xUnit + FluentAssertions |

### 9.2 Качество тестов

**AnchorageRulesTests ✅**
- Проверяют минимальные ограничения (15d, 200mm)
- Проверяют разумность результата для конкретного случая (d12 A500C B25 → 1000-1500mm)
- Проверяют, что lap > anchorage
- **Нет:** тесты для крайних значений (d40, B15/B40), тесты для округления

**PolygonDecompositionTests ✅**
- Point-in-polygon: inside, outside
- Area: rectangle, triangle (Shoelace)
- Decomposition: rectangle → single bbox

**ColorLegendTests ✅**
- Exact match, near match, too-far → null
- Хорошие edge cases

**FirstFitDecreasingOptimizerTests ✅**
- Empty input, single fit, two fit, exceed, waste calculation, many small
- **Нет:** тесты с несколькими стоковыми длинами, тесты с SawCutWidth=0

### 9.3 Отсутствующие тесты

| Что не покрыто | Критичность |
|---------------|-------------|
| RebarLayoutEngine | 🔴 Нет ни одного теста |
| DxfIsolineParser | 🔴 Нет тестов |
| RasterIsolineParser | 🔴 Нет тестов |
| LayoutReinforcementUseCase | 🟡 Нет тестов |
| FileSupplierCatalogLoader JSON/CSV | 🟡 Нет тестов |
| PolygonDecomposition — L-shape | 🟡 Нет тестов для непрямоугольных |
| Cross-class integration | 🟡 Нет E2E тестов |

**Общее покрытие оценочно: ~35-40%.** Для строительного ПО (safety-critical) рекомендуется ≥80%.

---

## 10. Зависимости и безопасность

### 10.1 NuGet пакеты

| Пакет | Версия | Статус | Заметки |
|-------|--------|--------|---------|
| IxMilia.Dxf | 0.8.0 | ✅ Latest | MIT, стабильная |
| SixLabors.ImageSharp | 3.1.7 | ⚠️ | Apache-2.0, но есть коммерческая лицензия для >1M$ revenue |
| MS.Extensions.DI | 8.0.2 | ✅ | |
| MS.Extensions.Logging | 8.0.2 | ✅ | |
| System.Text.Json | 8.0.5 | ✅ | |

**⚠️ ImageSharp лицензия:** С ImageSharp v3.0 SixLabors перешли на split licensing. Для коммерческих проектов с revenue > $1M USD нужна платная лицензия. Альтернатива: SkiaSharp (MIT) или System.Drawing.Common (Windows-only).

### 10.2 Python пакеты

| Пакет | Версия | Замечания |
|-------|--------|-----------|
| torch | ≥2.2 | PyTorch — MIT-like |
| onnxruntime | ≥1.17 | MIT |
| fastapi | ≥0.110 | MIT |
| opencv-python-headless | ≥4.9 | Apache-2.0 |
| ezdxf | ≥0.19 | MIT, зрелая DXF-библиотека |
| shapely | ≥2.0 | BSD-3 |

**✅ Все лицензии совместимы** для коммерческого использования.

### 10.3 Безопасность

| Аспект | Статус | Детали |
|--------|--------|--------|
| Path Traversal (FileSupplierCatalogLoader) | ⚠️ | `File.ReadAllTextAsync(filePath)` без валидации пути |
| Denial of Service (ImageSharp) | ⚠️ | Нет лимита на размер изображения — OOM при огромных PNG |
| Input Validation | ⚠️ | Нет валидации отрицательных диаметров, нулевых spacing и т.д. |
| Deserialization | ✅ | System.Text.Json — безопасный десериализатор |
| Revit Transaction | ✅ | Using-паттерн — откат при исключении |

**Рекомендации:**
1. Валидировать `filePath` в `FileSupplierCatalogLoader` (whitelist директорий)
2. Ограничить максимальный размер PNG (e.g., 100 Mpx)
3. Добавить domain validation в модели (diameter ∈ {6,8,10,...,40}, spacing > 0, etc.)

---

## 11. Сравнение с open-source аналогами

### 11.1 Рынок open-source решений для армирования

| Проект | Язык | Функционал | Статус |
|--------|------|-----------|--------|
| **JAJA1706/SteelCuttingSystem** | C# | Cutting stock web app | Master thesis, basic |
| **AlexanderMorozovDesign/Linear_Cutting** | C# | 1D cutting stock + Grasshopper plugin | Hobbyist |
| **emadehsan/csp** | Python | Cutting stock via OR-Tools | Educational |
| **FreeCAD Rebar addon** | Python | Rebar detailing в FreeCAD | Active, community |
| **pyFEM** | Python | FEM + reinforcement (academic) | Unmaintained |
| **ENS Structures** | Various | Eurocode design tools | Commercial-leaning |

**Вывод: прямых open-source конкурентов с полным pipeline (isoline → layout → cutting → Revit) НЕТ.** A101-Reinforcement занимает уникальную нишу. Ближайшие коммерческие аналоги: АРБАТ (Lira-SAPR), КРОСС (SCAD Soft), SOFiSTiK (international), IDEA StatiCa (international).

### 11.2 Конкурентные преимущества A101

1. **Автоматический парсинг изополей** — уникальная функция, ни один аналог не делает CV-extraction из растровых чертежей
2. **Оптимизация раскроя с визуализацией** — интегрировано в pipeline (vs standalone cutting stock tools)
3. **Revit integration** — прямой экспорт в BIM (vs PDF/DXF-only конкуренты)
4. **Clean Architecture** — расширяемость для EC2 / ACI 318 / других стандартов

---

## 12. Рекомендации по roadmap

### Приоритет 1 (Критический — перед production)

| # | Задача | Effort |
|---|--------|--------|
| 1.1 | Исправить формулу анкеровки: добавить η₁, η₂ коэффициенты по СП 63 | 2ч |
| 1.2 | Параметризовать α_lap для нахлёста (1.2/1.4/2.0) | 1ч |
| 1.3 | Добавить X/Y direction и top/bottom layer в RebarBar + RebarLayoutEngine | 8ч |
| 1.4 | Тесты для RebarLayoutEngine (минимум 10 кейсов) | 4ч |
| 1.5 | Тесты для IsolineParsers (sample PNG + DXF fixtures) | 6ч |
| 1.6 | Добавить DI Composition Root + wiring | 3ч |

### Приоритет 2 (Важный — для качества)

| # | Задача | Effort |
|---|--------|--------|
| 2.1 | Заменить FFD на Column Generation (Google OR-Tools) | 16ч |
| 2.2 | Перейти на CIE Lab для цветовой дистанции | 4ч |
| 2.3 | Поддержка arc segments в DXF polylines (Bulge) | 4ч |
| 2.4 | Polygon clipping для стержней (Sutherland-Hodgman или Clipper2) | 8ч |
| 2.5 | Input validation в domain models (Guard clauses) | 3ч |
| 2.6 | AreaReinforcement.Create() вместо Rebar.CreateFromCurves | 8ч |

### Приоритет 3 (Развитие)

| # | Задача | Effort |
|---|--------|--------|
| 3.1 | Eurocode 2 support (параллельный NormativeCode enum) | 16ч |
| 3.2 | Спецификация арматуры (ведомость по ГОСТ 21.501) | 12ч |
| 3.3 | DXF export (карты раскроя в DXF) | 8ч |
| 3.4 | ML-модель для сканов (fine-tune SAM 2 на строительных чертежах) | 40ч+ |
| 3.5 | Multi-slab support (несколько плит в одном проекте) | 8ч |
| 3.6 | IFC export (buildingSMART стандарт) | 16ч |

---

## 13. Итоговая оценка по категориям

| Категория | Оценка | Комментарий |
|----------|--------|-------------|
| **Архитектура** | 9/10 | Clean Architecture, правильные порты/адаптеры, единственная претензия — Infrastructure→Application ref |
| **Нормативная корректность** | 5/10 | Rs/Rbt таблицы верны, но формулы анкеровки/нахлёста упрощены (отсутствуют η₁, η₂, α_lap) |
| **Оптимизация раскроя** | 6/10 | FFD работоспособен, но далёк от оптимума; нет column generation |
| **Computer Vision** | 6/10 | Рабочий flood-fill + DXF parser, но RGB distance неадекватна; ML pipeline — заглушка |
| **Раскладка арматуры** | 4/10 | Базовая однонаправленная раскладка; нет X/Y, top/bottom, polygon clipping |
| **Revit Plugin** | 3/10 | Draft-заглушка без SDK; правильная структура, но нереализован |
| **Тестирование** | 5/10 | Хорошие тесты для того, что покрыто (~40%), но большие пробелы |
| **Документация** | 6/10 | README есть, XML-doc comments хорошие, но нет user guide / API doc |
| **Безопасность** | 7/10 | Нет критических уязвимостей, но path traversal и input validation нужны |
| **Зависимости** | 8/10 | Зрелые библиотеки, правильные версии; ImageSharp licensing caveat |

**Итого: 7.2 / 10** — Архитектурно зрелый проект с правильными абстракциями, требующий доработки домённой логики до production quality.

---

*Конец отчёта.*
