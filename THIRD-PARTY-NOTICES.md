# Third-party notices

Components shipped with or used by this project, with their licences. Reference projects that
were only studied for behaviour (and whose code is **not** copied) are listed separately.

## Runtime dependencies

| Component | Used by | Licence |
|---|---|---|
| [Clipper2](https://github.com/AngusJohnson/Clipper2) (NuGet `Clipper2`) | Embroidery.Geometry — polygon union/offset | Boost Software License 1.0 |
| [React](https://react.dev), React DOM | web/embroidery-editor | MIT |

## Build and test tools

Vite, @vitejs/plugin-react, TypeScript, Vitest (MIT / Apache-2.0); xunit, Microsoft.NET.Test.Sdk (Apache-2.0 / MIT).

## Behaviour references (no code copied)

| Project | Licence | How it is used |
|---|---|---|
| Ink/Stitch | GPL-3.0 | Documented satin/fill behaviour and the SVG satin-column convention (two rail subpaths + rung subpaths) are followed as a file-format convention. No source is ported. |
| pystitch / pyembroidery | MIT | DST record layout cross-checked against its documented encoding. |
| bastidor | MIT | Tatami connector probe reproduced as a regression test. |
| PEmbroider | GPL-3.0 + ACSL | Not used. |
| satin-studio (Frankyface) | no licence file | Not used; concepts only. |
