# Things learned

## Two apps sharing a file must share its whole schema (2026-11-13)

The Linux model had no `HostDetails`, so a Windows configuration opened and saved on Linux silently lost every host's priority, disabled state, snooze and timing. Nothing failed; the data was just gone. Linux 1.16.0 reads and writes the Windows shape, and `--self-test` imports a real Windows-shaped file and checks every option survives a save and reload.

Rule: when two programs read and write one file, either share the model or keep unknown fields, and test with a file written by the other program (CODING 8.10 is the fixture half of this).

Applies to: every project. Harvested: no.

## A package cache makes CI-only projects testable locally (2026-11-13)

NuGet is blocked in the agent sandbox, so the Linux app could only be built in CI (CODING 8.8). A temporary branch ran `dotnet restore` in Actions and attached the package cache to a draft release; release assets are reachable from the sandbox, artifacts aren't. With the cache unpacked, the app built and its Xvfb suite ran locally in seconds, which turned a 10-minute CI loop into a 1-minute one. The draft release and branch were deleted afterwards.

Applies to: every project whose dependencies come from a blocked registry. Harvested: no.

## Linux packaging traps (2026-11-13)

- `rpmbuild` strips binaries by default, which corrupts a .NET single-file bundle. The spec turns off `__os_install_post`, `__strip` and debuginfo, and CI compares the packaged binary byte-for-byte with the tested one.
- GitHub's Ubuntu 24.04 runners block the unprivileged user namespaces `flatpak-builder` needs; `sysctl kernel.apparmor_restrict_unprivileged_userns=0` first.
- Avalonia: a static class named `Theme` is shadowed by `StyledElement.Theme` inside every control, and `Screens.ScreenFromWindow` throws once a window is closed (read it in `Closing`).

Applies to: this project and any Linux .NET packaging. Harvested: no.

## Measure complete text before fitting a topology (2026-11-13)

A marker/hit-region count can pass while the renderer hides every address. The #43 regression reproduced 0 of 68 addresses despite the host nodes existing. Check drawn captions as well as node bounds, reproduce against the previous renderer, and inspect screenshots.

Uniform site-row heights wasted space when one site had many endpoints. Give busy rows the height they need before scaling; transform hit regions with the drawing. Review caught excessive shrinking that passing tests alone missed.

Scope: this project's topology renderer. Evidence: PR #45 and Windows validation 43212934329. No cross-project standard change required.
