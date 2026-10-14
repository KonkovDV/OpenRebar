# Plan: IFC bar type, body, and IDS — 2026-10-14

## Context

r10 PR-10a and PR-10b, and SOTA rows IFC-D1 and IFC-D2. A bar was only a
property set. IDS validation had to stay off IfcOpenShell: that library is
LGPL-3.0, and this check does not add a package.

## Changes

- One `IfcReinforcingBarType` per diameter and steel class, linked by
  `IfcRelDefinesByType`.
- Body is `IfcSweptDiskSolid` of radius d/2, with no inner radius. A straight
  bar uses a polyline. A hook uses a composite curve from `HookCenterline`.
- `IfcLocalPlacement` sits on the bar start. The axis height comes from
  `LayerElevations`.
- `Name` and `GlobalId` are unchanged.
- `tools/ci/ids/openrebar.ids` names the entities. CI checks those names, and
  the metre unit, in each generated example. simple-slab result figures do
  not change. The IFC bytes do.

## Acceptance

Exporter facts: one type for two equal bars, a solid of radius 0.006 m, a
placement, and a composite curve for a hook. The identity fact is unchanged.
The simple-slab export contains the IDS entities.
