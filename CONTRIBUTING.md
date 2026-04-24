# Contributing to Stagehand

Thanks for your interest. This guide covers everything you need to set up a local build, submit changes, and understand the review bar Stagehand maintains. If anything here is unclear, open a `question` issue.

Stagehand is a single-process .NET 8 WPF app for Windows. There is no server component, no telemetry, and no database — so "getting set up" really does mean "install the SDK and open the solution."

## Local development setup

You need:

- **Windows 10 21H2 (build 19044) or Windows 11 22H2+** — this is the OS baseline Stagehand targets.
- **.NET 8 SDK** (`8.0.x`). Install via `winget install Microsoft.DotNet.SDK.8` or from <https://dotnet.microsoft.com/download>.
- **An IDE**, either:
  - **Visual Studio 2022 17.8+** with the ".NET desktop development" workload, or
  - **JetBrains Rider 2024.1+**.
- **Node.js 24+** (latest active LTS "Krypton") — only for Prettier, which formats Markdown, YAML, and JSON. Install via `winget install OpenJS.NodeJS.LTS`.

After cloning:

```powershell
# Restore local .NET tools (CSharpier, XAML Styler).
dotnet tool restore

# Restore the Prettier devDependency.
npm ci

# Build and run the tests.
dotnet build
dotnet test

# Launch the app.
dotnet run --project app/src/App.Shell
```

The solution file is `App.sln` at the repo root. All source lives under `app/src/`, all tests under `tests/`.

## Formatter toolchain

Stagehand uses four opinionated formatters. All four run as checks in CI; none of them require debate in code review.

| Formatter         | Scope                    | Fix command                                  | CI check command                                       |
| ----------------- | ------------------------ | -------------------------------------------- | ------------------------------------------------------ |
| **Prettier**      | Markdown, YAML, JSON     | `npx prettier --write .`                     | `npx prettier --check .`                               |
| **CSharpier**     | C# layout                | `dotnet csharpier .`                         | `dotnet csharpier --check .`                           |
| **XAML Styler**   | XAML                     | `dotnet xstyler --recursive --directory app` | `dotnet xstyler --recursive --directory app --passive` |
| **dotnet format** | C# whitespace, `.csproj` | `dotnet format`                              | `dotnet format --verify-no-changes`                    |

Run all four before pushing. If you prefer local automation, you can wire these commands into a pre-commit hook of your choice (e.g. Husky, lefthook, a plain `.git/hooks/pre-commit` script). Stagehand does not prescribe or install a specific hook tool — the CI gates are the source of truth.

## Branching policy

Trunk-based development. `main` is always releasable.

- Branch names: `feat/<topic>`, `fix/<topic>`, `docs/<topic>`, `refactor/<topic>`, `chore/<topic>`.
- Short-lived: under a week preferred. If a branch must live longer, rebase it onto `main` frequently.
- Force-push to your feature branch is fine; force-push to `main` is not.

## Pull request expectations

Every PR must:

1. **Link an issue** (or clearly state why there is no issue for small changes).
2. **Pass CI.** The pipeline runs all four formatter gates, build, test, and `dotnet list package --vulnerable`.
3. **Get at least one approving review** from a maintainer or code owner.
4. **Fill out the PR template** — type of change, checklist, screenshots for UI changes.
5. **Use a Conventional Commits title**, because release notes are generated from the commit log.

Squash-merge is the default. The squash commit message is written by the merger and should remain Conventional Commits-compliant.

## Commit convention

Stagehand follows [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/). Release tooling (Plan 05) parses the commit log to generate release notes.

Allowed types:

- `feat:` — user-visible feature.
- `fix:` — user-visible bug fix.
- `docs:` — documentation only.
- `chore:` — tooling, dependencies, repo hygiene.
- `refactor:` — code change that neither fixes a bug nor adds a feature.
- `test:` — adding or adjusting tests.
- `ci:` — CI / workflow changes.
- `perf:` — performance improvement.

Scope is optional but encouraged: `feat(overlay): add sidebar fade-in`. Wrap the body at 72 characters.

A breaking change is marked with a `!` after the type or a `BREAKING CHANGE:` footer — e.g. `feat(settings)!: drop v0 schema support`.

## Nullable reference types

Nullable is enabled project-wide. A `!` null-forgiving suppression always carries a `// reason:` comment on the same line explaining why the value is guaranteed non-null at that point. Suppressions without a justification get flagged in review.

## Code of conduct

This project follows the [Contributor Covenant Code of Conduct](./CODE_OF_CONDUCT.md). By participating, you agree to uphold it. Report unacceptable behavior to **<softventures.orga@gmail.com>**.

## Security

If you believe you have found a vulnerability, please **do not open a public issue**. Follow the disclosure process in [`SECURITY.md`](SECURITY.md), which uses GitHub's Private Vulnerability Reporting flow and honours a 90-day embargo by default.

## Where to go next

- Architecture overview: [`docs/architecture/overview.md`](docs/architecture/overview.md).
- Threat model: [`docs/architecture/threat-model.md`](docs/architecture/threat-model.md).
- Manual test checklists: [`docs/manual-tests/README.md`](docs/manual-tests/README.md).
- Implementation plans (living briefs for each phase of work): [`docs/plans/`](docs/plans/).
