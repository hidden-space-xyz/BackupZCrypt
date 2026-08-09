<p align="center">
<img alt=".NET" src="https://img.shields.io/badge/.NET_10-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" />
<img alt="Windows" src="https://img.shields.io/badge/Windows-0078D6?style=for-the-badge&logo=windows&logoColor=white" />
<img alt="Linux" src="https://img.shields.io/badge/Linux-FCC624?style=for-the-badge&logo=linux&logoColor=black" />
<img alt="macOS" src="https://img.shields.io/badge/macOS-000000?style=for-the-badge&logo=apple&logoColor=white" />
<img alt="License" src="https://img.shields.io/badge/GPL--3.0-red?style=for-the-badge" />
</p>

<p align="center">
<img alt="Release" src="https://img.shields.io/github/v/release/hidden-space-xyz/BackupZCrypt?style=for-the-badge&color=2EA44F" />
<img alt="CI" src="https://img.shields.io/github/actions/workflow/status/hidden-space-xyz/BackupZCrypt/ci.yml?style=for-the-badge&label=CI&logo=githubactions&logoColor=white" />
<img alt="CodeQL" src="https://img.shields.io/github/actions/workflow/status/hidden-space-xyz/BackupZCrypt/codeql.yml?style=for-the-badge&label=CodeQL&logo=github&logoColor=white" />
</p>

# 🔐 BackupZCrypt

**BackupZCrypt splits your files into chunks, deduplicates them, and encrypts every chunk
individually under a key that only your password can produce.**

## 📋 What BackupZCrypt Does For You

BackupZCrypt gives you privacy and security with efficient, incremental backups:

- **🛡️ Protect Sensitive Documents** — Encrypt financial records, personal photos, medical information, and more
- **☁️ Secure Cloud Storage** — Safely store encrypted backups on any cloud like Dropbox, Google Drive, or OneDrive
- **⚡ Efficient Updates** — Only changed chunks are re-encrypted and synced, saving time and bandwidth
- **🔒 Control Your Privacy** — Keep your data private, even when sharing devices or storage
- **✅ Peace of Mind** — Authenticated encryption means your files stay private *and* tamper-evident

## ⭐ Features

- **🧩 Content-Defined Chunking** — Files are split into variable-size chunks using a gear-hash algorithm (FastCDC)
- **🔄 Multiple Encryption Algorithms** — AES-256 GCM, ChaCha20-Poly1305, Twofish-256 GCM, Serpent-256 GCM, and Camellia-256 GCM — all in authenticated encryption (AEAD) mode
- **🔑 Multiple Key Derivation Algorithms** — Argon2id, Scrypt, and PBKDF2
- **🗜️ Optional Zstandard Compression** — Three presets (Fast, Normal, Best) applied per chunk before encryption
- **🎲 Password Generator** — One click produces a 50-character password from a cryptographic random source
- **📊 Password Strength Guidance** — Built-in analyzer evaluates your password and warns you before using a weak one
- **🛡️ Integrity Verification** — Check that a backup is complete and undamaged without restoring it
- **⏱️ Backup Time Estimator** — Benchmarks the selected algorithms on your own hardware
- **🌐 Portable Archives** — A backup written on one operating system restores identically on the other two
- **🌍 Localized Interface** — Available in English and Spanish
- **💻 Local Processing Only** — Your files and passwords never leave your computer
- **👁️ Zero Data Collection** — We don't track, collect, or transmit any of your information
- **💯 Completely Free** — Open-source and free to use, forever

## 🚀 Usage

- **🔐 Create Backup** — Select a source folder, a destination, and a password to produce an encrypted backup
- **🔄 Update Backup** — Re-scan the source and re-encrypt only the chunks that changed since the last backup
- **📦 Restore Backup** — Point to an existing backup, enter its password, and recover your files anywhere
- **🛡️ Verify Integrity** — Point to a backup and enter its password to confirm every chunk is intact and restorable, without writing any files
- **⚙️ Settings** — Choose your preferred encryption, key derivation, and compression defaults, plus the interface language

Settings are stored as plain JSON under `%LocalAppData%\BackupZCrypt` on Windows and the equivalent
local application data directory on Linux and macOS. They hold preferences only — never your password.

## 📸 Screenshots

<p align="center">
  <img width="30%" alt="BackupZCrypt desktop interface" src="https://github.com/user-attachments/assets/2fee1133-1831-4b1d-9c71-f3798471e505" />
  <img width="30%" alt="BackupZCrypt desktop interface" src="https://github.com/user-attachments/assets/1d2b6c7b-16cd-4b6c-9604-642d058475a7" />
  <img width="30%" alt="BackupZCrypt desktop interface" src="https://github.com/user-attachments/assets/516080e4-8fbc-44bc-bcf4-2ae825937ff4" />
</p>

## ⬇️ Download

