---
name: clean-architecture
description: >-
  Enforces the Clean Architecture + DDD layering of BackupZCrypt (Domain ← Application ←
  Composition → Desktop; Infrastructure → Domain). Use when adding, moving, or renaming a type;
  deciding which project code belongs in; introducing an interface or its implementation; wiring
  dependency injection; or reviewing whether dependencies still point inward.
---

# Clean Architecture + DDD

The solution is split into concentric layers. **Dependencies point inward only.** Outer layers
depend on inner ones; inner layers never know the outer ones exist.

## Layer responsibilities

| Project | Contains | May reference |
|---|---|---|
| `BackupZCrypt.Domain` | Enums, constants, value objects, and **interfaces** (`IEncryptionAlgorithmStrategy`, `IKeyDerivationAlgorithmStrategy`, `ICompressionStrategy`, `IChunkingStrategy`, `IFileOperationsService`, `ISystemStorageService`, factory interfaces). Pure contracts and domain types. | **BCL only.** No NuGet packages, no project references. |
| `BackupZCrypt.Application` | Use cases and orchestration (`BackupOrchestrator`), business services (`ChunkedBackupService`, `ManifestService`, `PasswordService`, `SettingsService`), validators, and the `Result` / `Result<T>` types. | Domain |
| `BackupZCrypt.Infrastructure` | Concrete implementations of Domain interfaces: encryption, key derivation, compression, chunking strategies, and file/storage services. | Domain (+ `BouncyCastle.Cryptography`, `ZstdSharp.Port`) |
| `BackupZCrypt.Composition` | The DI composition root (`DependencyInjection.cs`) wiring contracts to implementations. | Domain, Application, Infrastructure |
| `BackupZCrypt.Desktop` | Avalonia MVVM UI (views, view models, services) and **all localized text**. | Application, Composition (+ Avalonia, CommunityToolkit.Mvvm) |

## Hard rules

1. **Domain depends on nothing.** No project references, no NuGet packages. If you reach for a
   third-party type in Domain, the abstraction belongs there but the implementation belongs in
   Infrastructure.
2. **Dependencies flow inward.** Application may use Domain; Infrastructure may use Domain; never
   the reverse. Domain must not reference Application or Infrastructure.
3. **Program to interfaces.** A Domain interface defines the contract; Infrastructure provides the
   `internal sealed` implementation. Consumers depend on the interface, resolved via DI.
4. **Implementations are `internal sealed`.** They are exposed to `Composition` and `Test` only via
   `InternalsVisibleTo` (already configured in the `.csproj` files). Do not make them `public` to
   wire them up.
5. **No `new` across layers.** Never instantiate a concrete service/strategy from another layer.
   Register it in `BackupZCrypt.Composition/DependencyInjection.cs` and inject the interface.
6. **No localized strings below Desktop.** Lower layers return a language-neutral `MessageCode`
   wrapped in `LocalizableMessage`; only the Desktop layer resolves it to text. (See `localization`.)

## Where does new code go? (decision guide)

- A new **contract/abstraction**, enum, constant, or value object → **Domain**.
- A new **algorithm or platform implementation** (a cipher, KDF, compressor, file access) →
  **Infrastructure**, as an `internal sealed` class implementing a Domain interface.
- A new **use case, orchestration step, validation, or business rule** → **Application**.
- **UI, view models, dialogs, clipboard/file-picker, or anything user-facing** → **Desktop**.
- It needs to be **resolvable**? Register it in **Composition**.

## Strategy pattern (the dominant idiom)

Algorithm families are sets of strategies selected by an enum `Id`. All strategies of a family are
registered as singletons so consumers can resolve `IEnumerable<TStrategy>` and index by `Id` via a
factory (`KeyDerivationServiceFactory`, `CompressionServiceFactory`). To add a variant:

1. Implement the Domain interface as an `internal sealed` class exposing its enum `Id`.
2. Add the enum member in Domain.
3. Register the new strategy in `DependencyInjection.cs` (`AddDomainServices`).
4. Cover it with tests and (if user-selectable) localized strings.

## Before you finish

- Confirm no inward type leaked outward and no new project/package reference points the wrong way.
- A quick check: Domain `.csproj` still has **zero** `PackageReference`/`ProjectReference` entries.
