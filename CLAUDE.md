# CLAUDE.md

Guidance for AI agents working in this repository. This file is **always in context** — it is
deliberately short. Detailed, step-by-step procedures live in **Skills** (see below) and load on
demand, so read this first and let the relevant skill load when you act.

---

## 1. Project

**BackupZCrypt** is a cross-platform, chunk-based **encrypted backup** desktop application
(.NET 10, Avalonia UI / MVVM). It splits files into content-defined chunks (FastCDC), compresses
them optionally (Zstandard), and encrypts each chunk with authenticated encryption (AES-GCM,
ChaCha20-Poly1305, Twofish/Serpent/Camellia-GCM) under a key derived from the user's password
(Argon2id / Scrypt / PBKDF2).

> This is a **security-critical** application. It handles passwords, key material, and the only
> copy of a user's encrypted data. **There is no password recovery.** Treat every change as
> security-sensitive and assume mistakes can cause irreversible data loss.

---

## 2. The two non-negotiable priorities — in this order, ALWAYS

1. **Security first.** Never weaken a cryptographic guarantee, leak key material, or trade safety
   for convenience. When unsure, choose the safer option and say so.
2. **Performance second.** This tool processes large datasets; favor streaming, spans, and
   bounded memory. Optimize only without compromising priority #1.

Everything else (clean code, architecture, docs, tests, localization) is mandatory too, but when a
genuine trade-off forces a choice, resolve it in the order **Security → Performance → the rest**.
Never ship code that is incorrect, untested, or that breaks the build to satisfy any priority.

---

## 3. Architecture — Clean Architecture + DDD

Dependencies point **inward only**. The Domain knows nothing about the outer layers.

```
            ┌─────────────┐
 Desktop ──>│             │
            │ Composition │──> Application ──> Domain
 Infra ────>│             │                      ▲
            └─────────────┘  Infrastructure ─────┘
```

| Project | Role | May reference |
|---|---|---|
| `BackupZCrypt.Domain` | Enums, constants, value objects, **interfaces** (strategies, services, factories). The contracts. | BCL only — **no NuGet, no other project** |
| `BackupZCrypt.Application` | Use cases / orchestration, business services, validators, `Result`/`Result<T>`. | Domain |
| `BackupZCrypt.Infrastructure` | Concrete strategy & service implementations (crypto, KDF, compression, chunking, file I/O). | Domain (+ BouncyCastle, ZstdSharp) |
| `BackupZCrypt.Composition` | DI composition root wiring contracts → implementations. | Domain, Application, Infrastructure |
| `BackupZCrypt.Desktop` | Avalonia MVVM UI; **owns all localized text**. | Application, Composition (+ Avalonia, CommunityToolkit.Mvvm) |
| `BackupZCrypt.Test` | NUnit + NSubstitute test suite. | all of the above |

Hard rules (full detail → **clean-architecture** skill):
- Domain must never reference another project or a NuGet package.
- Implementations are `internal sealed` and exposed only via `InternalsVisibleTo` (Composition, Test).
  Register them in `BackupZCrypt.Composition/DependencyInjection.cs`, never new them up across layers.
- No user-facing English/Spanish strings below the Desktop layer — lower layers emit a language-neutral
  `MessageCode` carried by `LocalizableMessage`; the Desktop layer translates.

---

## 4. Definition of Done

A change is complete only when **every** box holds. Each maps to a skill that explains how.

- [ ] **Architecture** boundaries respected; dependencies still point inward. → `clean-architecture`
- [ ] **Security & performance** reviewed against the checklist; no regression in either. → `security-and-performance`
- [ ] **Build is clean**: zero new analyzer/Roslynator warnings, code matches `.editorconfig`. → `clean-code`
- [ ] **NuGet** packages are on their latest stable, compatible versions. → `clean-code`
- [ ] **Docs**: every non-`private` member has XML doc comments; `README.md` updated if behavior/usage changed. → `documentation`
- [ ] **Tests** cover the change and pass; **100% coverage** is maintained. → `testing`
- [ ] **Localization**: any user-facing text exists in **both** `Strings.resx` (en) and `Strings.es.resx` (es) with identical keys. → `localization`

---

## 5. Commands

```bash
# Build (analyzers run during build; warnings must be zero)
dotnet build BackupZCrypt.sln --nologo

# Run the desktop app
dotnet run --project BackupZCrypt.Desktop

# Run the full test suite
dotnet test BackupZCrypt.sln --nologo

# Auto-format to .editorconfig before committing
dotnet format BackupZCrypt.sln
```

---

## 6. Skills

These project skills auto-load from `.claude/skills/` when their trigger matches. Invoke one
explicitly with `/<name>` if it does not fire on its own.

| Skill | Use it when… |
|---|---|
| `clean-architecture` | adding/moving a type, choosing a layer, wiring DI, or reviewing dependency direction |
| `clean-code` | writing/refactoring C#, fixing analyzer warnings, or updating NuGet packages |
| `documentation` | adding/changing any non-`private` member, or shipping a user-visible change |
| `testing` | adding/changing code that needs tests, or checking coverage |
| `localization` | touching any user-facing string or adding a `MessageCode` |
| `security-and-performance` | touching crypto, key handling, I/O, or any hot path — and before finishing any change |

---

## 7. Token efficiency

Keep interactions lean. The biggest wins are **structural**, not stylistic:

1. **Progressive disclosure** (primary technique). This CLAUDE.md stays small; depth lives in skills
   that load only when relevant. Don't paste large procedures inline — point to the skill.
2. **Scoped tool use.** Prefer `Grep`/`Glob` and targeted, ranged `Read`s over reading whole files
   or trees. Don't re-read a file you just edited.
3. **Fan-out to subagents** for broad searches/exploration so raw results stay out of the main
   context; act on the conclusion they return.
4. **Concise output.** No preamble/postamble; answer, then stop. Report failures plainly.

---

## 8. Conventions

- **Commits**: Conventional Commits (`feat:`, `fix:`, `chore:`, `docs:`, `refactor:`, `test:`).
- **Branching**: feature branches off `develop`; PRs target `master` only for releases.
- Commit or push **only when asked**.
