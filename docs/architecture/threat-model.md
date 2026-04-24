# Stagehand Threat Model

This is the engineer-facing threat-model document. The public-facing reporting contact and embargo
policy live in [`SECURITY.md`](../../SECURITY.md) at the repository root; this file covers the
attack surface, mitigations, and supply-chain controls in enough detail to drive implementation
decisions.

## Threat model summary

Stagehand is a single-process WPF application that runs unprivileged at the signed-in user's level.
It installs no kernel driver, no service, and no elevation shim. It does not inject DLLs, hook
foreign processes, or load unsigned code. Everything it does is a call to a public Win32 or DWM
API that the user's session already exposes.

- **Attacker capability assumed:** another process running in the same user session. An attacker
  cannot use Stagehand as a privilege-escalation vector, because Stagehand has no more privilege
  than the attacker already does.
- **Assets protected:** the user's settings file (`%APPDATA%\Stagehand\settings.json`), local log
  files (`%LOCALAPPDATA%\Stagehand\logs\`), update binaries, and the visible integrity of the
  desktop (no windows stranded off-screen, no permanently shrunk Work Area).
- **Non-assets:** window metadata that the app reads (title, class, PID, bounds) is already
  available to any process on the session via public APIs — Stagehand does not widen that exposure.
  Network traffic is likewise a non-asset because the only endpoint serves public release
  artefacts.

## Network surface

The only outbound network call Stagehand makes is to the update channel. The update URL is a
compile-time constant pinned to the official GitHub Releases endpoint of this repository; it is
overridable only via a debug-build environment variable that is not set in release artefacts.
Velopack verifies Authenticode signatures on every downloaded package before applying an update; an
unsigned or mis-signed package aborts the update.

No metrics, crash dumps, or analytics leave the machine. There is no inbound socket.

## Supply chain

- Every NuGet version is pinned in `Directory.Packages.props` with central package management
  enabled.
- `packages.lock.json` is committed per project; CI restores with `--locked-mode` so a drift
  between lockfile and manifest fails the build.
- Dependabot opens weekly update PRs for both NuGet and GitHub Actions.
- `dotnet list package --vulnerable --include-transitive` runs on every CI build; any Critical or
  High advisory fails the pipeline.
- A CycloneDX SBOM is generated in the release workflow (enabled in Plan 05) and attached to each
  release for downstream consumers.

## Elevation boundary

Stagehand refuses to park elevated windows. `WindowController.Park` raises
`ElevationBoundaryException` when the target HWND belongs to a process at a higher integrity level;
Plans 02–03 surface that state in the sidebar overlay rather than silently skipping the window, so
the user can see why the window was not taken.

Running Stagehand itself as administrator is off by default. A launch-time check refuses to start
an elevated process unless the user has opted in through Settings (a persisted acknowledgement so
subsequent elevated launches can skip the refusal, with `--allow-elevated` passed on the shortcut
target). The trade-off is real: elevation lets Stagehand control more windows — Task Manager,
Registry Editor — but it also widens the blast radius of any bug. Off by default is the right
default.

## Telemetry posture

None, by default. Stagehand does not collect, aggregate, or transmit usage data, and no crash dumps
leave the machine. Exceptions go to the local log sink only. If telemetry is ever introduced, it
must be opt-in, anonymised, described in a dedicated plan, and documented in both this file and
`SECURITY.md` before release.

## Responsible disclosure

Vulnerability reports go through GitHub's Private Vulnerability Reporting on the repository's
Security tab (public link published in [`SECURITY.md`](../../SECURITY.md)). The default embargo is
90 days, measured from acknowledgement, with the acknowledgement/triage/ship milestones documented
in `SECURITY.md`. Shorter disclosure is possible if the fix lands earlier and the reporter agrees;
extensions past 90 days are negotiated with the reporter.

## Code signing

Release artefacts are Authenticode-signed via SignPath.io, the free OSS signing path. The release
workflow reads `SIGNING_PFX_BASE64` and `SIGNING_PFX_PASSWORD` from repository secrets; the private
key never lives in source control. Unsigned builds are permitted for local development but log a
`Warning` on startup so a developer machine is always distinguishable from a release install. The
full signing workflow (key rotation, reproducibility, release attestation) is implemented in
Plan 05.
