---
name: security-and-performance
description: >-
  Security-first, then performance review checklist for BackupZCrypt, a security-critical encrypted
  backup tool. Use when touching cryptography, key derivation, password handling, manifests, file
  I/O, or any hot path — and as a final gate before completing ANY change. Security always takes
  priority over performance, and both take priority over convenience.
---

# Security & Performance

This is a **security-critical** application: it guards user passwords, key material, and the only
copy of irreplaceable encrypted data, **with no password recovery**. The priority order is fixed
and absolute:

> **1. Security  →  2. Performance  →  everything else.**

Never weaken a security guarantee for speed or convenience. Never optimize at the cost of
correctness. If a change cannot satisfy security, it does not ship.

---

## A. Security checklist (priority 1)

### Key material & secrets
- **Zero secrets after use.** Wipe keys, plaintext, tags, and derived material with
  `CryptographicOperations.ZeroMemory(...)` in a `finally` block (see `AesEncryptionStrategy`).
  Buffers holding sensitive data must not outlive their use.
- **Never log, persist, or surface** passwords, keys, nonces, or plaintext — not in exceptions,
  messages, manifests, filenames, or progress reports.
- **One KDF per session.** Derive the master key once with the chosen expensive KDF
  (Argon2id default / Scrypt / PBKDF2); derive purpose-specific sub-keys via HKDF. Do not run the
  password KDF per file/chunk.
- Use `CryptographicOperations.FixedTimeEquals` for secret comparisons — never `==` or
  `SequenceEqual` on secrets/tags.

### Encryption (AEAD)
- **Always authenticated encryption.** Every cipher here is AEAD (AES-GCM, ChaCha20-Poly1305,
  Twofish/Serpent/Camellia-GCM). Never add or fall back to an unauthenticated mode.
- **Never reuse a (key, nonce) pair.** Nonces must be unique per chunk under a given key. Verify any
  new nonce-generation path cannot collide.
- **Bind context with associated data (AAD)** so chunks can't be swapped/relocated undetected.
- **Fail closed.** On any authentication/decryption failure, throw (`CryptographicException`), zero
  the plaintext buffer, and never return partial/unverified data. Don't leak whether key vs. tag
  failed.
- Use the platform/BouncyCastle primitives already present; **do not hand-roll crypto** or invent
  formats. Reuse the strategy interfaces (see `clean-architecture`).

### Passwords & input
- Enforce the existing password policy (min/max length, no leading/trailing spaces) and keep the
  strength analyzer/warnings intact; surface weak-password warnings, never silently downgrade.
- **Validate and normalize all paths** (use `PathNormalizationHelper`); guard against traversal,
  source/destination overlap, and operating on a file where a directory is required.
- Treat every manifest and backup artifact as **untrusted input** on read: validate sizes, bounds,
  and authenticity before acting; never trust lengths or offsets from the file.

### General
- Keep dependencies free of known vulnerabilities — a CVE in `BouncyCastle.Cryptography`,
  `ZstdSharp.Port`, Avalonia, etc. is a priority-1 issue (see `clean-code` for the scan commands).
- For a focused audit of pending changes, the `/security-review` command is available.

---

## B. Performance checklist (priority 2 — never above security)

- **Stream, don't slurp.** Process large files via streams/chunks with bounded buffers; avoid
  loading whole files into memory (respect the chunked architecture and `StreamConstants`).
- **Use `Span<T>` / `ReadOnlySpan<T>`** and avoid needless allocations and array copies on hot
  paths (encryption, hashing, chunking) — but never skip a security step (e.g. zeroing) to save an
  allocation.
- **Don't repeat expensive work**: one KDF per session (also a security rule), cache derived keys
  for the session, reuse buffers where safe.
- Keep crypto/I/O **off the UI thread**; honor `CancellationToken` so long operations stay
  responsive and abortable.
- Choose appropriate buffer sizes; prefer async I/O for file operations as the existing services do.
- Measure before micro-optimizing; a clear, correct, secure implementation beats a clever fast one.

---

## C. Final gate (run before finishing ANY change)

1. No secret can leak (logs, exceptions, messages, filenames, manifest).
2. All sensitive buffers are zeroed in `finally`.
3. No nonce reuse; AEAD preserved; failures fail closed.
4. No unbounded memory use on large inputs; `CancellationToken` honored.
5. Security-critical behavior is covered by tests (see `testing`), build is clean (see `clean-code`).

If any item is uncertain, stop and flag it rather than guessing — in this codebase a silent
security regression can mean permanent, unrecoverable data loss.
