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

## Постановка задачи

Мотивирующий сценарий: инженер по ЖБ размещает арматуру в Revit на основании карт изолиний, экспортируемых из расчётных/постпроцессорных инструментов (например, LIRA-SAPR / Stark-ES). Ручная постановка занимает существенное время и даёт вариативность результатов (между этажами, зонами и исполнителями).

OpenRebar реализует воспроизводимый конвейер:

1. Парсинг файла изолиний (**DXF** или **PNG**) в набор цвето-кодированных зон армирования
2. Классификация зон и декомпозиция сложных многоугольников в прямоугольники (с сохранением метрик покрытия/перепокрытия)
3. Расчёт раскладки арматуры по зонам (шаг, диаметры, анкеровка)
4. Оптимизация раскроя (1D CSP) для снижения отходов (базовая схема в стиле column generation + точный fallback для малых экземпляров)
5. Сохранение проверяемых машинно-читаемых артефактов для downstream BIM-систем
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

**Каноническая схема:** `contracts/aerobim-reinforcement-report.schema.json`

Канонический отчёт явно хранит:

- `normativeProfile` (например, `ru.sp63.2018`) и идентификатор набора таблиц с версией (например, `ru.sp63.2018.tables.v1`)
- `analysisProvenance` по декомпозиции геометрии и оптимизации раскроя (идентификаторы алгоритмов, пороги, fallback-ветви)

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

Текущая нормативная база упакована как версионированный embedded resource профиль, с golden-тестами для:

- lookup bond stress / design strength
- классификации периодического профиля
- таблиц линейной массы

### Оптимизация раскроя

Две реализации оптимизатора за портом `IRebarOptimizer`:

| Алгоритм | Роль |
|---|---|
| `ColumnGenerationOptimizer` | Инженерный production-бейзлайн с сохранением provenance и точным fallback для малых экземпляров |
| `FirstFitDecreasingOptimizer` | Эвристический бейзлайн и fallback |

Текущую реализацию column generation следует трактовать как сильный инженерный бейзлайн (LP/pricing/repair), но не как математически полный branch-and-price. Это различие явно фиксируется в provenance канонического отчёта.

### Распознавание цветов (DXF/PNG)

- **DXF:** палитра AutoCAD ACI (256 цветов) + ByLayer
- **PNG:** сравнение цветов в CIE L*a*b* по ΔE*76 (ISO/CIE 11664-4)
- Опционально: ML-сегментация PNG через FastAPI (ml/)

## Сборка и тесты

```bash
dotnet build OpenRebar.sln
dotnet test OpenRebar.sln
```

Текущий регрессионный статус (локальный `dotnet test OpenRebar.sln --configuration Release`): **163/163 теста проходят**.

## Комплексный аудит (2026-04-25)

Проведён полный аудит проекта по уровням: архитектура, алгоритмическая корректность, качество CI/CD, supply-chain безопасность, уязвимости зависимостей и качество документации.

- Отчёт аудита: [docs/COMPREHENSIVE_PROJECT_AUDIT_2026_04_25.md](docs/COMPREHENSIVE_PROJECT_AUDIT_2026_04_25.md)
- Ключевой технический фикс: cost-aware non-regression guard в `ColumnGenerationOptimizer` + отдельный регрессионный тест
- Базовый стек верификации: `git fsck --full`, `dotnet build`, `dotnet test`, `dotnet list package --vulnerable --include-transitive` и формат-гейт `dotnet format --verify-no-changes`

### Опциональный corpus rail

Manifest-driven corpus rail намеренно сделан опциональным. Чтобы включить его, добавьте:

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

## Python ML модуль (опционально)

```bash
cd ml
pip install -r requirements.txt
pytest tests -q
uvicorn src.api.server:app --port 8101
```

В CI python-smoke тесты запускаются с явным PYTHONPATH, указывающим на ml/.

## Граница Revit-хоста

Revit-хост компилируется под `#if REVIT_SDK` и требует локальных Autodesk Revit references.
В репозитории присутствуют:

- DI composition root (`src/OpenRebar.RevitPlugin/Bootstrap.cs`)
- реализация размещения через `Rebar.CreateFromCurves`
- проход создания тегов (`IndependentTag.Create`)
- трекинг форм гибов (создание bending detail элементов оставлено как явная TODO-граница)

## Документация

- Архитектурные заметки: docs/architecture.md
- Комплексный аудит: docs/COMPREHENSIVE_PROJECT_AUDIT_2026_04_25.md
- Changelog: CHANGELOG.md
- Audit и roadmap: HYPER_DEEP_AUDIT_REPORT.md, TASKS.md
- Contribution guide: CONTRIBUTING.md
- Security policy: SECURITY.md
- Code of conduct: CODE_OF_CONDUCT.md
- Citation metadata: CITATION.cff
- Funding metadata: .github/FUNDING.yml

## Лицензия

MIT — см. LICENSE.
