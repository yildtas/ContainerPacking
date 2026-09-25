# Third-party notices

Components shipped with or used by this project, with their licences. Reference projects that
were only studied for behaviour (and whose code is **not** copied) are listed separately.

## Runtime dependencies

| Component | Used by | Licence |
|---|---|---|
| [Clipper2](https://github.com/AngusJohnson/Clipper2) (NuGet `Clipper2`) | Embroidery.Geometry — polygon union/offset | Boost Software License 1.0 |
| [React](https://react.dev), React DOM | web/embroidery-editor | MIT |

## Ported code

| Component | Licence | What was ported |
|---|---|---|
| [pyembroidery](https://github.com/EmbroidePy/pyembroidery) 1.5.1 | MIT | `src/Embroidery.Formats/Home/`: the PES (truncated v1 + PEC), JEF and EXP writers, the PEC/JEF thread charts, the PEC thumbnail frame and the red-mean colour distance. Output was verified by reading it back with pyembroidery. |

pyembroidery licence text:

```
MIT License

Copyright (c) 2018

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## External programs (separate processes, not linked)

Ink/Stitch (GPL-3.0) and Inkscape (GPL-2.0+) can be configured as comparison backends; they run
as separate processes and none of their code is included. Wilcom EWA is a commercial web service
used only with the user's own account.

## Build and test tools

Vite, @vitejs/plugin-react, TypeScript, Vitest (MIT / Apache-2.0); xunit, Microsoft.NET.Test.Sdk (Apache-2.0 / MIT).

## Behaviour references (no code copied)

| Project | Licence | How it is used |
|---|---|---|
| Ink/Stitch | GPL-3.0 | Documented satin/fill behaviour and the SVG satin-column convention (two rail subpaths + rung subpaths) are followed as a file-format convention. No source is ported. |
| pystitch / pyembroidery | MIT | DST record layout cross-checked against its documented encoding (the home formats above are ported). |
| bastidor | MIT | Tatami connector probe reproduced as a regression test. |
| PEmbroider | GPL-3.0 + ACSL | Not used. |
| satin-studio (Frankyface) | no licence file | Not used; concepts only. |
