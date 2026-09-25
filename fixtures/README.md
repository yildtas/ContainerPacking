# Golden fixtures

Reference designs used to measure our output against professionally digitized files.

- Customer designs are **not committed**. Put them under `fixtures/private/` (ignored by git).
- Per fixture keep: `source.svg` (our vector input), `reference.dst` (the professional file),
  `settings.json` (what the reference was made with, if known) and sew-out photos.

Measure a reference and compare a candidate:

```bash
dotnet run --project src/Embroidery.Tools -- analyze fixtures/private/FER-7/reference.dst
dotnet run --project src/Embroidery.Tools -- compare fixtures/private/FER-7/reference.dst fixtures/private/FER-7/source.svg
```

Metrics marked *inferred* (satin width, same-rail spacing, running share, trims) are
interpretations of a raw stream; DST has no object, satin or trim records.

## FER-7 ÖN (reference, 2026-09-25)

Front panel, gold satin scrollwork, single colour. Measured with `analyze`:

| Metric | Value |
|---|---|
| Stitches / jumps / inferred trims | 72,336 / 51 / 11 |
| Size | 288.3 × 504.6 mm (larger than any single standard hoop) |
| Satin throw (column width), p25 / p50 / p75 | 3.50 / 3.97 / 4.29 mm |
| Same-rail spacing, p25 / p50 / p75 | 0.22 / 0.30 / 0.36 mm |
| Running stitches (underlay/travel/cut line) | 11.4 % |
| Thread path | 258 m |
