# `.claude/` skill scaffold (staging)

This folder holds the **initial** repo-local skills for AI agents working on
Aireq. They live here in `tools/claude-skills-staging/` only because Cowork
sandboxes can't write to `.claude/` directly — the install step below moves
them into `.claude/skills/` where Claude Code and Cowork actually discover
them.

## Install (one-time)

```bash
mkdir -p .claude
mv tools/claude-skills-staging/skills .claude/skills
# this README too, so the .claude/ folder is self-documenting:
mv tools/claude-skills-staging/README.md .claude/README.md
rmdir tools/claude-skills-staging
git add .claude/ CLAUDE.md
git rm -rf tools/claude-skills-staging 2>/dev/null || true
```

Then commit as part of the AIR0007 PR.

## What's in here

| Skill | Use when |
|---|---|
| `aireq-verify` | User wants pre-commit verification (build + tests + typecheck + lint + web build). |
| `aireq-new-story` | User wants to start a new story branch with the right name + base. |

## Adding more skills

A skill is a single markdown file (`SKILL.md`) inside a folder named for the
skill. YAML frontmatter (`name`, `description`) drives when it triggers. Keep
descriptions precise — vague triggers cause false fires.

```
.claude/skills/<name>/SKILL.md
```

## Related

- [`CLAUDE.md`](../../CLAUDE.md) at the repo root — codebase context every AI agent reads first.
- [`memory.md`](../../memory.md) — durable decision log (append-only).
- [`PLAN.md`](../../PLAN.md) — execution plan (story IDs, dependencies).
