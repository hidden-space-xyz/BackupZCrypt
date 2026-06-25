# `.claude/` — Claude Code harness

Configuration that shapes how Claude Code (and any [Agent Skills](https://agentskills.io)-compatible
tool) works in this repository. Everything here **except `settings.local.json`** is shared and
version-controlled. The always-in-context project guidance lives in [`../CLAUDE.md`](../CLAUDE.md).

## Layout

| Path | What it is |
|---|---|
| `settings.json` | Shared, committed settings — the permission policy. Applies to everyone. |
| `settings.local.json` | **Personal, git-ignored** overrides. Never commit it. |
| `skills/` | Knowledge skills that load on demand when their description matches the task. |
| `agents/` | Specialized review subagents that run in an isolated context window. |

## Permission policy (`settings.json`)

- **`defaultMode: "ask"`** — anything not explicitly allowed prompts for confirmation. This is the
  real safety backstop for a security-critical, data-loss-sensitive project.
- **`allow`** — the non-destructive dev loop runs without prompting: `dotnet build/test/format/run/
  restore/list` and read-only git (`status`, `diff`, `log`, `show`, `branch`). Each is listed for
  both the **PowerShell** and **Bash** tools, because this repo is PowerShell-primary while the docs
  use bash-style commands.
- **`deny`** — irreversible commands are blocked outright (`rm -rf`, `git push --force`,
  `git reset --hard`, `git clean -f`, `Remove-Item -Recurse -Force`). `deny` is best-effort; the
  `ask` default is the true backstop.

Personal tweaks (e.g. allowing an extra command on your machine) go in `settings.local.json`. It is
git-ignored, so it never affects teammates. See the
[settings reference](https://code.claude.com/docs/en/settings).

## Skills (`skills/`)

Load automatically when their description matches your task; invoke explicitly with `/<name>`. Their
bodies stay out of context until used.

| Skill | Fires when… |
|---|---|
| `clean-architecture` | adding/moving a type, choosing a layer, wiring DI, reviewing dependency direction |
| `clean-code` | writing/refactoring C#, fixing analyzer warnings, updating NuGet packages |
| `documentation` | changing a non-`private` member, or shipping a user-visible change |
| `testing` | adding/changing code that needs tests, or checking coverage |
| `localization` | touching user-facing text or adding a `MessageCode` |
| `security-and-performance` | touching crypto, key handling, I/O, or any hot path |

## Subagents (`agents/`)

Delegated automatically when a task matches their description; they work in their own context window
and return only a report, keeping search/audit noise out of the main conversation.

| Subagent | Use it to… | Tools | Model |
|---|---|---|---|
| `security-reviewer` | audit a crypto/key/I/O change against the security checklist | Read, Grep, Glob, Bash | opus |
| `architecture-reviewer` | verify Clean Architecture boundaries after structural changes | Read, Grep, Glob | sonnet |

## Conventions

This harness encodes the project's two non-negotiable priorities — **Security → Performance →
everything else** — and the Definition of Done in [`../CLAUDE.md`](../CLAUDE.md). Keep skill bodies
concise (they are a recurring token cost) and keep `settings.json` free of personal or
machine-specific values.
