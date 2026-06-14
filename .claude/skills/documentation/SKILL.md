---
name: documentation
description: >-
  Documentation standards for BackupZCrypt. Use when adding or changing any non-private type or
  member (which requires XML doc comments), or when a change alters behavior, features, build
  steps, or usage (which may require a README update). Ensures the public surface is fully
  documented and user-facing docs stay accurate.
---

# Documentation

Two obligations, both part of the Definition of Done.

## 1. XML doc comments on every non-`private` member

Any type or member that is **not `private`** (`public`, `internal`, `protected`) must carry XML doc
comments. `private` members are documented only when the logic is non-obvious (use a short `//`
comment there instead).

Match the existing house style (see `IFileOperationsService`, `LocalizableMessage`, `Result<T>`):

- `<summary>` is **multi-line** and explains *what and why*, not just a restatement of the name.
- Document **every** parameter with `<param>`, the return with `<returns>`, and each thrown
  exception that callers should handle with `<exception>`.
- Use `<see cref="..."/>` / `<paramref name="..."/>` / `<typeparamref>` for cross-references, and
  `<see langword="true"/>`/`false`/`null` for keywords.
- Document **enum members** individually (see `MessageCode`, `EncryptionAlgorithm`).
- Keep it accurate: if you change a signature or behavior, update its doc comment in the same edit.
  Roslynator flags malformed doc tags as warnings (`RCS1247`, `RCS1243`) — keep the build clean.

Example shape:

```csharp
/// <summary>
/// Encrypts a single chunk with AES-GCM and returns the ciphertext with the authentication
/// tag appended.
/// </summary>
/// <param name="plaintext">The chunk to encrypt.</param>
/// <param name="key">The encryption key.</param>
/// <returns>The ciphertext followed by the authentication tag.</returns>
/// <exception cref="CryptographicException">Authentication or decryption fails.</exception>
```

## 2. Keep `README.md` accurate

Update `README.md` when a change affects anything a reader relies on:

- A new or changed **feature**, encryption/KDF/compression algorithm, or supported behavior →
  update the Features / What-it-does sections.
- A change to **build, run, or prerequisite** steps → update *Building from Source*.
- A new project or a change in project responsibilities → update the *Project Structure* table.
- A change to **security behavior** → update *Security Notes*.

Do **not** touch the README for purely internal refactors that a user or contributor would never
observe. When unsure whether a change is user-visible, it probably belongs in the README.

## Don't over-document

- No redundant comments that just echo the code.
- No commented-out code, changelog noise, or "added by" attributions in source.
- Prefer clearer names and types over explanatory comments where possible.
