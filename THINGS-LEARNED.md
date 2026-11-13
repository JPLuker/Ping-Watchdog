# Things learned

## Measure complete text before fitting a topology (2026-11-13)

A marker/hit-region count can pass while the renderer hides every address. The #43 regression reproduced 0 of 68 addresses despite the host nodes existing. Check drawn captions as well as node bounds, reproduce against the previous renderer, and inspect screenshots.

Uniform site-row heights wasted space when one site had many endpoints. Give busy rows the height they need before scaling; transform hit regions with the drawing. Review caught excessive shrinking that passing tests alone missed.

Scope: this project's topology renderer. Evidence: PR #45 and Windows validation 43212934329. No cross-project standard change required.
