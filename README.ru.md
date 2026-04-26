# OpenRebar — автоматизация армирования ЖБ плит по изолиниям

[![CI](https://github.com/KonkovDV/OpenRebar/actions/workflows/ci.yml/badge.svg)](https://github.com/KonkovDV/OpenRebar/actions/workflows/ci.yml)
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/KonkovDV/OpenRebar/badge)](https://scorecard.dev/viewer/?uri=github.com/KonkovDV/OpenRebar)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Русский | [English](README.md)

OpenRebar — кодовая база на .NET 8 для генерации раскладки арматуры в плоских железобетонных плитах по входным данным в формате изолиний (DXF или PNG). Репозиторий спроектирован так, чтобы **инженерная логика была тестируемой вне Revit**, но при этом сохранялась возможность выхода на границу плагина Revit 2025.

В репозитории поддерживаются три поверхности исполнения:

- **CLI-хост** (`src/OpenRebar.Cli`) — пакетные прогоны, CI, отладка, генерация интеграционных артефактов
- **Revit-хост** (`src/OpenRebar.RevitPlugin`) — ExternalCommand + UI (компилируется только при наличии ссылок на Revit SDK)
- **Опциональный ML-модуль** (`ml/`) — сегментация PNG-изолиний U-Net через HTTP (FastAPI)

## Содержание

- [Постановка задачи](#постановка-задачи)
- [Быстрый старт (паритет с CI)](#быстрый-старт-паритет-с-ci)
- [Выходные артефакты и контракт интеграции](#выходные-артефакты-и-контракт-интеграции)
- [Архитектура](#архитектура)
- [Доменные порты](#доменные-порты)
- [Сборка и тесты](#сборка-и-тесты)
- [Контрольные проверки CI](#контрольные-проверки-ci)
- [Канонические примеры и снапшоты](#канонические-примеры-и-снапшоты)
- [Python ML модуль (опционально)](#python-ml-модуль-опционально)
- [Граница Revit-хоста](#граница-revit-хоста)
- [Документация](#документация)
- [Академический Стандарт Отчётности](#академический-стандарт-отчётности)

## Быстрый старт (паритет с CI)

Предварительные требования:

- .NET SDK 8.x
- Python 3.11+ (для `tools/ci/*.py` и опционального модуля `ml/`)
- Git с корректной обработкой LF

Запускать из корня репозитория:

```bash
dotnet restore OpenRebar.sln --locked-mode -p:EnableWindowsTargeting=true
dotnet build OpenRebar.sln --no-restore --configuration Release -p:EnableWindowsTargeting=true
dotnet format OpenRebar.sln --verify-no-changes --no-restore
dotnet test OpenRebar.sln --no-build --configuration Release
python tools/ci/verify_readme_regression_claim.py
```

Эта последовательность соответствует базовому .NET-контуру в CI и рекомендуется как минимальный набор проверок перед PR для изменений кода и документации.

## Постановка задачи

Мотивирующий сценарий: инженер по ЖБ размещает арматуру в Revit на основании карт изолиний, экспортируемых из расчётных/постпроцессорных инструментов (например, LIRA-SAPR / Stark-ES). Ручная постановка занимает существенное время и даёт вариативность результатов (между этажами, зонами и исполнителями).

OpenRebar реализует воспроизводимый конвейер:

1. Парсинг файла изолиний (**DXF** или **PNG**) в набор цвето-кодированных зон армирования
2. Классификация зон и декомпозиция сложных многоугольников в прямоугольники (с сохранением метрик покрытия/перепокрытия)
3. Расчёт раскладки арматуры по зонам (шаг, диаметры, анкеровка)
4. Оптимизация раскроя (1D CSP) для снижения отходов (точный путь для малых экземпляров + column generation для более крупных)
5. Сохранение проверяемых машинно-читаемых артефактов для внешних BIM-систем
6. (При включении) размещение арматуры в Revit и генерация тегов / трекинг форм гибов

Цель — сократить рутинную постановку армирования плиты с инженер-недель до инженер-часов **при корректной валидации предпосылок** и наличии стабильного контракта выходных данных.

## Выходные артефакты и контракт интеграции

Репозиторий рассматривает машинно-читаемые выходы как первоклассные артефакты.

| Артефакт | Кто формирует | Назначение |
|---|---|---|
| `*.result.json` | Pipeline / CLI | Канонический отчёт исполнения (под schema) |
| `*.aerobim.json` | CLI exporter | Экспорт в формат, удобный для AeroBIM |
| `*.schedule.csv` | CLI exporter | Выгрузка ведомости арматуры (CSV) |
| `*.reinforcement.ifc` | CLI exporter | IFC-экспорт (IFC4 через xBIM) |

**Каноническая схема:** `contracts/aerobim-reinforcement-report.schema.json` (`schemaVersion` `1.2.0`)

Канонический отчёт явно хранит:

- `normativeProfile` (например, `ru.sp63.2018`) и идентификатор набора таблиц с версией (например, `ru.sp63.2018.tables.v1`)
- `analysisProvenance` по декомпозиции геометрии и оптимизации раскроя (идентификаторы алгоритмов, пороги, резервные ветви)
- `sawCutWidthMm` для каждого cutting plan, чтобы downstream-потребители могли независимо пересчитывать kerf-aware `wasteMm` / `wastePercent`
- `dualBound` / `gap` по каждому диаметру (если доступны у оптимизатора); в эвристическом резервном режиме эти поля намеренно `null`, так как LP-гарантии нижней границы там не действуют

## Архитектура

Clean Architecture со строгой инверсией зависимостей:

```
Domain (pure) ← Application (use cases) ← Infrastructure (adapters) ← Hosts (CLI, RevitPlugin)
```

- **Domain** (`src/OpenRebar.Domain`): модели, порты (интерфейсы), правила; без внешних зависимостей
- **Application** (`src/OpenRebar.Application`): оркестрация и use cases; зависит только от Domain
- **Infrastructure** (`src/OpenRebar.Infrastructure`): DXF/PNG парсинг, оптимизация, экспорты, report store, логирование; зависит от Domain + Application
- **Hosts**:
  - CLI (`src/OpenRebar.Cli`)
  - Revit-плагин (`src/OpenRebar.RevitPlugin`, `#if REVIT_SDK`)

## Доменные порты

Порты определены в `src/OpenRebar.Domain/Ports/`.

| Порт | Ответственность |
|---|---|
| `IIsolineParser` | Парсинг DXF/PNG изолиний в зоны |
| `ILegendLoader` | Загрузка/выдача конфигурации легенды |
| `IZoneDetector` | Классификация зон и декомпозиция многоугольников |
| `IReinforcementCalculator` | Генерация стержней/сегментов по зонам |
| `IRebarOptimizer` | Оптимизация раскроя арматуры (1D CSP) |
| `ISupplierCatalogLoader` | Загрузка доступных длин/цен по поставщикам |
| `IReportStore` | Сохранение канонических `*.result.json` отчётов |
| `IReportExporter` | Экспорт отчётов для внешних систем (например, AeroBIM) |
| `IScheduleExporter` | Экспорт ведомости арматуры (CSV) |
| `IIfcExporter` | IFC-экспорт |
| `IRevitPlacer` | Размещение арматуры в активном документе Revit |
| `IImageSegmentationService` | ML-мост для сегментации PNG (HTTP) |
| `IStructuredLogger` | Минимальная абстракция структурного логирования |

## Ключевые реализованные поверхности

### Нормативный движок (СП 63.13330.2018)

Текущая нормативная база упакована как версионированный встроенный ресурс, с эталонными тестами для:

- lookup bond stress / design strength
- классификации периодического профиля
- таблиц линейной массы

### Оптимизация раскроя

Две реализации оптимизатора за портом `IRebarOptimizer`:

| Алгоритм | Роль |
|---|---|
| `ColumnGenerationOptimizer` | Инженерный рабочий базовый алгоритм с сохранением provenance и точным fast-path для малых экземпляров |
| `FirstFitDecreasingOptimizer` | Эвристический базовый алгоритм и резервный маршрут |

Текущую реализацию column generation следует трактовать как сильный инженерный базовый алгоритм (LP/pricing/repair), но не как математически полный branch-and-price. Это различие явно фиксируется в provenance канонического отчёта.
Показатели `WasteMm` / `WastePercent` kerf-aware: они измеряют остаток стержня после вычитания как установленной длины арматуры, так и потерь материала на saw cut.

### Распознавание цветов (DXF/PNG)

- **DXF:** палитра AutoCAD ACI (256 цветов) + ByLayer
- **PNG:** сравнение цветов в CIE L*a*b* по ΔE*76 (ISO/CIE 11664-4)
- Опционально: ML-сегментация PNG через FastAPI (ml/)

## Сборка и тесты

```bash
dotnet restore OpenRebar.sln --locked-mode -p:EnableWindowsTargeting=true
dotnet build OpenRebar.sln --no-restore --configuration Release -p:EnableWindowsTargeting=true
dotnet format OpenRebar.sln --verify-no-changes --no-restore
dotnet test OpenRebar.sln --no-build --configuration Release
```

Текущий регрессионный статус (локальный `dotnet test OpenRebar.sln --configuration Release`): **193/193 тестов проходят**.

## Контрольные проверки CI

Конвейер `build-and-test` проверяет не только компиляцию, но и согласованность заявлений:

| Проверка | Назначение | Эффект при падении |
|---|---|---|
| `dotnet restore --locked-mode` | целостность файла блокировки зависимостей и детерминированный граф зависимостей | блокирует конвейер |
| `dotnet build` | целостность компиляции | блокирует конвейер |
| `dotnet format --verify-no-changes --no-restore` | защита от дрейфа форматирования | блокирует конвейер |
| `dotnet test` + TRX | исполнимый регрессионный базовый уровень | блокирует конвейер |
| `verify_readme_regression_claim.py` | паритет числа тестов в README EN/RU с TRX | блокирует конвейер |
| отчёты по состоянию зависимостей | видимость состояния зависимостей (`--vulnerable`, `--outdated`) | публикация артефактов |
| пороговая проверка benchmark summary | контроль допустимого диапазона качества | блокирует benchmark-конвейер |

Для аудит-уровня используйте [docs/VALIDATION_BASELINE.md](docs/VALIDATION_BASELINE.md).

## Комплексный аудит (2026-04-25)

Проведён полный аудит проекта по уровням: архитектура, алгоритмическая корректность, качество CI/CD, supply-chain безопасность, уязвимости зависимостей и качество документации.

- Отчёт аудита: [docs/COMPREHENSIVE_PROJECT_AUDIT_2026_04_25.md](docs/COMPREHENSIVE_PROJECT_AUDIT_2026_04_25.md)
- Ключевой технический фикс: cost-aware non-regression guard в `ColumnGenerationOptimizer` + отдельный регрессионный тест
- Дополнительный фикс: mixed-stock constructive packing для heterogeneous stock catalog, закрывающий large-batch single-stock regression и расширяющий покрытие бенчмарков за пределы exact-search envelope
- Базовый стек верификации: `git fsck --full`, `dotnet build`, `dotnet test`, `dotnet list package --vulnerable --include-transitive` и формат-гейт `dotnet format --verify-no-changes`

### Опциональный контур корпусных данных

Контур корпусных данных на основе manifest намеренно сделан опциональным. Чтобы включить его, добавьте:

- `tests/OpenRebar.Application.Tests/Fixtures/BatchBenchmarkCorpus/manifest.json`, или
- установите `OPENREBAR_BATCH_CORPUS_ROOT` на директорию, содержащую manifest и фикстуры.

## Быстрый старт CLI

```bash
dotnet run --project src/OpenRebar.Cli -- <isoline-file> [options]
```

Часто используемые опции:

- --legend <path>: легенда (JSON)
- --catalog <path>: каталог поставщика (JSON/CSV)
- --ml-url <url>: сервис сегментации PNG (например, http://localhost:8101)
- --slab-width <mm> / --slab-height <mm> / --thickness <mm> / --cover <mm>

CLI пишет канонический отчёт рядом со входным файлом (`.result.json`), а также формирует `.schedule.csv`, `.aerobim.json` и `.reinforcement.ifc`.

## Канонические примеры и снапшоты

В репозитории добавлены воспроизводимые примеры в `examples/`:

- `examples/dxf/simple-slab/input.dxf`
- `examples/png/simple-slab/input.png`

Для каждого примера зафиксированы эталонные снапшоты:

- `expected/input.result.json`
- `expected/input.schedule.csv`

Скрипты пересборки эталонов:

```bash
# Linux/macOS
bash tools/examples/generate_expected_outputs.sh

# Windows PowerShell
powershell -ExecutionPolicy Bypass -File tools/examples/generate_expected_outputs.ps1
```

Проверка снапшотов выполняется тестами `ExamplesSnapshotTests` (`tests/OpenRebar.Application.Tests`).

## Python ML модуль (опционально)

```bash
cd ml

# Установка с проверкой хэшей (рекомендуется для production)
pip install --require-hashes -r requirements.locked.txt

# Альтернатива для локальной разработки
pip install -r requirements.txt

pytest tests -q
uvicorn src.api.server:app --port 8101
```

В CI python-smoke тесты запускаются с явным PYTHONPATH, указывающим на ml/, а зависимости устанавливаются из `ml/requirements.locked.txt` с `--require-hashes`.

**Безопасность**: см. [ml/SUPPLY_CHAIN_SECURITY.md](ml/SUPPLY_CHAIN_SECURITY.md)

- lock-файл зависимостей с SHA256 хэшами
- манифест чекпоинтов `ml/models/MANIFEST.json` для проверки целостности
- процедуры CI/CD-валидации
- шаблон регулярного re-pin зависимостей

## Граница Revit-хоста

Revit-хост компилируется под `#if REVIT_SDK` и требует локальных Autodesk Revit references.
В репозитории присутствуют:

- DI composition root (`src/OpenRebar.RevitPlugin/Bootstrap.cs`)
- реализация размещения через `Rebar.CreateFromCurves`
- проход создания тегов (`IndependentTag.Create`)
- отслеживание форм гибов (создание bending detail-элементов оставлено как явная TODO-граница)

## Документация

- Роутер документации: [docs/README.md](docs/README.md)
- Архитектурные заметки: [docs/architecture.md](docs/architecture.md)
- Комплексный аудит: [docs/COMPREHENSIVE_PROJECT_AUDIT_2026_04_25.md](docs/COMPREHENSIVE_PROJECT_AUDIT_2026_04_25.md)
- Changelog: [CHANGELOG.md](CHANGELOG.md)
- Архив аудита и дорожной карты: [docs/HYPER_DEEP_AUDIT_REPORT.md](docs/HYPER_DEEP_AUDIT_REPORT.md), [docs/TASKS.md](docs/TASKS.md)
- Contribution guide: [CONTRIBUTING.md](CONTRIBUTING.md)
- Security policy: [SECURITY.md](SECURITY.md)
- Code of conduct: [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)
- Citation metadata: [CITATION.cff](CITATION.cff)
- Funding metadata: [.github/FUNDING.yml](.github/FUNDING.yml)

## Академический Стандарт Отчётности

### Граница Утверждений

Этот README разделяет реализованное и проверенное поведение от плановых намерений.

- Подтверждённые утверждения опираются на исполнимые пути кода, контракты, тесты и сохранённые отчёты.
- Плановые элементы остаются явно помеченными как будущая работа.
- Метрики производительности и качества привязаны к конкретным артефактам и контурам валидации, а не к универсальным гарантиям.

### Базовый Протокол Воспроизводимости

Используйте [docs/VALIDATION_BASELINE.md](docs/VALIDATION_BASELINE.md) как канонический базовый протокол валидации перед публикацией инженерных или бенчмарк-утверждений.

Минимальный репозиторный набор проверок:

```bash
dotnet build OpenRebar.sln --configuration Release
dotnet test OpenRebar.sln --configuration Release
cd ml
python -m pip install --require-hashes -r requirements.locked.txt
python -m pytest tests -q
```

Если менялись ML-зависимости или логика конвейера, дополнительно проверьте закреплённый путь обновления lock-файла, описанный в [docs/VALIDATION_BASELINE.md](docs/VALIDATION_BASELINE.md):

```bash
cd ml
python -m pip install --require-hashes -r ..\.github\requirements\pip-tools.locked.txt
python -m piptools compile --allow-unsafe --generate-hashes --output-file=requirements.locked.txt requirements.in
```

Для утверждений на основе отчётов указывайте путь к report-файлу, версию schema-контракта и commit SHA.

### Цитирование И Научное Переиспользование

- Используйте метаданные цитирования из `CITATION.cff`.
- Для сравнительных исследований фиксируйте и достигнутые показатели качества, и оставшиеся граничные условия.
- При внешних публикациях сохраняйте явную связку с `normativeProfile` и версией таблиц.

## Управление проектом

- [Support](SUPPORT.md)
- [Contributing](CONTRIBUTING.md)
- [Security Policy](SECURITY.md)
- [Maintainers](MAINTAINERS.md)
- [Release Policy](RELEASE_POLICY.md)
- [Citation Metadata](CITATION.cff)

## Правила ведения документации

Чтобы не допускать расхождения между публичными утверждениями и проверяемыми фактами:

1. Сначала обновляйте канонические поверхности (`README.md`, `README.ru.md`, [docs/VALIDATION_BASELINE.md](docs/VALIDATION_BASELINE.md), [docs/README.md](docs/README.md)).
2. Исторические цифры оставляйте только с датой в документах дорожной карты и аудитов; актуальные значения держите в текущих публичных описаниях.
3. Если изменилось поведение, в том же изменении обновляйте командные примеры и рекомендации по паритету с CI.
4. Снимки отчётов трактуйте как датированные подтверждения, а не как вечную истину.

## Лицензия

MIT — см. LICENSE.
