# Manual Tests

Some behaviour in Stagehand is hard to cover from an automated suite — monitor hot-plug, DWM
thumbnail painting, UAC dialog interaction, full-screen game pause, and the feel of the overlay
animation all resist cheap automation. We document them as human-run checklists here instead, and
keep automated xUnit coverage focused on logic that doesn't touch the compositor.

## Convention

- Each execution plan (`docs/plans/02-*.md` through `docs/plans/05-*.md`) may add a manual-test
  checklist to this folder whenever automated coverage cannot reach the user-visible behaviour.
- Checklists live at `docs/manual-tests/<short-name>.md` — for example,
  `docs/manual-tests/stage-lifecycle.md` for the enable/disable flow. Prefer a kebab-case noun
  phrase that matches the feature the plan ships.
- Every checklist starts with an **Environment** header listing the operating system build, the
  monitor count and layout, the DPI scaling per monitor, and the display language/locale the run
  targets. Checklists that depend on specific hardware (an elevated Task Manager window, a
  full-screen game) note that requirement too.
- Steps are numbered. Each step states the action and the expected outcome on the same line or as
  a pair of sub-bullets (`- Do X.` / `- Expect Y.`). Steps should be independent enough that a
  reviewer can stop partway through and resume without restarting the environment.

## When to run

Contributors run the relevant checklist locally before opening a pull request that touches the UX
it covers. Reviewers may re-run a checklist when the change is risky, but they are not expected to
on every PR. **CI does not run manual checklists** — they exist precisely because CI cannot.

## Index

No checklists exist yet. Plans 02–05 add entries here as their features land.
