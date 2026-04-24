# Security Policy

Stagehand is a single-process desktop application for Windows. It runs at the signed-in user's privilege level and manages windows via public Win32 and DWM APIs. This document describes the security posture you can expect from a release build, and how to report a vulnerability.

For engineering-level detail (attack surface, supply chain controls, code-signing path), see [`docs/architecture/threat-model.md`](docs/architecture/threat-model.md).

## Supported versions

Only the latest minor release line of Stagehand receives security updates.

| Version         | Supported |
| --------------- | --------- |
| `0.x` (current) | Yes       |
| older           | No        |

## Threat model summary

- **Attacker capability:** another process running in the same user session. Stagehand does not escalate privileges, so it cannot be used as a lateral-movement tool beyond what the attacker already has.
- **Assets protected:** the user's settings file (`%APPDATA%\Stagehand\settings.json`), the local log files (`%LOCALAPPDATA%\Stagehand\logs\`), the update binaries the app downloads, and the integrity of the on-screen desktop state.
- **Non-assets:** network traffic. The only network endpoint is the update channel, which serves public release artefacts.

## Network posture

Stagehand does not make network calls during normal window management. The only outbound network access is the update channel, which is pinned at compile time to the official GitHub Releases URL of this repository. The update client verifies Authenticode signatures on every downloaded package before applying it.

No crash dumps, metrics, or analytics are transmitted.

## Telemetry

None, by default. Stagehand does not collect, aggregate, or transmit usage data. If telemetry is ever introduced, it must be opt-in, anonymised, described in a dedicated plan, and documented here before release.

## Elevation boundary

**Stagehand refuses to park elevated windows.** An unelevated Stagehand process cannot manipulate windows owned by elevated processes (Task Manager, Registry Editor, the UAC dialog itself). When the app encounters such a window it raises `ElevationBoundaryException` in the interop layer and surfaces a visible indicator in the sidebar overlay rather than silently skipping the window.

Running Stagehand itself as administrator is off by default and discouraged. A launch-time check refuses to start an elevated process unless the user has opted in via Settings (a persisted acknowledgement that subsequent elevated launches can rely on). Opting in expands what Stagehand can control, but it also increases the blast radius of any bug — treat the trade-off accordingly.

## Supply chain

- Every NuGet version is pinned in `Directory.Packages.props`.
- `packages.lock.json` is committed per project; CI restores in `--locked-mode`.
- Dependabot opens weekly update PRs for NuGet and GitHub Actions.
- `dotnet list package --vulnerable --include-transitive` runs on every CI build; any Critical or High advisory fails the build.
- A CycloneDX SBOM is generated in the release workflow and attached to each release (enabled in Plan 05).

## Reporting a vulnerability

**Please do not report security vulnerabilities through public GitHub issues.**

Use one of the private channels below:

1. **GitHub Security Advisories** (preferred) — open a report at <https://github.com/SoftVentures/Stagehand/security/advisories/new>. This keeps the conversation private and lets us coordinate a fix and disclosure.
2. **Email** — send details to **<softventures.orga+legal@gmail.com>**. A PGP key is available on request.

Include, where possible:

- A concise description of the issue and its impact.
- A reproduction — a minimal set of steps, a sample build, or a short script.
- The Stagehand version and Windows build on which you observed the issue.
- Your preferred contact and credit preference.

## What to expect

- **Acknowledgement** within **5 working days** of receipt.
- An **initial assessment** (severity, plan) within **10 working days**.
- For **High / Critical** issues we aim for a fix and coordinated disclosure within **30 days**.
- Lower-severity issues are scheduled into the next regular release.

## Embargo and disclosure timeline

Stagehand honours a **90-day embargo by default**, measured from the day we acknowledge the report. If a fix lands earlier and has been adopted widely, we may disclose sooner with the reporter's consent. If the fix requires a larger change, we may request an extension — with the reporter's agreement — beyond the 90 days.

## Safe harbor

We will not pursue legal action against good-faith researchers who:

- Make a reasonable effort to avoid privacy violations and data destruction.
- Give us a reasonable chance to address the issue before public disclosure.
- Do not exploit the issue beyond what is necessary to demonstrate it.

Thank you for helping keep Stagehand and its users safe.

## Scope

In scope:

- Defects in Stagehand source (`app/`, `tests/`) that allow an attacker to gain capabilities they do not already have on the local session.
- Weaknesses in the update channel, signing pipeline, or build reproducibility.
- Issues that allow Stagehand to leave the desktop in an unrecoverable state (windows parked off-screen, Work Area permanently shrunk).

Out of scope:

- Bugs in Microsoft-supplied Win32 or DWM APIs.
- Issues that require an attacker to already be running as an administrator in the same session.
- Feature requests framed as security issues — use the Feature issue template instead.