Grab the build for your platform from the [latest release](https://github.com/hidden-space-xyz/BackupZCrypt/releases/latest).
Every asset is a self-contained single-file executable — no .NET runtime required:

| Platform | Asset |
| --- | --- |
| 🪟 Windows x64 | `BackupZCrypt-v<version>-win-x64.zip` |
| 🐧 Linux x64 | `BackupZCrypt-v<version>-linux-x64.tar.gz` |
| 🍎 macOS Intel | `BackupZCrypt-v<version>-osx-x64.tar.gz` |
| 🍎 macOS Apple Silicon | `BackupZCrypt-v<version>-osx-arm64.tar.gz` |

On Linux and macOS run `chmod +x BackupZCrypt` after extracting.

## 🔒 Security

### How your data is protected

| Layer | What BackupZCrypt does |
| --- | --- |
| **Key derivation** | Your password and a 32-byte random salt produce a 256-bit master key through Argon2id (256 MiB, 4 passes), Scrypt (N = 2¹⁸, r = 8, p = 1) or PBKDF2-SHA256 (800,000 iterations) |
| **Key separation** | HKDF-SHA256 splits that master key into four purpose-bound sub-keys, so the key that names a chunk cannot decrypt it |
| **Chunk encryption** | Each chunk is compressed, then sealed with the chosen AEAD cipher under a 96-bit nonce derived from its own content hash, carrying a 128-bit authentication tag |
| **File names** | Chunks are stored under `HMAC-SHA256(naming key, content hash)`, so the names in your backup folder reveal nothing about their contents |
| **Manifest** | The file list is a separately encrypted, authenticated document. Only its 34-byte header — cipher, key derivation function and salt — is readable, and that header is bound into the ciphertext so it cannot be swapped |
| **Restore** | Entry paths are validated and confined to the destination, decompression is bounded by the declared size, and every restored file's SHA-256 is re-checked against the manifest |

Key material is wiped from memory after use, and hashes and salts are compared in constant time.

Backups are portable between operating systems: the manifest records paths with `/` separators
regardless of the platform that wrote it, and restore accepts either separator, so an archive created
on Windows rebuilds the same directory tree on Linux and macOS.

BackupZCrypt contains no networking code. There is no telemetry, no update check, and no account —
nothing to opt out of.

### Before you start

- 🔑 Everything rests on your password — use the built-in generator, or a long passphrase kept somewhere safe
- ⚠️ There is no password recovery and no back door. Lose the password and the backup is unreadable, permanently
- 🧪 Verify a fresh backup, and restore it once for real. A backup you have never restored is an assumption
- 🔄 Keep your operating system and BackupZCrypt updated

## 🛠️ Building from Source

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later

### Build and Run

```bash
git clone https://github.com/hidden-space-xyz/BackupZCrypt.git
cd BackupZCrypt
dotnet build BackupZCrypt.sln
dotnet run --project BackupZCrypt.Desktop
```

Building never produces a distributable — the portable packages come only from the release workflow.

### Running the Tests

```bash
dotnet test BackupZCrypt.sln
```

The suite is designed to run unattended on CI: no test depends on wall-clock timing, throughput, or a
specific locale, and every temporary file lives in a directory the test owns and deletes.

### Project Structure

| Project | Purpose |
|---|---|
| `BackupZCrypt.Domain` | Core contracts: enums, constants, strategy and service interfaces, value objects, and the algorithm factories |
| `BackupZCrypt.Application` | Business logic: backup orchestration, chunked backup engine, manifest and settings services |
| `BackupZCrypt.Infrastructure` | Implementations: encryption, key derivation, compression, and chunking strategies; file system access |
| `BackupZCrypt.Composition` | Dependency injection wiring shared by all front ends |
| `BackupZCrypt.Desktop` | Cross-platform Avalonia UI (MVVM with CommunityToolkit.Mvvm) |
| `BackupZCrypt.Test` | xUnit suite: unit, integration, architecture, and on-disk format tests |

## 💡 How to Contribute

We welcome contributions from everyone, regardless of technical background — community suggestions
are what guide where this project goes next.

### For Non-Developers

- **Report Bugs**: Found something that doesn't work? Let us know by opening an issue.
- **Suggest Features**: Have ideas for new features or improvements? We'd love to hear them.
- **Translations**: Help translate the application into your language.
- **Documentation**: Improve or clarify our documentation.
- **Spread the Word**: Share the project on social media, blog about it, or tell your friends.
- **User Testing**: Try new features and provide feedback.

### For Developers

1. Fork the repository
2. Create a feature branch from `develop`
3. Implement your changes, with XML documentation and tests
4. Open a pull request against `develop` — `master` only receives release merges

Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/). `feat:`,
`fix:`, `refactor:` and `bump:` are the four prefixes the generated release notes are built from,
and a `!` marker or a `BREAKING CHANGE:` footer puts a warning banner at the top of them.

Automated checks run on pull requests into `master` only, so run the same three locally before
opening yours:

```bash
dotnet build BackupZCrypt.sln
dotnet test BackupZCrypt.sln
dotnet format whitespace BackupZCrypt.sln --verify-no-changes
```

We especially welcome contributions for UI and security improvements.

### For Maintainers

The release version is chosen by hand — it is the `<Version>` property in `Directory.Build.props`.
Landing a commit on `master` publishes `v<version>` as a GitHub Release, and publishes nothing if
that tag already exists: raising the property is what cuts a release, and a merge that leaves it
untouched ships no release at all. A version below the latest release fails the workflow.
[`.github/workflows/README.md`](.github/workflows/README.md) documents both pipelines in full.

## 📜 License

BackupZCrypt is released under the [GNU General Public License v3.0](LICENSE). You are free to use,
study, modify and redistribute it, and any derivative work must carry the same license.
