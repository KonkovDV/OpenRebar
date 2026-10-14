# OpenRebar

[![CI](https://github.com/KonkovDV/OpenRebar/actions/workflows/ci.yml/badge.svg)](https://github.com/KonkovDV/OpenRebar/actions/workflows/ci.yml)
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/KonkovDV/OpenRebar/badge)](https://scorecard.dev/viewer/?uri=github.com/KonkovDV/OpenRebar)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple)](https://dotnet.microsoft.com/)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Русский | [English](README.md)

OpenRebar превращает карту требуемой площади арматуры плоской железобетонной плиты в стержни, анкеровку, нахлёсты, ведомость, раскрой и файл IFC. Карта приходит как изолинии DXF, калиброванный PNG или числовое поле. Ядро детерминированное. Языковая модель не раскладывает стержни и не ставит отметку «принято».

Библиотеки собираются под .NET 8 и .NET 10. CLI — на .NET 10. Надстройка Revit — две сборки по рантайму процесса: `RevitNet8`, пока Revit на .NET 8, и `RevitNet10` на .NET 10. Правила проверяются без Revit.

**Чего здесь нет.** Это не конечно-элементный решатель, не обученный сегментатор и не расчёт ширины трещин или прогиба. Гнутые стержни в Revit не ставятся. Экономия стали не заявляется. Каталог `ml/` поставляется с пустым манифестом модели и остаётся офлайн.

## Состояние

Регрессия по пяти командам из раздела [Сборка и тесты](#сборка-и-тесты): **453/453 тестов проходят**.

| Поверхность | Сейчас |
|---|---|
| Отчёт | `schemaVersion` 2.1.0, контракт `OpenRebar.reinforcement.report.v2` |
| Нормы | СП 63.13330.2018, профиль `ru.sp63.2018`, таблицы `ru.sp63.2018.tables.v3` |
| Проверка площади | Сетка ячеек. `Passed` только при нулевом дефиците. Жёсткая коллизия эту проверку не заменяет |
| `checks` | `providedArea` повторяет проверку площади. Трещины и прогиб остаются `NotEvaluated` |
| Раскрой | Генерация столбцов (HiGHS) с записанным provenance. Процент отходов и `estimatedCost` (null, если в каталоге нет цены) |
| IFC | IFC4. Один тип на диаметр и класс стали, диск радиуса d/2, размещение на оси. CI проверяет эти имена сущностей |
| Revit | Только прямые стержни. Крюк, Г или П — ошибка, не прямая линия |

Пример `examples/dxf/simple-slab` завершается с кодом 2. Края свободные, требуемая площадь доходит до края, крюк не засчитывается. Это регрессия, а не ошибка эталона.

## Место среди соседних репозиториев

[AeroBIM](https://github.com/KonkovDV/AeroBIM) сверяет комплект документации (модель, лист, ведомость, текст) и сам выставляет флаг комплекта. OpenRebar проектирует армирование плиты и пишет отчёт и IFC, которые такая сверка может прочитать. Комплект он не проверяет.

Остальные репозитории того же автора (движки расписаний, маркетплейсы, исследовательские заметки) — отдельные продукты.

## Конвейер

1. Читается файл изолиний. Сущность DXF, которая не стала зоной, учитывается, а не пропадает. PNG измеряется по заголовку до декодирования.
2. Зоны классифицируются, покрытие разбиения записывается.
3. Ставятся стержни. Шаг на грани меряется по суммарному слою. Длины анкеровки и нахлёста берутся из версионированных таблиц. Зачёт крюка равен 0. Хвост крюка по умолчанию равен 0.
4. Режется склад. Стержень, который нельзя купить или состыковать, — критическая ошибка.
5. Уложенная площадь сверяется с требуемой, пишется `checks`.
6. Пишутся `*.result.json`, `*.schedule.csv`, `*.aerobim.json` и `*.reinforcement.ifc`.

## SOTA 2026 как разрыв, не как обещание

Сравнение — в [docs/SOTA_2026.md](docs/SOTA_2026.md). Коротко:

| Тема | Есть в репозитории | Нет в репозитории |
|---|---|---|
| Сертификат площади | Запас по ячейке и полигон дефицита | Утверждение, что раскладка оптимальна |
| Провенанс | Идентификатор профиля, версия таблиц, идентификатор оптимизатора, источник параметра | Норма, набранная по памяти |
| Раскрой | Генерация столбцов, записанные отходы и стоимость закупки | Измеренная экономия в процентах или деньгах |
| IFC | IFC4: тип стержня, тело-диск, размещение, имена сущностей IDS в CI | Кодирование нахлёста IFC 4.3, круговой обмен с просмотрщиком |
| Сегментация | Точное совпадение RGB. У пути U-Net нет контрольной точки | SAM2, DINOv2, опубликованный mIoU |
| Поиск раскладки | Правила зон и проверка шага | GNN, обучение с подкреплением, физически-информируемая сеть |
| Вторая группа предельных состояний | `NotEvaluated` для трещин и прогиба | PINN или нормативная проверка этих величин |

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

Код 2 значит, что проверка площади не прошла, а файлы всё равно записаны. Код 1 — ошибка входа или ввода-вывода. Код 3 — критическая ошибка детализации.

Полезные флаги: `--legend`, `--catalog`, `--ml-url`, `--slab-width`, `--slab-height`, `--thickness`, `--cover`.

## Выходы

| Файл | Роль |
|---|---|
| `*.result.json` | Канонический отчёт. Неизвестные свойства отвергаются |
| `*.schedule.csv` | Позиции и масса. Кавычки безопасны для таблиц |
| `*.aerobim.json` | Короткая сводка для внешней проверки |
| `*.reinforcement.ifc` | Стержни IFC4 |

Отчёт хранит нормативный профиль, версию таблиц, provenance оптимизатора, `wastePercent` с учётом пропила и `estimatedCost`. Новое поле верхнего уровня требует подъёма схемы. `checks[].status` — это `Passed`, `Failed` или `NotEvaluated`.

## Слои

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
| `IImageSegmentationService` | Необязательный HTTP-мост. Офлайн. Без внешней модели |

Генерация столбцов — основной раскрой. Она записывает, когда двойственная граница имеет статус `Proven`. Запасной класс — `FirstFitDecreasingOptimizer`; его цикл оставляет хлыст с наименьшим остатком. Ни то ни другое не является заявлением о минимуме отходов на реальном объекте.

## Известные пределы

- Один чертёж — один слой. Файл проекта может назвать до четырёх чертежей.
- Анкеровка останавливается на границе рабочей области. Крюк — дуга по оси, без прямого хвоста, пока профиль не задаст `hooks.tailDiameters`. Муфты не моделируются. Отверстия учитываются и остаются внутри проверяемой площади.
- Генерация столбцов пакует одну складскую длину за раз.
- Ведомость — CSV. XLSX не пишется.
- IFC — это IFC4. Это не модель нахлёста IFC 4.3, и круговой обмен с импортом Revit в этом репозитории не прогонялся.
- Постановка в Revit крюка, Г или П отклоняется. Записью остаются ведомость и IFC.
- PNG требует калибровки (`--px-per-mm` и начало координат). Кадр больше 25 миллионов пикселей отвергается до декодирования.
- `examples/dxf/simple-slab` при Ø20 и защитном слое 30 мм: `edgeDevelopmentAreaM2` 6,12, `realDeficitAreaM2` 0,99, `underReinforcedAreaM2` 7,11.

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

CI ставит этот lock с хэшами на Python 3.11. Вызова внешней модели нет. Пустой манифест значит, что у сервиса нет контрольной точки.

## Revit

Скопируйте `addin/RevitNet8` рядом со сборкой `net8.0-windows` или `addin/RevitNet10` рядом со сборкой `net10.0-windows`. Ось стержня сидит на cover + d/2. Внутреннее направление грани лежит на внешнем. Верхний защитный слой читается из `CLEAR_COVER_TOP`. См. [RELEASE_POLICY.md](RELEASE_POLICY.md).

## Документы

- [docs/README.md](docs/README.md) — карта
- [docs/architecture.md](docs/architecture.md)
- [docs/NORMATIVE_TRACEABILITY.md](docs/NORMATIVE_TRACEABILITY.md) — пункт, код, тест
- [docs/VALIDATION_BASELINE.md](docs/VALIDATION_BASELINE.md) — как публичная цифра получает право на существование
- [docs/SOTA_2026.md](docs/SOTA_2026.md) — список разрывов
- [docs/OPENREBAR_AGENT_PLAN_r10_2026_10_14.md](docs/OPENREBAR_AGENT_PLAN_r10_2026_10_14.md) — текущая очередь
- [CHANGELOG.md](CHANGELOG.md), [CONTRIBUTING.md](CONTRIBUTING.md), [SECURITY.md](SECURITY.md), [CITATION.cff](CITATION.cff)

Аудиты апреля 2026 в `docs/archive/` — история.

## Лицензия

MIT. См. [LICENSE](LICENSE).
