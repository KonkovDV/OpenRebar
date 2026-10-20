<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/brand/openrebar-logo-dark.png">
    <img src="docs/brand/openrebar-logo.png" alt="OpenRebar" width="640">
  </picture>
</p>

# OpenRebar

<p align="center">
  <a href="https://github.com/KonkovDV/OpenRebar/actions/workflows/ci.yml"><img src="https://github.com/KonkovDV/OpenRebar/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <a href="https://scorecard.dev/viewer/?uri=github.com/KonkovDV/OpenRebar"><img src="https://api.scorecard.dev/projects/github.com/KonkovDV/OpenRebar/badge" alt="OpenSSF Scorecard"></a>
  <img src="https://img.shields.io/badge/.NET-8.0-purple" alt=".NET 8">
  <img src="https://img.shields.io/badge/.NET-10.0-purple" alt=".NET 10">
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-blue.svg" alt="License: MIT"></a>
</p>

Русский | [English](README.md)

OpenRebar кладёт арматуру плоской плиты на карту, которую уже нарисовал расчёт. На входе изолинии DXF, калиброванный PNG или поле требуемой площади. На выходе стержни, анкеровка, нахлёсты, ведомость, раскрой и файл IFC.

Один и тот же чертёж. Один и тот же отчёт.

Три места в цепочке.

