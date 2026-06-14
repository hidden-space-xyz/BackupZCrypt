---
name: localization
description: >-
  Localization workflow for BackupZCrypt (English + Spanish). Use when adding or changing any
  user-facing text, introducing a MessageCode, or editing the resx resources. Lower layers emit
  language-neutral codes; only the Desktop layer holds translations, and both languages must keep
  identical key sets.
---

# Localization (English + Spanish)

The app ships in **English (`en`, neutral/default)** and **Spanish (`es`)**. The two resx files
must always contain the **identical set of keys** — `LocalizationParityTests` fails the build
otherwise. No user-facing string ever lives in the Domain, Application, or Infrastructure layers.

## How messages flow

1. **Lower layers** (Domain/Application/Infrastructure) never hold display text. They return a
   language-neutral `MessageCode` (enum, `BackupZCrypt.Domain/ValueObjects/Localization/MessageCode.cs`)
   wrapped in a `LocalizableMessage(code, args)`. The `Result` / `Result<T>` failure path carries
   these.
2. **The Desktop layer** resolves each `MessageCode` to localized text. Each enum member name maps
   to a resx key of the **same name** in `Strings.resx`. `MessageLocalizer` /
   `Resources/Strings.cs` perform the lookup and apply `string.Format` arguments.

Files:
- `BackupZCrypt.Desktop/Resources/Strings.resx` — English (neutral).
- `BackupZCrypt.Desktop/Resources/Strings.es.resx` — Spanish.

## Adding or changing a message — checklist

When a lower layer needs to report something to the user:

1. **Add a `MessageCode` member** in `MessageCode.cs` with an XML doc comment. If it takes
   `string.Format` arguments, **suffix the name with `Format`** (e.g. `SourcePathNotExistFormat`) —
   that is the established convention.
2. **Add a resx entry with the exact same name** to **both** `Strings.resx` (English) **and**
   `Strings.es.resx` (Spanish). Keep placeholders (`{0}`, `{1}`) consistent across both languages.
3. **Emit it** from the lower layer via `new LocalizableMessage(MessageCode.X, arg0, …)` (or the
   `Result.Failure(MessageCode.X, args)` overload).
4. **Run the parity tests**: `dotnet test --filter "FullyQualifiedName~LocalizationParityTests"`.
   They assert (a) every `MessageCode` has an English key and (b) the English and Spanish key sets
   are identical.

When adding **UI-only** strings (labels, buttons) that don't pass through `MessageCode`, still add
the key to **both** resx files.

## Rules

- Never hardcode display text outside the Desktop resx files. A literal English/Spanish string in
  Domain/Application/Infrastructure is a layering violation (see `clean-architecture`).
- Never add a key to one language and forget the other — the parity test is the gate, run it.
- Keep translations faithful and complete; don't leave a Spanish value as the English text.
- New `MessageCode` members and resx keys are documented/tested like any other change
  (see `documentation`, `testing`).
