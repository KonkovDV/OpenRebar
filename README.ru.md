# OpenRebar

[![CI](https://github.com/KonkovDV/OpenRebar/actions/workflows/ci.yml/badge.svg)](https://github.com/KonkovDV/OpenRebar/actions/workflows/ci.yml)
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/KonkovDV/OpenRebar/badge)](https://scorecard.dev/viewer/?uri=github.com/KonkovDV/OpenRebar)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple)](https://dotnet.microsoft.com/)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Русский | [English](README.md)

OpenRebar раскладывает арматуру плоской железобетонной плиты по карте требуемой площади. Карта — это чертёж изолиний DXF, калиброванный PNG или числовое поле. Прогон пишет стержни, анкеровку, нахлёсты, ведомость, раскрой и файл IFC.

Три места в цепочке.

| Место | Роль |
|---|---|
| Расчётная программа | Пишет требуемую площадь |
| **OpenRebar** | Раскладывает стержни и проверяет эту площадь |
| [AeroBIM](https://github.com/KonkovDV/AeroBIM) | Читает комплект документации и сам ставит флаг комплекта |

Остальные репозитории того же аккаунта GitHub — другие продукты.

Один и тот же вход даёт один и тот же отчёт.

Библиотеки собираются под .NET 8 и .NET 10. CLI — на .NET 10. Надстройка Revit — две сборки по рантайму процесса: `RevitNet8`, пока Revit на .NET 8, и `RevitNet10` на .NET 10. Правила проверяются без Revit.

## Состояние

Регрессия по пяти командам из раздела [Быстрый старт](#быстрый-старт): **467/467 тестов проходят**.

| | |
|---|---|
| Отчёт | `schemaVersion` 2.1.0, контракт `OpenRebar.reinforcement.report.v2`. Неизвестные поля отвергаются |
| Нормы | СП 63.13330.2018, профиль `ru.sp63.2018`, таблицы `ru.sp63.2018.tables.v3` |
| Проверка площади | Сетка ячеек. `Passed` только при нулевом дефиците. Жёсткая коллизия записывается рядом с этой проверкой |
| `checks` | `providedArea` повторяет проверку площади. Трещины и прогиб остаются `NotEvaluated`, если на входе только требуемая площадь |
| Раскрой | Генерация столбцов (HiGHS). `wastePercent` и `estimatedCost` (null, если у использованной складской длины нет цены) |
| IFC | IFC4. Один тип на диаметр и класс стали, диск радиуса d/2, размещение на оси |
| Revit | Прямые стержни. Крюк, Г или П — ошибка |

Пример `examples/dxf/simple-slab` завершается с кодом 2. Края свободные, требуемая площадь доходит до края, крюк не засчитывается. Это регрессия.

Примеры — фикстуры для тестов. Пилот в проектном бюро и сертификат у этой страницы ещё впереди. Если эта страница расходится с тестами, верны тесты. Публичная цифра следует правилу [docs/VALIDATION_BASELINE.md](docs/VALIDATION_BASELINE.md).

Где литература 2026 года впереди этого репозитория, записано в [docs/SOTA_2026.md](docs/SOTA_2026.md).

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

На simple-slab, Ø20 и защитный слой 30 мм, `effectiveDepthMm` и `effectiveDepthByLayer.BottomX` оба равны 180. Это `220 − 30 − 20/2`: толщина, минус защитный слой, минус половина диаметра. Слой без стержней в карту не попадает. `estimatedCost` равен null, потому что в каталоге примера нет цены.

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
| `IImageSegmentationService` | Необязательный локальный HTTP-мост. Внешней модели нет |

Раскрой в прогоне — генерация столбцов. Она записывает, когда двойственная граница имеет статус `Proven`. Рядом стоит `BestFitDecreasingOptimizer`: стержень кладётся в открытый хлыст с наименьшим остатком. Стек диагностики хранится как `stackTraceSha256`. Текст остаётся в журнале.

## Пределы

Один чертёж — один слой. Файл проекта может назвать до четырёх чертежей.

Край `Supported` или `Continuous` — сторона описанного прямоугольника. На любом другом контуре прогон останавливается с `edgesRequireRectangularSlab` и кодом 3. Свободные края косой плиты остаются на контуре. Косой стержень шагается наружу на 10 мм и затем обрезается, так что конец остаётся внутри контура.

Поздний кусок нахлёста рисуется на один диаметр в стороне от проектной оси: рядом, если `laps.representation` равен `inPlane`, и выше, если `stacked`. Профиль без этого поля оставляет `inPlane`. Анкеровка останавливается на границе рабочей области. Крюк — дуга по оси. Прямой хвост остаётся 0, пока профиль не задаст `hooks.tailDiameters`. Муфты не моделируются.

Ведомость — CSV. IFC здесь — IFC4: тип стержня, тело-диск, размещение. Нахлёст IFC 4.3 и прогон файла через другую программу ещё впереди. Revit принимает прямой стержень. Крюк, Г и П отклоняются. Записью остаются ведомость и IFC.

PNG требует `--px-per-mm` и начало координат. Кадр больше 25 миллионов пикселей отвергается по заголовку, до декодирования.

На simple-slab цифры площади: `edgeDevelopmentAreaM2` 6,12, `realDeficitAreaM2` 0,99, `underReinforcedAreaM2` 7,11.

## Примеры

- `examples/dxf/simple-slab/` — свободные края, проверка площади не проходит, код 2
- `examples/png/simple-slab/` — та же плита из калиброванного PNG
- `examples/fe-field/supported-slab.csv` и `supported-slab.project.json` — плита на стенах, проверка площади проходит

Эталоны пересобираются скриптами `tools/examples/generate_expected_outputs.ps1` и `.sh`. Каждая изменённая цифра объясняется в запросе на слияние.

Корпус пакетов не входит в прогон push. Без `OPENREBAR_BATCH_CORPUS_ROOT` корпус пишет `corpus: NotEvaluated`.

## Необязательная ветка ML

```bash
cd ml
python -m pip install --require-hashes -r requirements.locked.txt
python -m pytest tests -q
python scripts/validate_model_manifest.py models/MANIFEST.json
```

CI ставит этот lock с хэшами на Python 3.11. Манифест пуст: контрольной точки нет, и вызова внешней модели нет. Без контрольной точки PNG читается точным совпадением цвета.

## Revit

Скопируйте `addin/RevitNet8` рядом со сборкой `net8.0-windows` или `addin/RevitNet10` рядом со сборкой `net10.0-windows`. Ось стержня сидит на cover + d/2. Внутреннее направление грани лежит на внешнем. Верхний защитный слой читается из `CLEAR_COVER_TOP`. См. [RELEASE_POLICY.md](RELEASE_POLICY.md).

## Цепочка поставки

Восстановление NuGet идёт по lock-файлу. Ветка ML ставит `ml/requirements.locked.txt` с хэшами. Scorecard запускается на `main`. Релиз собирает SBOM, подписывает провенанс опубликованных файлов и отдельно связывает этот SBOM с архивом CLI. ImageSharp остаётся на 3.1.12: версия 4 требует лицензионный ключ, а у пяти предупреждений на линии 3.1.12 нет патча. Любая другая находка высокой серьёзности роняет аудит.

## Документы

- [docs/README.md](docs/README.md) — карта
- [docs/architecture.md](docs/architecture.md)
- [docs/NORMATIVE_TRACEABILITY.md](docs/NORMATIVE_TRACEABILITY.md) — пункт, код, тест
- [docs/VALIDATION_BASELINE.md](docs/VALIDATION_BASELINE.md)
- [docs/SOTA_2026.md](docs/SOTA_2026.md)
- [docs/OPENREBAR_AGENT_PLAN_r10_2026_10_14.md](docs/OPENREBAR_AGENT_PLAN_r10_2026_10_14.md) — очередь работ
- [CHANGELOG.md](CHANGELOG.md), [CONTRIBUTING.md](CONTRIBUTING.md), [SECURITY.md](SECURITY.md), [CITATION.cff](CITATION.cff)

Аудиты апреля 2026 в `docs/archive/` — история.

## Лицензия

MIT. См. [LICENSE](LICENSE).
