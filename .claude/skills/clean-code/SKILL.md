---
name: clean-code
description: >-
  C# style, formatting, and dependency hygiene for BackupZCrypt. Use when writing or refactoring
  C#, resolving Roslynator/analyzer warnings, running dotnet format, or adding/updating NuGet
  packages. Ensures code matches the strict .editorconfig and that dependencies stay on their
  latest stable, compatible versions.
---

# Clean Code & Dependencies

The solution enforces quality through **Roslynator analyzers** plus a strict `.editorconfig`, with
`AnalysisMode=All` and `RunAnalyzersDuringBuild=true`. **A clean build has zero new warnings.**

## Formatting — the analyzer is the source of truth

Run `dotnet format BackupZCrypt.sln` before committing, then `dotnet build` and fix every warning.
Do not hand-tune style against the rules below — let the tooling normalize it. The conventions that
matter most here:

- **`var` always** for local declarations (`roslynator_use_var = always`).
- **Explicit accessibility modifiers** on every declaration (`public`/`internal`/`private`…).
- **File-scoped namespaces**, with a blank line after the declaration.
- **4-space indentation**, LF line endings, UTF-8, final newline, no trailing whitespace.
- **Max line length ~140**; wrap long signatures one parameter per line (see existing services).
- **Multi-line braces** for blocks and accessors; **block bodies** (not expression bodies) when a
  member spans multiple lines.
- **Trailing commas** in multi-line initializers, enums, and argument lists.
- **Pattern-matching null checks** (`is null` / `is not null`), not `== null`.
- Group `using` directives; prefer collection expressions where applicable.
- `internal sealed` for Infrastructure/Application implementation classes (see `clean-architecture`).

When unsure, open a neighboring file in the same layer and match it. `.editorconfig` documents the
full rule set and rationale.

## Clean-code habits

- Small, single-responsibility methods and classes; clear, intention-revealing names.
- No dead code, commented-out blocks, or leftover TODOs in committed work.
- Fail fast: validate inputs, return early, prefer the `Result` / `Result<T>` pattern over throwing
  for expected error conditions in the Application layer.
- Don't duplicate logic that already exists — search first, reuse helpers
  (`PathNormalizationHelper`, `ByteSizeFormatter`, the factories) rather than reinventing them.
- Security- and performance-sensitive code has extra rules — see `security-and-performance`.

## NuGet dependencies — keep them current

Versions are managed centrally with **Central Package Management (CPM)**. Every version lives in
`Directory.Packages.props` at the repo root as a `<PackageVersion>`; each `.csproj` references a
package with `<PackageReference Include="..." />` and **no `Version` attribute**. To add a package:
add a `<PackageVersion>` to `Directory.Packages.props`, then a versionless `<PackageReference>` to
the consuming project. To change a version, edit `Directory.Packages.props` only. Analyzer packages
keep their `PrivateAssets`/`IncludeAssets` metadata in the `.csproj` — only the version moves out.

Dependencies must stay on their **latest stable, compatible** versions. When touching package
references or starting substantial work:

```bash
# List packages with newer versions available
dotnet list BackupZCrypt.sln package --outdated

# Check for vulnerable / deprecated transitive or direct packages
dotnet list BackupZCrypt.sln package --vulnerable --include-transitive
dotnet list BackupZCrypt.sln package --deprecated
```

Update guidance:
- Bump versions in `Directory.Packages.props` to the newest **stable** release compatible with
  **.NET 10**; avoid pre-release unless the user asks.
- CPM guarantees one version per package solution-wide, so there is no drift to reconcile across
  projects — keep it that way (one `<PackageVersion>` per package).
- After any update: `dotnet build` and `dotnet test` must both pass with zero new warnings before the
  change is done.
- Treat a security advisory on a dependency as a **priority-1 (security)** issue — see
  `security-and-performance`.

Current direct dependencies: `BouncyCastle.Cryptography`, `ZstdSharp.Port` (Infrastructure);
`Avalonia*`, `CommunityToolkit.Mvvm` (Desktop); `Roslynator.Analyzers` +
`Roslynator.Formatting.Analyzers` (analyzers); NUnit + `NSubstitute` (tests).
