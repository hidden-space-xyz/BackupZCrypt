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
- **🤖 Automated Worker** — Run unattended backups and restores as a background service
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
- **💻 Local Processing Only** — Your files and passwords never leave your computer
- **👁️ Zero Data Collection** — We don't track, collect, or transmit any of your information

## 🚀 Usage

BackupZCrypt offers two execution modes depending on your needs:

### 💻 Terminal (Interactive)

The Terminal mode provides an interactive command-line interface for manual backup and restore operations. Ideal for one-off tasks or when you want full control over each step.

### 🤖 Worker (Automated)

The Worker is a background service that performs **automated, unattended backups and restores**. It runs once, processes any pending operations, and stops automatically — perfect for scheduled tasks and CI/CD pipelines.

#### How It Works

The worker uses **four directory bindings**:

| Directory | Default Path | Purpose |
|---|---|---|
| **BackupSource** | `/data/backup-source` | Place files here to create an encrypted backup |
| **BackupDestination** | `/data/backup-destination` | Encrypted backup output is written here |
| **RestoreSource** | `/data/restore-source` | Place an encrypted backup here to restore it |
| **RestoreDestination** | `/data/restore-destination` | Restored files are written here |

**On startup**, the worker:
1. Checks `BackupSource` for files → if found, creates/updates an encrypted backup in `BackupDestination`
2. Checks `RestoreSource` for a valid backup (manifest file) → if found, restores it to `RestoreDestination`
3. Optionally deletes source files after each successful operation
4. Stops automatically when there is nothing left to process

#### Environment Variables

All configuration options are available as environment variables:

| Variable | Description | Default |
|---|---|---|
| `BACKUP_PASSWORD` | Password for encryption/decryption | *(empty)* |
| `BACKUP_ENCRYPTION_ALGORITHM` | `None`, `Aes`, `Twofish`, `Serpent`, `ChaCha20`, `Camellia` | `Aes` |
| `BACKUP_KEY_DERIVATION_ALGORITHM` | `Argon2id`, `PBKDF2`, `Scrypt` | `Argon2id` |
| `BACKUP_COMPRESSION` | `None`, `ZstdFast`, `Zstd`, `ZstdBest` | `None` |
| `BACKUP_DELETE_SOURCE_FILES` | Delete source files after operation (`true`/`false`) | `false` |

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
