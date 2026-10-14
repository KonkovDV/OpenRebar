# Plan: reject an oversized PNG before decode — 2026-10-14

## Context

r10 RT-7. The pixel budget was checked after `Image.LoadAsync`, so a header
that claims a huge frame was decoded first. The five accepted ImageSharp
advisories stay on 3.1.12; this does not pretend to patch them.

## Changes

- `Image.IdentifyAsync` runs first. A frame whose width times height exceeds
  25_000_000 pixels throws the same `InvalidIsolineFileException` as before.
- Decode uses `DecoderOptions` with `MaxFrames = 1`.
- The check after load stays, in case the file changes between the two reads.

## Acceptance

One parser fact builds a PNG header that claims 100000x100000 and expects that
size in the error. Existing PNG tests stay green. Examples are unchanged.
