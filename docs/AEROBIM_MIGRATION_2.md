# AeroBIM report 2.0.0

The canonical report `schemaVersion` is `2.0.0` and `contractId` is `OpenRebar.reinforcement.report.v2`.

What changed for a downstream reader:

- `layers` always has four objects, in order `BottomX`, `BottomY`, `TopX`, `TopY`. A layer with no drawing has `status` `NotProvided`. A layer that was read has `status` `Provided`, plus background, classes, zone ids, positions, and installed mass.
- `requiresEngineerReview` is always `true`.
- `profile` is `{id, version, sha256}`. Without a company profile file the id is `unspecified` and the hash is null.
- `inputSource` is `{adapterId, version, sha256}` for the drawing that was read.
- `normativeProfile` keeps its existing fields and also publishes `id` and `version`.
- The schedule CSV is split into `[BottomX]`, `[BottomY]`, `[TopX]`, `[TopY]`, then `[Расход стали]`. An empty layer is the line `status;NotProvided`. A position row is `Позиция;Обозначение;Наименование;Кол.;Масса ед., кг;Примечание`. The name is `Ø12 A500C l = 2450`. Обозначение is empty. The note is the internal shape, `форма H`. The steel sheet is `Класс стали;Диаметр, мм;Масса, кг`.
- The AeroBIM summary (`*.aerobim.json`) uses `$schema` `aerobim-OpenRebar-reinforcement-report/v2` and lists each layer's status and mass.

Zones, cutting plans, and the previous top-level fields are still present.

`parameterSources` lists the values that were actually used and whether each one came from the company profile, the project input, or a built-in default.