| Место | Роль |
|---|---|
| Расчётная программа | Пишет требуемую площадь |
| **OpenRebar** | Раскладывает стержни и проверяет эту площадь |
| [AeroBIM](https://github.com/KonkovDV/AeroBIM) | Читает комплект документации и сам ставит флаг комплекта |

Остальные репозитории того же аккаунта — своя работа.

Библиотеки собираются под .NET 8 и .NET 10. CLI — на .NET 10. Надстройка Revit — две сборки по рантайму процесса: `RevitNet8`, пока Revit на .NET 8, и `RevitNet10` на .NET 10.

## Содержание

- [Как выглядит прогон](#как-выглядит-прогон)
- [Состояние](#состояние)
- [Быстрый старт](#быстрый-старт)
- [Что получается](#что-получается)
- [Как устроено](#как-устроено)
- [Как ведёт себя прогон](#как-ведёт-себя-прогон)
- [Примеры](#примеры)
- [Необязательная ветка ML](#необязательная-ветка-ml)
- [Revit](#revit)
- [Цепочка поставки](#цепочка-поставки)
- [Документы](#документы)
- [Лицензия](#лицензия)

## Как выглядит прогон

Ø20 с шагом 150 мм, защитный слой 30 мм, плита толщиной 220 мм. Рабочая высота 180 мм: `220 − 30 − 20 / 2`.

<p align="center">
  <img src="docs/images/simple-slab-plan.svg" alt="План домашней плиты: 26 стержней на 6000 на 4000 мм, бледные полосы у обоих свободных концов и оранжевая кромка" width="760">
</p>

Это домашняя плита `examples/dxf/simple-slab`, нарисованная по её собственным IFC и файлам проверки. 26 стержней идут через пролёт 6000 мм. Прямой участок кончается в 130 мм от каждого свободного края, и венец крюка садится на линию защитного слоя, в 40 мм от грани. Последний ряд стоит на 3885 мм, в 170 мм от предыдущего. Бледные полосы — ячейки в зоне анкеровки у свободного конца: `edgeDevelopmentAreaM2` 5,46. Оранжевая кромка — ячейки, до которых стержень не доходит: `realDeficitAreaM2` 2,55. Вместе `underReinforcedAreaM2` 8,01. Прогон выходит с кодом 2 и оставляет все файлы на столе.

У каждого стержня есть `hookOutsideConcrete`. Полуокружность поднимается на 180 мм от оси на отметке 40 мм, и поверхность стержня выходит из верхней грани на 10 мм. Выход вертикальный, поэтому план показывает стержни и ячейки.

```mermaid
flowchart LR
  M["DXF, PNG или поле площади"] --> B["Стержни, анкеровка, нахлёсты"]
  B --> C["Проверка площади по ячейкам"]
  C --> O["Ведомость, раскрой, IFC"]
```

## Состояние

Пять команд из раздела [Быстрый старт](#быстрый-старт): **473/473 тестов проходят**.

| | |
|---|---|
| Отчёт | `schemaVersion` 2.1.0, контракт `OpenRebar.reinforcement.report.v2`. Неизвестные поля отвергаются |
| Нормы | СП 63.13330.2018, профиль `ru.sp63.2018`, таблицы `ru.sp63.2018.tables.v3` |
| Проверка площади | Сетка ячеек. `Passed` только при нулевом дефиците. Жёсткая коллизия записывается рядом с этой проверкой |
| `checks` | `providedArea` повторяет проверку площади. Трещины и прогиб остаются `NotEvaluated`, если на входе только требуемая площадь |
| Раскрой | Генерация столбцов (HiGHS). `wastePercent` и `estimatedCost` (null, если у использованной складской длины нет цены) |
| IFC | IFC4. Один тип на диаметр и класс стали, диск радиуса d/2, размещение на оси |
| Revit | Прямые стержни в открытом документе |

Цифры этой страницы держат тесты. Правило — [docs/VALIDATION_BASELINE.md](docs/VALIDATION_BASELINE.md).

## Быстрый старт

Нужны SDK .NET 8 и 10 и Python 3.11 для скриптов CI и необязательной ML-ветки.

```bash
dotnet restore OpenRebar.sln --locked-mode -p:EnableWindowsTargeting=true -p:Platform=x64
dotnet build OpenRebar.sln --no-restore --configuration Release -p:EnableWindowsTargeting=true -p:Platform=x64
dotnet format OpenRebar.sln --verify-no-changes --no-restore
dotnet test tests/OpenRebar.Domain.Tests/OpenRebar.Domain.Tests.csproj --no-build --configuration Release -f net8.0
dotnet test tests/OpenRebar.Infrastructure.Tests/OpenRebar.Infrastructure.Tests.csproj --no-build --configuration Release -f net8.0
dotnet test tests/OpenRebar.TestCorpus/OpenRebar.TestCorpus.csproj --no-build --configuration Release -f net8.0
dotnet test tests/OpenRebar.Application.Tests/OpenRebar.Application.Tests.csproj --no-build --configuration Release -f net8.0
dotnet test tests/OpenRebar.Cli.Tests/OpenRebar.Cli.Tests.csproj --no-build --configuration Release -f net10.0
python tools/ci/verify_readme_regression_claim.py
```

Одна плита:

```bash
dotnet run --project src/OpenRebar.Cli --configuration Release -- examples/dxf/simple-slab/input.dxf --thickness 220 --cover 30 --slab-width 6000 --slab-height 4000 --layer BottomX
```

Код 2: проверка площади не прошла, файлы записаны. Код 1: ошибка входа или ввода-вывода. Код 3: критическая ошибка детализации, например стержень, который нельзя купить или состыковать, или опёртый край на контуре, который не является прямоугольником (`edgesRequireRectangularSlab`).

Флаги: `--legend`, `--catalog`, `--ml-url`, `--slab-width`, `--slab-height`, `--thickness`, `--cover`. `--ml-url` говорит с указанным хостом. Loopback разрешён сам по себе. Редирект на другой хост отклоняется.

## Что получается

| Файл | Роль |
|---|---|
| `*.result.json` | Отчёт |
| `*.schedule.csv` | Позиции и масса |
| `*.aerobim.json` | Короткая сводка для внешней проверки |
| `*.reinforcement.ifc` | Стержни IFC4 |
| `*.verification.png` | Пиксель на ячейку: зелёный держит, красный в недоборе |
| `*.verification.json` | Ячейки в недоборе, с запасом и статусом |

На simple-slab, Ø20 и защитный слой 30 мм, `effectiveDepthMm` и `effectiveDepthByLayer.BottomX` оба равны 180. Это `220 − 30 − 20 / 2`: толщина, минус защитный слой, минус половина диаметра. Слой без стержней в карту не входит.

Отчёт хранит профиль, версию таблиц и оптимизатор. `checks[].status` — это `Passed`, `Failed` или `NotEvaluated`.

## Как устроено

```
Domain ← Application ← Infrastructure ← CLI и плагин Revit
```

В Domain нет пакетов NuGet. NetTopologySuite, HiGHS, xBIM и ImageSharp остаются в Infrastructure за портами `src/OpenRebar.Domain/Ports/`. Revit API подключается только в `src/OpenRebar.RevitPlugin/` под `#if REVIT_SDK`.

| Порт | Роль |
|---|---|
| `IIsolineParser` | Зоны из DXF и PNG |
| `IReinforcementCalculator` | Стержни зоны |
| `IRebarOptimizer` | Одномерный раскрой |
| `IIfcExporter` | IFC4 |
| `IRevitPlacer` | Прямые стержни в открытом документе |
| `IImageSegmentationService` | Необязательный локальный HTTP-мост |
| `IStructuredLogger` | Журнал прогона; по умолчанию консоль |

Раскрой в прогоне — генерация столбцов. Она записывает, когда двойственная граница имеет статус `Proven`. Рядом стоит `BestFitDecreasingOptimizer`: стержень кладётся в открытый хлыст с наименьшим остатком. С флагом `--include-diagnostics` стек исключения уходит в журнал, а его SHA-256 — в отчёт как `stackTraceSha256`.

## Как ведёт себя прогон

Один чертёж — один слой. Файл проекта может назвать четыре.

Край `Supported` или `Continuous` — сторона прямоугольника. Любой другой контур останавливается с `edgesRequireRectangularSlab` и кодом 3, до первого стержня. Свободные края косой плиты остаются на живом контуре. Косой стержень шагается наружу на 10 мм и обрезается так, что конец сидит внутри контура.

Поздний кусок нахлёста садится на один диаметр в стороне от проектной оси: рядом, если `laps.representation` равен `inPlane`, и выше, если `stacked`. Профиль без этого поля оставляет `inPlane`. Анкеровка кончается на границе рабочей области. Крюк — дуга вдоль оси. Прямой хвост остаётся 0, пока профиль не задаст `hooks.tailDiameters`.

Ведомость — CSV. IFC4 несёт тип стержня, тело-диск и размещение на оси. Revit берёт прямой стержень, а крюк, Г и П возвращает ошибкой. Запись — ведомость и IFC.

PNG приносит `--px-per-mm` и начало координат. Кадр больше 25 миллионов пикселей останавливают по заголовку, до первого пикселя.

На simple-slab цифры площади: `edgeDevelopmentAreaM2` 5,46, `realDeficitAreaM2` 2,55, `underReinforcedAreaM2` 8,01.

## Примеры

- `examples/dxf/simple-slab/` — свободные края, код 2, файлы на месте
- `examples/png/simple-slab/` — та же плита из калиброванного PNG
- `examples/fe-field/supported-slab.csv` и `supported-slab.project.json` — плита на стенах, проверка площади проходит

Эталоны пересобираются скриптами `tools/examples/generate_expected_outputs.ps1` и `.sh`. Изменённая цифра объясняется в запросе на слияние.

План любого прогона рисует `python tools/examples/render_plan.py <папка>/input out.svg`. Скрипт читает IFC, ячейки проверки и отчёт, которые прогон записал в эту папку.

## Необязательная ветка ML

```bash
cd ml
python -m pip install --require-hashes -r requirements.locked.txt
python -m pytest tests -q
python scripts/validate_model_manifest.py models/MANIFEST.json
```

CI ставит этот lock с хэшами на Python 3.11. PNG читается точным цветом легенды. `--ml-url` ждёт, когда свой сервис уже стоит у вас.

## Revit

Скопируйте `addin/RevitNet8` рядом со сборкой `net8.0-windows` или `addin/RevitNet10` рядом со сборкой `net10.0-windows`. Ось стержня сидит на cover + d/2. Внутреннее направление грани лежит на внешнем. Верхний защитный слой читается из `CLEAR_COVER_TOP`. См. [RELEASE_POLICY.md](RELEASE_POLICY.md).

## Цепочка поставки

Восстановление NuGet идёт по lock-файлу. Ветка ML ставит `ml/requirements.locked.txt` с хэшами. Scorecard запускается на `main`. Релиз собирает SBOM и связывает его с архивом CLI.

## Документы

- [docs/README.md](docs/README.md) — карта
- [docs/architecture.md](docs/architecture.md)
- [docs/NORMATIVE_TRACEABILITY.md](docs/NORMATIVE_TRACEABILITY.md) — пункт, код, тест
- [docs/VALIDATION_BASELINE.md](docs/VALIDATION_BASELINE.md)
- [docs/SOTA_2026.md](docs/SOTA_2026.md)
- [docs/AUDIT_2026-10-18.md](docs/AUDIT_2026-10-18.md)
- [docs/AUDIT_2026-10-20.md](docs/AUDIT_2026-10-20.md)
- [docs/brand/openrebar-brand-sheet.jpg](docs/brand/openrebar-brand-sheet.jpg)
- [docs/OPENREBAR_AGENT_PLAN_r10_2026_10_14.md](docs/OPENREBAR_AGENT_PLAN_r10_2026_10_14.md)
- [CHANGELOG.md](CHANGELOG.md), [CONTRIBUTING.md](CONTRIBUTING.md), [SECURITY.md](SECURITY.md), [CITATION.cff](CITATION.cff)

## Лицензия

MIT. См. [LICENSE](LICENSE).
