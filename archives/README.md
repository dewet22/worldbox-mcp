# Archives, worldbox-mcp

> Nothing in here is a source of truth. This directory keeps what used to be true and is not
> any more. Do not read it when picking up a session, and never cite it from a live document.
> Current documentation lives in `docs/` and in the three files at the root.

## Rules

**No silent deletion.** A document that is retired moves here, it is never erased.

**Location**: `archives/<YYYY-MM>/<original path>`. The dated bucket avoids collisions over
time, the preserved path makes the provenance readable at a glance.

**Move with `git mv`**, so the file keeps its history.

**Required header** at the top of an archived file:

```markdown
> **ARCHIVED** — YYYY-MM-DD
> Origin: `<original path>`
> Reason: <why it stopped being true>
> Replaced by: `<path>` or "nothing"
```

Then one line in the index below.

**What does not come here.** A wrong section inside a file that is still useful gets fixed in
place. A durable rule in `CLAUDE.md` that has become false is deleted outright: this is not a
bin for one-off mistakes, and a wrong rule is worse than a missing one.

## Index

Nothing archived yet. The v0.4.0 close-out on 2026-09-05 found three false statements in
`CLAUDE.md` (a stale `v0.3.1` status snapshot, an out-of-date "recently shipped" line, and a
machine-specific Windows path to the memory directory). All three were corrections inside a file
that is still current, so they were fixed in place rather than moved here, which is what the
rules above prescribe.
