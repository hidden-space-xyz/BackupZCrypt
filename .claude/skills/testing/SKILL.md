---
name: testing
description: >-
  Testing conventions and the 100% coverage requirement for BackupZCrypt. Use when adding or
  changing any production code (which must be covered by tests), writing or fixing tests, running
  the suite, or measuring code coverage. The project uses NUnit + NSubstitute in
  BackupZCrypt.Test.
---

# Testing & Coverage

Every production change ships with tests, and the suite must stay green. **Target: 100% line and
branch coverage.** New code that lowers coverage is not done.

## Stack & layout

- **NUnit 4** (`[Test]`, `[TestCase]`, `[TestCaseSource]`, `Assert.That(...)` constraint model) +
  **NSubstitute** for mocking. `using NUnit.Framework;` is a global using.
- Tests live in `BackupZCrypt.Test`, split into:
  - `Unit/` mirroring the source layer (`Unit/Domain`, `Unit/Application`, `Unit/Infrastructure`).
  - `Integration/` for end-to-end flows (`BackupRestoreRoundtripTests`, `SettingsServiceTests`,
    `LocalizationParityTests`, …).
  - `Common/` for helpers: `TestHost` (builds the real DI provider via
    `AddDomainServices().AddApplicationServices()`), `TempDir`, `RecordingProgress`.

## Conventions (match the existing tests)

- Test classes are `public sealed`; name methods `Method_Scenario_ExpectedResult`
  (e.g. `Decrypt_WithWrongKey_Throws`).
- Use `Assert.That(actual, Is.EqualTo(expected))` — the constraint model, not classic asserts.
- Drive variants with `[TestCaseSource]` (see `EncryptionStrategyTests` running every cipher
  through the same roundtrip/tamper assertions).
- Resolve real implementations through `TestHost.CreateProvider()` for integration tests; use
  `NSubstitute` doubles to isolate a unit and to exercise error branches.
- Use `TempDir` for any filesystem work so tests are hermetic and self-cleaning.

## What to cover

- **Happy path** and **every failure branch** — a method returning `Result`/`Result<T>` needs a
  test per `MessageCode` it can emit.
- **Security-critical behavior must be tested explicitly**: roundtrip correctness, and that
  decryption throws `CryptographicException` on wrong key, wrong nonce, wrong associated data, and
  tampered ciphertext (see `EncryptionStrategyTests`). Apply the same rigor to KDF and manifest
  handling.
- **Boundaries**: empty input, zero-length, large sizes, min/max password length, path edge cases.
- When you add a `MessageCode` or user-facing string, `LocalizationParityTests` must still pass.

## Commands

```bash
# Run the whole suite
dotnet test BackupZCrypt.sln --nologo

# Run one test class
dotnet test BackupZCrypt.sln --filter "FullyQualifiedName~EncryptionStrategyTests"
```

### Measuring coverage

A coverage collector is not yet wired into `BackupZCrypt.Test.csproj`. To measure coverage, add the
`coverlet.collector` package to the test project, then:

```bash
dotnet test BackupZCrypt.sln --collect:"XPlat Code Coverage"
# Optional HTML report (requires the reportgenerator global tool):
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coveragereport
```

Inspect any line/branch the change left uncovered and add tests until it reaches 100%. If a line is
genuinely unreachable, prefer removing it over leaving it untested.

## Definition of done for a change

- New/changed behavior has direct tests, including its error branches.
- `dotnet test` is fully green.
- Coverage is at 100% (no net decrease); justify any unavoidable gap explicitly.
