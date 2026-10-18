# OpenRebar: план r10 для ИИ-агента (`main` @ `6e9bea0`, 14.10.2026)

> **Кому:** ИИ-агенту, который работает с `KonkovDV/OpenRebar` через GitHub (MCP или `gh`), в том числе без локального .NET SDK.
> **Срез `main`:** `6e9bea0` (merge PR #186). CI на `main` зелёный. Регрессия **467/467**: Domain 157, Infrastructure 178, TestCorpus 7, Application 99 (net8.0), Cli 26 (net10.0).
> **Приоритет источников:** код и тесты `main` → r10 → r9 → r8 → r7 → r6 → r5. Ответ владельца в `docs/agent/decisions/DECISIONS.md` сильнее любого плана (сейчас там нет ни одной строки).
> **Что это:** сводная очередь. r9 §3 закрыт частично; всё открытое из r9, Red Team 2026-10-03 и r7 §7 перенесено сюда с проверенным статусом.

## 0. Правила, которые нарушались (прочитать до первой правки)

Каждое правило ниже взято из реальной ошибки в PR #157 или #159.

1. **Файл — обычный текст UTF-8.** PR #157 записал `.cs` файлы как base64, сборка упала. Передавать в инструмент записи исходный текст файла. После пуша прочитать файл обратно с GitHub: первая строка должна быть `using …` / `namespace …` / `#`, а не `dXNpbmcg…`.
2. **Не создавать второй тестовый класс с тем же именем.** PR #159 добавил второй `SlabGeometryTests` → `CS0101`, `CS0111`. Перед новым классом искать `class <Имя>` по репозиторию; если класс есть, дописывать тест в него.
3. **Не придумывать нормы.** PR #157 ввёл хвост крюка 12d, PR #159 сослался на п. 10.3 для `h − c − d/2`. Число попадает в код только из `src/OpenRebar.Domain/Rules/Data/*.json` с `sourceQuote`, ссылкой и датой доступа. Если первоисточника нет — безопасное умолчание из §6 и стоп-вопрос.
4. **Не печатать эталонные цифры по памяти.** Изменение `examples/*/*/expected/*` допускается, только если каждое изменённое число объяснено в описании PR формулой. Без SDK брать фактические значения из лога упавшего `ExamplesSnapshotTests` в CI, а не из чужого отчёта.
5. **Счётчик тестов — только из TRX.** README и README.ru меняются вместе, в одном коммите. Без SDK: новое число = 453 + число добавленных тестов (`[Fact]` = 1, каждая строка `[InlineData]` = 1; считаются только net8-прогоны четырёх библиотек и net10 `Cli.Tests`). CI (`verify_readme_regression_claim.py`) проверит.
6. **Не писать `-p:Platform=x64` в проектный `dotnet test --no-build`.** Solution пишет в `bin/Release`, флаг заставляет искать `bin/x64/Release`.
7. **Личность коммита — `Konkov Dmitrij <272916918+KonkovDV@users.noreply.github.com>`, автор и коммитер.** Без `Co-authored-by`, без «Generated with», без имён моделей и ассистентов в сообщениях, коде и документах. Через API автором становится владелец токена: после первого коммита проверить `list_commits`; если имя или адрес другие — остановиться и сообщить, не переписывать.
8. **История `main` неизменна.** Не делать force-push, rebase, amend и filter-branch по `main`. Коммит `47dc36d` (2026-10-04T00:11:17+03:00) зафиксирован владельцем навсегда. Новое — только новыми коммитами через PR.
9. **На GitHub остаётся только `main`.** Одна задача = одна ветка от свежего `main` = один PR. После merge ветку удалить. Не открывать вторую ветку, пока первая не влита или не закрыта.
10. **Merge — только при зелёных проверках на GitHub.** Локальный зелёный не считается. Обязательные (имена как в `gh pr checks`): `lint-workflows`, `build-and-test`, `python-smoke`, `corpus-note`, `CodeQL (C#)`, `CodeQL (Python)`, `dependency-review`; плюс job из `ifc-validation.yml`, если PR трогает `Export/` или `examples/`. `benchmark-corpus` на PR пропускается — это норма. После merge дождаться зелёного CI на `main`.
11. **Без SDK CI — это компилятор.** Красный job: прочитать лог, исправить новым коммитом в ту же ветку. После трёх красных итераций подряд остановиться и описать ошибку владельцу.
12. **Не коммитить** `bin/`, `obj/`, `artifacts/`, `TestResults/`, `*.trx`, папку `files/`.

## 1. Инварианты (из AGENTS.md и r5 §5, не меняются)

- Domain без NuGet. NetTopologySuite, HiGHS, xBIM, ImageSharp — только Infrastructure, за портами `src/OpenRebar.Domain/Ports/`.
- Revit API только в `src/OpenRebar.RevitPlugin/` под `#if REVIT_SDK`. Две сборки: `RevitNet8`, `RevitNet10` по версии рантайма.
- DI собирается в `ServiceCollectionExtensions` и `Bootstrap.cs`.
- Новое поле в `*.result.json` → новое свойство в `contracts/aerobim-reinforcement-report.schema.json` (там `additionalProperties: false`), новый верхний ключ ещё и в `ExpectedTopLevelProperties` (`ReportSchemaComplianceTests`), и `SchemaVersion` в `ReinforcementReport.cs`: `2.0.0` → `2.1.0` для добавления поля, `3.0.0` для удаления или переименования.
- Статус проверки: `Passed`, `Failed`, `NotEvaluated`. Трещины и прогиб — `NotEvaluated`, пока вход только As,req.
- Жёсткая коллизия сама по себе не валит верификацию; ворота — проверка площади.
- Зачёт крюка 0. Хвост крюка по умолчанию 0 (`hooks.tailDiameters`, `source: default`). П-образные скобы не генерировать.
- Rs: A400 340, B500 415, A500 435. Rsc A400 340 + `NotVerified` (Q-15). Rbt B55/B60 не трогать (RT-3).
- ImageSharp остаётся на 3.x, FluentAssertions на 7.x.
- `ml/` офлайн: никаких вызовов внешних моделей. Lock ставится с `--require-hashes`.
- Стиль C#: 2 пробела, file-scoped namespace, `TreatWarningsAsErrors`. Гейт `dotnet format --verify-no-changes`.
- Публично не заявлять: экономию стали в %, ML-модель, «готово для Revit» для гнутых стержней.

## 2. Что закрыто после r9 (проверено по `main`)

| r9 | Что | PR / коммит |
|---|---|---|
| P0-r9a…d | прогон, торцевой слой, единая масса, крюк | до #158 |
| PR-9 | ось крюка: прямая + полуокружность, `hooks.tailDiameters` | [#158](https://github.com/KonkovDV/OpenRebar/pull/158) |
| п. 7 | `EffectiveDepthFor(d)`, отчёт 190 → 180 мм | [#159](https://github.com/KonkovDV/OpenRebar/pull/159), `47dc36d` |
| PR-7 | детерминированные `BarId`, IFC `Name` и `GlobalId` из SHA-256 | [#185](https://github.com/KonkovDV/OpenRebar/pull/185) |
| — | ImageSharp 3.1.12: 5 GHSA приняты в `NuGetAuditSuppress` и `ACCEPTED_ADVISORIES` | [#185](https://github.com/KonkovDV/OpenRebar/pull/185) |
| RT-2 | максимальный шаг по суммарному слою, `layerSpacing` в `ClearanceChecker` | [#186](https://github.com/KonkovDV/OpenRebar/pull/186) |

## 3. Факты, на которые опирается очередь

- **Dependabot alerts (36 открытых)** — все pip, все в `ml/requirements.locked.txt` (torch и в `ml/requirements.txt`). Первые исправленные версии: pillow 12.3.0 (17 алертов, high), starlette 1.3.1 (5, high), python-multipart 0.0.31 (5, high), anyio 4.14.2 (critical), torch 2.13.0, onnx 1.22.0, setuptools 83.0.0, idna 3.15, pytest 9.0.3. `ml/requirements.txt` ограничивает `Pillow<11.0` и `pytest<9.0` — без правки диапазонов lock не обновить.
- **ImageSharp:** все 5 принятых GHSA имеют диапазон `<= 4.1.1` и исправлены только в **4.1.2**. Версия 3.2.0 (PR #161, #179) их **не** закрывает. Значит, исключение остаётся, пока владелец не решит Q-5.
- **Открытые PR Dependabot (25):** #160–#184. ImageSharp 4.1.3 пришёл в #164 (Infrastructure) и #168 (из каталога Application, где нет `ignore` для ImageSharp). xunit.runner.visualstudio 4.0.1 пришёл пятью PR (по одному на тестовый проект).
- **Настройки репозитория:** защиты `main` нет (HTTP 404), rulesets пусты, environment `release` отсутствует.
- **Статуса `NotEvaluated` нет ни в коде, ни в схеме отчёта.** `verification.status` знает только `Passed`, `Failed`, `PassedWithUnderCoverage`, `PassedWithSmoothing`. Инвариант AGENTS.md про трещины и прогиб в отчёте не виден.
- **Каталог CSV** (`FileSupplierCatalogLoader.LoadCsvAsync`): `double.TryParse` без `CultureInfo.InvariantCulture`, плохие строки пропускаются молча, нет проверок `> 0`, NaN/∞ и дублей. JSON-ветка тоже не проверяет значения.
- **PNG** (`PngIsolineParser.ParseWithColorQuantizationAsync`): лимит `MaxImagePixels` проверяется после `Image.LoadAsync`, то есть после полного декодирования.
- **DXF** (`DxfIsolineParser.CollectEntity`): эллипс и сплайн считаются в `unsupportedCurve`, открытые полилинии — в `ignoredOpenPolylines`. Молча теряются: сущность без цвета, цвет вне легенды, прочие типы сущностей (ветка `default` в `ExtractPolygonFromEntity`).
- **Доля стыков** (`LapPlanner.Plan`): окно 1,3·l_l и лимит `min(0.5, jointRatioMax)` считаются по группе стержней с одинаковыми концами. `ClearanceChecker.AddLapClashes` (`lapOverload`) считает по линиям прогона, но α в `LapPlanner` от этого не зависит.
- **Края плиты:** `SlabEdge.WorkingRectangle` строит рабочую область по bounding box плиты. Для косой плиты с объявленными опорами контур подменяется прямоугольником.
- **Эффективная высота:** `effectiveDepthMm` остаётся цифрой наибольшего поставленного стержня. `effectiveDepthByLayer` пишет `h − z` для нижнего слоя и `z` для верхнего, только для слоёв со стержнями. На simple-slab это `BottomX: 180`.
- **Оптимизатор:** класс `BestFitDecreasingOptimizer`, id `best-fit-decreasing-v1`. Аварийный упаковщик конвейера по-прежнему берёт первый подходящий хлыст (`first-fit-decreasing`). `MinScrapLengthMm` удалён. В отчёте `stackTraceSha256`, текст стека только в журнале.
- **IFC:** `IfcReinforcingBar` без `IfcReinforcingBarType`, без `IfcSweptDiskSolid`, без явного `ObjectPlacement`. IDS-проверки в CI нет.
- **Merge-коммиты #185 и #186** имеют коммитера `GitHub <noreply@github.com>` (так работает кнопка merge). Автор — Konkov Dmitrij. Менять уже влитые коммиты нельзя (правило 8).

## 4. Очередь

Порядок сверху вниз. Колонка «Среда»: **MCP** — можно сделать без SDK, CI проверит; **SDK** — нужен локальный .NET или Python (генерация хэшей, перегенерация эталонов). Агент без SDK задачи SDK не берёт, а пишет владельцу, что они ждут.

| # | ID | Задача | Среда | Эталоны | Схема |
|---|---|---|---|---|---|
| 1 | D-1 | Разбор PR Dependabot и `dependabot.yml` — сделано | MCP | нет | нет |
| 2 | D-2 | Обновить ML lock, закрыть 36 алертов | SDK | нет | нет |
| 3 | PR-12 | Каталог CSV/JSON без тихих потерь — сделано | MCP | нет | нет |
| 4 | RT-7 | PNG: размер до декодирования — сделано | MCP | нет | нет |
| 5 | PR-11 | DXF без тихих потерь — сделано | MCP | возможно `ignoredByReason` | нет |
| 6 | C-1 | Список проверок со статусом `NotEvaluated` — сделано | SDK | да | 2.1.0 |
| 7 | RT-5 | Доля стыков по всему сечению — сделано | SDK | нет (в эталонах нет стыков) | нет |
| 8 | G-1 | Косая плита: не подменять контур прямоугольником — сделано | SDK | нет | нет |
| 9 | G-2 | Эффективная высота по слоям — сделано | SDK | да | 2.1.0 |
| 10 | PR-10a | IFC v2: тип, тело, размещение — сделано | SDK | IFC | нет |
| 11 | PR-10b | IDS в CI — сделано | MCP | нет | нет |
| 12 | PR-17 | Раскрой: имя алгоритма, `MinScrapLengthMm`, хэш стека — сделано | SDK | да | 2.1.0 |
| 13 | RT-4 | Два KPI раскроя — уже есть: `wastePercent` и `estimatedCost` | SDK | нет | нет |
| 14 | RT-6 | `laps.representation` — сделано | SDK | нет | профиль |
| 15 | S-1 | Supply chain: OSV, SBOM, ML endpoint | MCP | нет | нет |
| 16 | P3 | Аудитор КЖ, ЛИРА, сравнение раскладок | владелец | — | — |

Задачи 6, 9, 12, 13 меняют схему. Если две из них идут подряд, версия поднимается один раз, а следующая задача дописывает поле в ту же 2.1.0, пока не было релиза.

### 4.1 D-1. Разбор PR Dependabot (MCP) — сделано

`dependabot.yml` сведён в один NuGet-каталог. ImageSharp `>= 3.2.0` и FluentAssertions `>= 8.0.0` игнорируются. PR #160–#184 закрыты: все они от 2026-10-11 и падают на restore (`NU1004` и/или `NU1902`/`NU1903` по ImageSharp 3.1.12, веток без подавления из #185). До тестов ни один не дошёл, поэтому xunit 4.0.1 не игнорируется. Свежий PR Dependabot против текущего `main` разбирается отдельно, по одному.

Цель была — снова оставить только `main` и перестать получать по пять одинаковых PR.

1. `.github/dependabot.yml`:
   - добавить `ignore: SixLabors.ImageSharp >= 4.0.0` во **все** NuGet-каталоги (Application, Domain, RevitPlugin, Cli), не только Infrastructure и TestCorpus. #168 пришёл из Application;
   - добавить `ignore: FluentAssertions >= 8.0.0` везде, где пакет есть;
   - по возможности свести NuGet-каталоги в один блок `directories:` с одной группой, чтобы одно обновление давало один PR.
2. Закрыть с комментарием (по одной строке причины):
   - #164, #168 — ImageSharp 4.x требует лицензионный ключ, Q-5 открыт;
   - #161, #179 — ImageSharp 3.2.0 не закрывает ни одну из 5 GHSA (исправлено только в 4.1.2), а принятое исключение в `check_dotnet_vulnerabilities.py` привязано к `3.1.12`. Вернуться, если владелец решит Q-5 иначе;
   - pip-PR #165, #166, #171, #172, #174 — их заменит D-2 (lock обновляется целиком, с хэшами).
3. Проверить по одному, каждый ждёт зелёного CI:
   - #167 / #170 System.Text.Json 10.0.12 — патч; влить один, второй закрыть или дать Dependabot пересобрать;
   - #169 / #176 Xbim.Essentials 6.1.605 — влить, только если зелёные `IFC validation` и `compare_tfm_hashes.py`; `ExportAsync_RepeatsTheBarIdentity` обязан пройти;
   - #160 / #178 Highs.Native 1.15.1 — влить, только если `ColumnGenerationBenchmarkPackTests` и эталоны не изменились;
   - xunit.runner.visualstudio 4.0.1 (#173, #175, #177, #180, #181) — признак беды: счётчик тестов в TRX меньше 423 или 0. Тогда закрыть все пять и добавить `ignore >= 4.0.0` с причиной;
   - Actions: #162 upload-sarif, #163 upload-artifact 7, #184 download-artifact 8, #182 setup-python 7, #183 sbom-action 0.24.3. Пины остаются полными SHA с комментарием версии. `lint-workflows` (actionlint 1.7.12) должен пройти. upload-artifact и download-artifact вливать вместе.
4. Приёмка: открытых PR 0, веток кроме `main` 0, `main` зелёный.

### 4.2 D-2. ML lock (SDK: Python 3.11 + pip-tools)

1. `ml/requirements.txt`: `Pillow>=12.3,<13`, `pytest>=9.0.3,<10`, остальные нижние границы — по §3. `ml/requirements.in` синхронно.
2. Перегенерировать lock тем путём, что в `docs/README.md` («lock-refresh path»): `.github/requirements/pip-tools.locked.txt`, затем `piptools compile --allow-unsafe --generate-hashes`. torch остаётся CPU-колесом (`+cpu`, индекс PyTorch CPU), как в r7 P0-1′.
3. numpy 2.x — отдельный шаг внутри задачи: поднимать, только если `pytest tests -q` и `validate_model_manifest.py` зелёные; иначе оставить 1.26.x и записать причину в PR.
4. Проверить локально: `python -m pip install --require-hashes -r ml/requirements.locked.txt`, `pytest tests -q`, `python ml/scripts/validate_model_manifest.py ml/models/MANIFEST.json`.
5. Приёмка: 0 открытых Dependabot alerts для `ml/`, `python-smoke` зелёный. Хэши записаны в lock — правило `ml/AGENTS.md`.

### 4.3 PR-12. Каталог без тихих потерь (MCP)

Файлы: `src/OpenRebar.Infrastructure/Catalog/FileSupplierCatalogLoader.cs`, тесты в существующий `tests/OpenRebar.Infrastructure.Tests/Catalog/FileSupplierCatalogLoaderTests.cs`.

1. CSV: заголовок обязателен и равен `LengthMm,PricePerTon,InStock` (без учёта регистра). Числа через `CultureInfo.InvariantCulture`, `NumberStyles.Float`. Пустые строки пропускаются; любая другая непарсящаяся строка → `SupplierCatalogLoadException` с номером строки.
2. CSV и JSON одинаково: `LengthMm` конечное и `> 0`; `PricePerTon` пустое или конечное `>= 0`; дубль длины → исключение. `InStock` — `true`/`false`, иначе исключение.
3. Тесты (по одному `[Fact]` или строке `[InlineData]` на правило): запятая как десятичный разделитель, `0`, `-6000`, `NaN`, `Infinity`, дубль, плохой `InStock`, мусорная строка с номером, корректный файл. Для культурозависимости — запуск под `ru-RU` (`CultureInfo.CurrentCulture` в `try/finally`).
4. Приёмка: CI зелёный, README +N.

### 4.4 RT-7. PNG: размер до декодирования (MCP)

Файл: `src/OpenRebar.Infrastructure/ImageProcessing/PngIsolineParser.cs`. Это же снижает риск принятых GHSA ImageSharp.

1. До `Image.LoadAsync` вызвать `Image.IdentifyAsync(filePath, ct)`; если `Width * Height > MaxImagePixels` (в `long`) — то же `InvalidIsolineFileException`, что сейчас.
2. Декодировать с `DecoderOptions { MaxFrames = 1 }`.
3. Проверку после загрузки оставить (файл мог смениться между вызовами).
4. Тест в `PngIsolineParserTests`: маленький PNG с большим заявленным размером не доходит до загрузки (сообщение об ошибке содержит размеры). Синтетический PNG генерировать в тесте через ImageSharp, не класть бинарник в репозиторий.

### 4.5 PR-11. DXF без тихих потерь (MCP)

Файл: `src/OpenRebar.Infrastructure/DxfProcessing/DxfIsolineParser.cs`.

1. Каждый ранний `return` в `CollectEntity` и `ExtractPolygonFromEntity` пишет причину в сессию: `noColor`, `colorNotInLegend`, `unsupportedEntity:<DxfEntityType>`. Ключи `unsupportedCurve`, `ignoredOpenPolylines`, `unsupportedBoundaryEdge` не переименовывать.
2. Ключи попадают в существующий `ignoredByReason` — новое поле схемы не нужно. Проверить в `GenerateReinforcementPipeline` (около строки 1109), какие причины поднимают предупреждение, и добавить туда `colorNotInLegend`.
3. Тесты в `DxfIsolineParserTests`: по одному на каждую новую причину.
4. Эталоны: `simple-slab` содержит одну полилинию и `unitsAssumedMillimetres`. Если `ignoredByReason` в эталоне изменился — объяснить в PR; если не изменился, так и написать.

### 4.6 C-1. Список проверок со статусом (SDK)

Цель — инвариант AGENTS.md становится видимым в отчёте.

1. Domain: `CheckStatus { Passed, Failed, NotEvaluated }` и `NormativeCheck { Id, Status, Reason, Clause? }`.
2. Отчёт: верхний ключ `checks` (массив). Минимальный набор:
   - `providedArea` — `Passed`, если `verification.status` начинается с `Passed`, иначе `Failed`; без верификации — `NotEvaluated`;
   - `crackWidth`, `deflection` — всегда `NotEvaluated`, `reason: "inputIsRequiredAreaOnly"`;
   - `transverseInLap` — `NotEvaluated`, `reason: "Q-2b"`, если в раскладке есть хотя бы один нахлёст; без нахлёстов — не писать.
3. Схема: `checks` в `properties`, `ExpectedTopLevelProperties`, `SchemaVersion` 2.1.0.
4. Эталоны: в `result.json` появится `checks` с тремя записями (нахлёстов в `simple-slab` нет). `schemaVersion` 2.0.0 → 2.1.0. Других изменений быть не должно.
5. README: в разделе ограничений одна фраза о том, что трещины и прогиб не проверяются и отчёт это показывает. EN и RU вместе.

### 4.7 RT-5. Доля стыков по сечению (SDK)

1. Вынести расчёт доли в одну функцию (Application): для слоя и направления — все стержни слоя, окно 1,3·l_l вокруг центра стыка, знаменатель — все стержни, пересекающие окно, а не только группа с теми же концами.
2. `ClearanceChecker` (`lapOverload`) и `LapPlanner.Allowed` используют её.
3. Если фактическая доля больше той, что взята для α в `AnchorageRules.LapSpliceShare`, — критическая диагностика `lapShareExceeded` с `BarId`. Новый коэффициент α не вводить: значения только из `tables.v3.json`.
4. Тесты в `ClearanceCheckerTests`: две группы с разными концами, стыки в одном окне, сумма > 50 % → `lapShareExceeded`; разнесённые на > 1,3·l_l → нет.

### 4.8 G-1. Косая плита (SDK) — сделано

1. Если контур плиты не прямоугольник по осям, а в `design-input` объявлены края `Supported`/`Continuous`: не строить `WorkingRectangle`, а дать критическую диагностику `edgesRequireRectangularSlab` (exit 3). Это безопасное умолчание до P1-7 r8 (края по рёбрам полигона). Сделано: `WorkingRectangle` бросает исключение, конвейер пишет критическую диагностику до раскладки.
2. Свободные края косой плиты — как сейчас.
3. Квантование косых рядов наружу на 10 мм (r5 §5.9) не должно выводить конец стержня за бетон: после квантования обрезать по рабочему контуру. Тест: косой край, конец стержня внутри контура с допуском 0,1 мм.

### 4.9 G-2. Эффективная высота по слоям (SDK) — сделано

1. Источник отметок — `Domain/Rules/LayerElevations.cs` (уже даёт 33/47/173/187 мм на тестах). Для нижних слоёв `d = h − z_axis`, для верхних `d = z_axis`, где `z_axis` — отметка оси. Сделано в `EffectiveDepths`. Поле необязательное, версия схемы остаётся 2.1.0. На simple-slab это `BottomX: 180`.
2. Отчёт: `slab.effectiveDepthByLayer: { "BottomX": …, … }` только для слоёв со стержнями. `effectiveDepthMm` оставить (обратная совместимость).
3. Эталон `simple-slab`: появится `BottomX: 180` (h 220 − c 30 − 20/2). Объяснить формулой.

### 4.10 PR-10a. IFC v2 (SDK)

Файл: `src/OpenRebar.Infrastructure/Export/XbimIfcExporter.cs`.

1. `IfcReinforcingBarType` на пару (диаметр, класс стали), стержни связаны через `IfcRelDefinesByType`.
2. Тело: `IfcSweptDiskSolid` по оси (`IfcPolyline` для прямого, `IfcCompositeCurve` для крюка из `HookCenterline`), `Radius = d/2`, без `InnerRadius`.
3. `ObjectPlacement` — `IfcLocalPlacement` относительно плиты; отметка оси из `LayerElevations`.
4. `GlobalId` и `Name` из #185 не менять: тест `ExportAsync_RepeatsTheBarIdentity` обязан пройти без правки.
5. Тесты в `XbimIfcExporterTests`: тип один на пару, у каждого стержня есть тело с радиусом d/2, размещение задано.
6. `IFC validation` и `compare_tfm_hashes.py` зелёные. Хэш IFC изменится — в PR перечислить, какие сущности добавлены.

### 4.11 PR-10b. IDS в CI (MCP)

1. `tools/ci/ids/openrebar.ids` (IDS 1.0): у `IfcReinforcingBar` есть `NominalDiameter`, тип, тело; у проекта единицы мм.
2. В `.github/workflows/ifc-validation.yml` шаг проверки примеров по IDS. Инструмент ставить из lock с хэшами; лицензию проверить до добавления (Dependency Review запрещает AGPL и SSPL). Пин action — полный SHA.
3. Сначала PR-10a, потом PR-10b: правила IDS должны проходить на новом IFC.

### 4.12 PR-17. Раскрой (SDK) — сделано

1. Алгоритм — best fit (§3). Переименовать класс в `BestFitDecreasingOptimizer` (и регистрацию в DI), `OptimizerId` в `best-fit-decreasing-v1`, `IntegerizationStrategy` в `GenerateReinforcementPipeline` (строка 1245) в `best-fit-decreasing`. Результат раскроя не меняется; меняются только строки provenance. В эталонах `simple-slab` основной оптимизатор `column-generation-relaxation-v2` — проверить, где имя FFD попадает в `result.json`, и объяснить каждую строку. Сделано: в DI класс не регистрировался, его создаёт column generation. В `result.json` simple-slab имени FFD нет. Строка 1245 — это аварийный упаковщик, он берёт первый подходящий хлыст, поэтому его метка остаётся `first-fit-decreasing`. У класса best-fit метка `best-fit-decreasing`.
2. `MinScrapLengthMm`: удалить из порта (нигде не используется). Вводить порог полезного остатка только по решению владельца.
3. `StackTrace` в отчёте → `stackTraceSha256` (SHA-256 строки стека); полный стек только в лог. Схема 2.1.0, эталон: поле `stackTrace: null` → `stackTraceSha256: null`.

### 4.13 RT-4. Два KPI раскроя — уже в отчёте

`wastePercent` и `estimatedCost`. Стоимость считается в `EstimatePurchasedStockCost`: масса закупленного хлыста в тоннах умножается на `PricePerTon`. Если у длины нет цены, поле `null`. В эталоне `simple-slab` оба уровня (`optimizationByDiameter` и `summary`) уже `estimatedCost: null`. Второе поле `purchaseCost` не добавлять.

### 4.14 RT-6. Представление нахлёста (SDK) — сделано

1. Профиль: `laps.representation: "inPlane" | "stacked"`, по умолчанию `inPlane` + `source: default`. Схема профиля и allow-list загрузчика. Сделано. `generic.json` поле не пишет, поэтому его хэш и эталон simple-slab не меняются.
2. В IFC (после PR-10a) и в Revit-плейсере (`#if REVIT_SDK`) второй кусок сдвигается на d в плоскости (`inPlane`) или по высоте (`stacked`), чтобы тела не пересекались.
3. Проверка `lapClearance` учитывает сдвиг. Тест на оба режима.

### 4.15 S-1. Supply chain (MCP)

1. `ml/osv-scanner.toml`: вместо общего списка — исключение на пакет и версию с причиной, владельцем, сроком и ссылкой. Сначала D-2: после обновления lock большая часть исключений не нужна.
2. `release.yml`: `actions/attest-sbom` (полный SHA), SBOM привязан к ZIP. Сделано: `v4.1.0` `c604332985a26aa8cf1bdc465b92731239ec6b9e`, субъект — ZIP, документ — SPDX.
3. ML endpoint: allowlist хостов и запрет редиректов на другой хост в C#-клиенте; тест на редирект. Сделано: по умолчанию loopback, `--ml-url` добавляет свой хост, редирект на другой хост отклоняется. OSV ещё ждёт D-2.

### 4.16 P3 (только владелец)

ЛИРА AS1–AS4 по реальному файлу; аудитор КЖ `openrebar verify --layout` (CSV с координатами, слои DXF; IFC вторым шагом); сравнение с целевой раскладкой (IoU, boundary-F, Δмасса, Δмарки, Δстыки). Без файла владельца не начинать.

## 5. Настройки репозитория (делает владелец)

Агент готовит, владелец включает. Команды для `gh` от имени владельца:

```bash
# защита main: обязательные проверки, без force-push и удаления
gh api -X PUT repos/KonkovDV/OpenRebar/branches/main/protection --input protection.json
# rulesets: запрет создания и удаления тегов v* кроме владельца
gh api -X POST repos/KonkovDV/OpenRebar/rulesets --input tag-ruleset.json
# environment release с обязательным ревьюером
gh api -X PUT repos/KonkovDV/OpenRebar/environments/release --input release-env.json
```

`protection.json`: `required_status_checks.strict = true`, `contexts` = проверки из правила 10, `enforce_admins = false`, `allow_force_pushes = false`, `allow_deletions = false`, `required_pull_request_reviews = null`. Обязательный ревьюер в PR не нужен: владелец один.

Если владелец хочет, чтобы и коммитер merge-коммита был Konkov Dmitrij, кнопка merge не подходит: она пишет коммитера `GitHub`. Вариант с SDK — `git merge --no-ff` локально с переменными `GIT_AUTHOR_*` / `GIT_COMMITTER_*` и обычный (не force) `git push origin main`; PR закроется как влитый. После включения защиты `main` прямой пуш потребует исключения для владельца. Решение — за владельцем (Q-18).

## 6. Стоп-вопросы владельцу

| # | Вопрос | Безопасное умолчание |
|---|---|---|
| Q-2b | Поперечная арматура в нахлёсте плит при α₂ = 1,2 | `NotEvaluated: transverseInLap` (C-1) |
| Q-5 | ImageSharp 4 (лицензионный ключ) или замена декодера | 3.1.12 + принятые 5 GHSA + RT-7 |
| Q-11 | Первичный текст п. 10.4.9 и 10.3.25 (скобы у свободного края) | скобы не генерировать, `remedy: uBarAtFreeEdge` |
| Q-12 | Первый пилот | аудитор КЖ |
| Q-15 | Rsc A400: 350 или 340 | 340 + `NotVerified` |
| RT-3 | Rbt B55/B60 (скан табл. 6.8) | не менять |
| — | Длины хвостов крюков | `tailDiameters: 0`, `source: default` |
| — | Порядок слоёв X/Y по высоте | X снаружи (умолчание `LayerElevations`) |
| — | Торцевой защитный слой по умолчанию | `max(coverEdgeMm, cover + d/2)` |
| Q-3 | Экспорт ЛИРА-САПР | только синтетика |
| — | Официальный текст Изм. № 1 к СП 63 (номер приказа) | копия ТН + пометка в PR |
| Q-18 | Коммитер merge-коммитов | кнопка merge (коммитер `GitHub`) |

Ответ владельца записывается строкой в `docs/agent/decisions/DECISIONS.md` (дата, вопрос, ответ, источник) отдельным PR до кода, который от него зависит.

## 7. Каждый PR: чек-лист

1. Ветка от свежего `main`: `fix/<task>` или `feat/<task>`.
2. Первый файл — `docs/agent/plans/YYYY-MM-DD-<task>.md`: цель, файлы, тесты, ожидаемые изменения эталонов.
3. Код + тесты в существующие классы.
4. `CHANGELOG.md` → `[Unreleased]` → `### Fixed` или `### Added`, одна строка простым английским.
5. Если число тестов изменилось — `README.md` и `README.ru.md` в одном коммите.
6. Если менялись эталоны — каждое число объяснено в описании PR.
7. Сообщение коммита: одно предложение на английском в повелительном или описательном наклонении, без префиксов вроде `feat:` и без упоминания ассистентов.
8. Проверить личность коммита (правило 7) и текст файлов (правило 1).
9. Дождаться всех проверок из правила 10, merge, удалить ветку, дождаться зелёного `main`.

## 8. Команды для агента с SDK

Перед пушем, по порядку (из `.github/workflows/ci.yml`, полный список в AGENTS.md):

```bash
dotnet restore OpenRebar.sln --locked-mode -p:EnableWindowsTargeting=true -p:Platform=x64
dotnet build OpenRebar.sln --no-restore --configuration Release -p:EnableWindowsTargeting=true -p:Platform=x64
dotnet format OpenRebar.sln --verify-no-changes --no-restore
dotnet test tests/OpenRebar.Domain.Tests/OpenRebar.Domain.Tests.csproj --no-build --configuration Release -f net8.0 --logger "trx;LogFileName=test-results.trx"
dotnet test tests/OpenRebar.Infrastructure.Tests/OpenRebar.Infrastructure.Tests.csproj --no-build --configuration Release -f net8.0 --logger "trx;LogFileName=test-results.trx"
dotnet test tests/OpenRebar.TestCorpus/OpenRebar.TestCorpus.csproj --no-build --configuration Release -f net8.0 --logger "trx;LogFileName=test-results.trx"
dotnet test tests/OpenRebar.Application.Tests/OpenRebar.Application.Tests.csproj --no-build --configuration Release -f net8.0 --logger "trx;LogFileName=test-results.trx"
dotnet test tests/OpenRebar.Cli.Tests/OpenRebar.Cli.Tests.csproj --no-build --configuration Release -f net10.0 --logger "trx;LogFileName=test-results.trx"
python tools/ci/verify_readme_regression_claim.py
python tools/ci/verify_agents_md.py
```

Эталоны перегенерировать только `tools/examples/generate_expected_outputs.ps1` (или `.sh`), не руками. `dotnet build` принимает один проект за вызов.

Частые ошибки компиляции в этом репозитории: `CS9175` (collection expression внутри expression tree FluentAssertions — писать явные сравнения), `CS8619` (nullable в кортеже — `?? ""`), `CS0101`/`CS0111` (дубль класса, правило 2), NuGet audit `NU1902`/`NU1903` (под `TreatWarningsAsErrors` это ошибка restore).

## 9. Стартовый промпт для агента

> Репозиторий `KonkovDV/OpenRebar`. Источник истины: код и тесты `main`, затем `docs/OPENREBAR_AGENT_PLAN_r10_2026_10_14.md`, затем r9…r5; `docs/agent/decisions/DECISIONS.md` сильнее плана. Прочитай AGENTS.md и r10 §0 целиком. Возьми первую незакрытую задачу из r10 §4, подходящую твоей среде (MCP или SDK). Одна задача — одна ветка — один PR. Не переписывай историю `main`, не делай force-push. Коммиты только от `Konkov Dmitrij <272916918+KonkovDV@users.noreply.github.com>`. Merge только при зелёных проверках на GitHub, после merge удали ветку. Если задача требует нормы без первоисточника — остановись и задай вопрос из r10 §6.
