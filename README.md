<p align="center">
<img alt=".NET" src="https://img.shields.io/badge/.NET_10-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" />
<img alt="Windows" src="https://img.shields.io/badge/Windows-0078D6?style=for-the-badge&logo=windows&logoColor=white" />
<img alt="Linux" src="https://img.shields.io/badge/Linux-FCC624?style=for-the-badge&logo=linux&logoColor=black" />
<img alt="macOS" src="https://img.shields.io/badge/macOS-000000?style=for-the-badge&logo=apple&logoColor=white" />
<img alt="License" src="https://img.shields.io/badge/GPL--3.0-red?style=for-the-badge" />
</p>

 # 🔐 BackupZCrypt

**BackupZCrypt is a chunk-based encrypted backup tool that protects your files with military-grade encryption. When a file changes, only the affected chunks are re-encrypted — not the entire file.**

## 📋 What BackupZCrypt Does For You

BackupZCrypt gives you privacy and security with efficient, incremental backups:

- **🛡️ Protect Sensitive Documents** — Encrypt financial records, personal photos, medical information, and more
- **☁️ Secure Cloud Storage** — Safely store encrypted backups on any cloud like Dropbox, Google Drive, or OneDrive
- **⚡ Efficient Updates** — Only changed chunks are re-encrypted and synced, saving time and bandwidth
- **🔒 Control Your Privacy** — Keep your data private, even when sharing devices or storage
- **✅ Peace of Mind** — Industry-standard authenticated encryption means your files stay private and tamper-proof

## ❓ Why Choose BackupZCrypt?

- **🧩 Chunk-Based Architecture** — Files are split into variable-size chunks using content-defined chunking (FastCDC), so small edits don't require re-encrypting entire files
- **🖱️ Simple Interface** — No cryptography knowledge needed — just select files, choose a password, and encrypt
- **🏦 Military-Grade Security** — Uses the same encryption standards trusted by financial institutions
- **🔌 No Internet Required** — Works completely offline, keeping your sensitive data off the network
- **🛠️ Multiple Security Options** — Choose from multiple proven encryption and key derivation methods
- **💯 Completely Free** — Open-source and free to use, forever

## ⭐ Features

- **🧩 Content-Defined Chunking** — Files are split into variable-size chunks using a gear-hash algorithm (FastCDC)
- **🔄 Multiple Encryption Algorithms** — AES-256 GCM, ChaCha20-Poly1305, Twofish-256 GCM, Serpent-256 GCM, and Camellia-256 GCM — all in authenticated encryption (AEAD) mode
- **🔑 Multiple Key Derivation Algorithms** — Argon2id (default), Scrypt, and PBKDF2
- **🗜️ Optional Zstandard Compression** — Three compression presets (Fast, Normal, Best) applied per-chunk before encryption. Disabled by default
- **📄 Encrypted Manifest** — A single encrypted manifest file stores all metadata (file paths, chunk references, hashes) needed for restoration
- **🔐 Single KDF Per Session** — One expensive key derivation produces a master key; sub-keys for encryption and chunk naming are derived via HKDF, eliminating per-file KDF overhead
- **🕵️ HMAC-Based Chunk Naming** — Chunk filenames are HMAC-SHA256 of the plaintext hash, keyed with a naming sub-key, preventing content confirmation attacks
- **📊 Password Strength Guidance** — Built-in analyzer evaluates your password and warns you before using a weak one
- **🛡️ Integrity Verification** — Check that a backup is complete and undamaged without restoring it: every chunk is decrypted, authenticated, and re-hashed against the manifest, and any missing or corrupted file is reported
- **⏱️ Backup Time Estimator** — Benchmarks the selected algorithms on your own hardware to estimate how long backing up a given amount of data (MB/GB/TB) would take
- **🌍 Localized Interface** — Available in English and Spanish
- **💻 Local Processing Only** — Your files and passwords never leave your computer
- **👁️ Zero Data Collection** — We don't track, collect, or transmit any of your information

## 🚀 Usage

BackupZCrypt is a modern, cross-platform desktop application built with Avalonia UI. Create, update, and restore encrypted backups through a polished graphical interface featuring live password strength analysis, a secure password generator, automatic detection of encrypted backups, and real-time progress reporting.

- **🔐 Create Backup** — Select a source folder, a destination, and a password to produce an encrypted backup
- **🔄 Update Backup** — Re-scan the source and re-encrypt only the chunks that changed since the last backup
- **📦 Restore Backup** — Point to an existing backup, enter its password, and recover your files anywhere
- **🛡️ Verify Integrity** — Point to a backup and enter its password to confirm every chunk is intact and restorable, without writing any files
- **⚙️ Settings** — Choose your preferred encryption, key derivation, and compression defaults, plus the interface language.

## 🛠️ Building from Source

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later

### Build and Run

```bash
git clone https://github.com/your-username/BackupZCrypt.git
cd BackupZCrypt
dotnet build BackupZCrypt.sln
dotnet run --project BackupZCrypt.Desktop
```

### Project Structure

| Project | Purpose |
|---|---|
| `BackupZCrypt.Domain` | Core contracts: enums, constants, strategy and service interfaces, value objects |
| `BackupZCrypt.Application` | Business logic: backup orchestration, chunked backup engine, manifest and settings services |
| `BackupZCrypt.Infrastructure` | Implementations: encryption, key derivation, compression, and chunking strategies; file system access |
| `BackupZCrypt.Composition` | Dependency injection wiring shared by all front ends |
| `BackupZCrypt.Desktop` | Cross-platform Avalonia UI (MVVM with CommunityToolkit.Mvvm) |

## 🚀 Roadmap

BackupZCrypt is constantly evolving. Here's what we're planning for future releases:

- **🎨 Enhanced User Interface** — Upcoming UI improvements for better usability and aesthetics
- **⚙️ Advanced Parameter Configuration** — Expert mode allowing customization of encryption parameters for advanced users
- **👥 Community-Driven Development** — We highly value community suggestions and contributions to guide the project's future

We're committed to continuously improving BackupZCrypt based on user feedback and security best practices. Your suggestions are always welcome and will help shape the application's future.

## 📸 Screenshots

<p align="center">
<img width="995" height="598" alt="image" src="https://github.com/user-attachments/assets/690b7637-0aac-4605-9a12-132c26e12158" />
</p>

## 🔍 Security Notes

- 🔑 Your security depends on your password strength — use long, complex passwords
- 🔄 Keep your operating system and BackupZCrypt updated
- ⚠️ There is no password recovery. If you forget your password, your encrypted files cannot be decrypted

## 💡 How to Contribute

- We welcome contributions from everyone, regardless of your technical background!
- Every contribution matters and helps make this project better for everyone!

#### For Non-Developers
You can make valuable contributions too:
- **Report Bugs**: Found something that doesn't work? Let us know by opening an issue.
- **Suggest Features**: Have ideas for new features or improvements? We'd love to hear them.
- **Translations**: Help translate the application into your language.
- **Documentation**: Improve or clarify our documentation.
- **Spread the Word**: Share the project on social media, blog about it, or tell your friends.
- **User Testing**: Try new features and provide feedback.

#### For Developers

1. Fork the repository
2. Create a feature branch from `develop`
3. Implement your changes with documentation and tests
4. Submit a pull request

We especially welcome contributions for UI and security improvements.
