<p align="center">
<img alt=".NET" src="https://img.shields.io/badge/.NET_10-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" />
<img alt="Docker" src="https://img.shields.io/badge/Docker-2496ED?style=for-the-badge&logo=docker&logoColor=white" />
<img alt="Windows" src="https://img.shields.io/badge/Windows-0078D6?style=for-the-badge&logo=windows&logoColor=white" />
<img alt="Linux" src="https://img.shields.io/badge/Linux-FCC624?style=for-the-badge&logo=linux&logoColor=black" />
<img alt="macOS" src="https://img.shields.io/badge/macOS-000000?style=for-the-badge&logo=apple&logoColor=white" />
<img alt="License" src="https://img.shields.io/badge/GPL--3.0-red?style=for-the-badge" />
</p>

 # 🔐 BackupZCrypt

**BackupZCrypt is a simple, powerful tool that helps you protect your sensitive files with military-grade encryption.**

## 📋 What BackupZCrypt Does For You

BackupZCrypt gives you privacy and security in a few simple clicks:

- **🛡️ Protect Sensitive Documents** - Encrypt financial records, personal photos, medical information, and more
- **☁️ Secure Cloud Storage** - Safely store encrypted files on any cloud like Dropbox, Google Drive, or OneDrive
- **🔒 Control Your Privacy** - Keep your data private, even when sharing devices or storage
- **✅ Peace of Mind** - Industry-standard encryption means your files stay private until you decide otherwise

## ❓ Why Choose BackupZCrypt?

- **🖱️ Simple Interface** - No cryptography knowledge needed - just select files, choose a password, and encrypt
- **🐳 Automated Docker Worker** - Run unattended backups and restores in Docker containers with environment variable configuration
- **🏦 Military-Grade Security** - Uses the same encryption standards trusted by financial institutions
- **🔌 No Internet Required** - Works completely offline, keeping your sensitive data off the network
- **🛠️ Multiple Security Options** - Choose from multiple proven encryption methods to match your needs
- **💯 Completely Free** - Open-source and free to use, forever

## ⭐ Features for Privacy Enthusiasts

* **🔄 Multiple Encryption Algorithms** - AES-256 GCM, ChaCha20-Poly1305, Twofish-256 GCM, Serpent-256 GCM, and Camellia-256 GCM
* **🔑 Multiple Key Derivation Algorithms** - Argon2id, Scrypt, and PBKDF2
* **🗜️ Zstandard Compression** - Three compression presets: Fast (speed-optimized), Normal (balanced, default), and Best (maximum compression)
* **🕵️ Filename Obfuscation** - Hide original file and folder names using GUID, SHA-256, or SHA-512 obfuscation
* **📄 Manifest-Based Architecture** - Encrypted manifest files store all metadata needed for restoration, eliminating per-file headers and simplifying the restore process
* **📊 Password Strength Guidance** - Built-in analyzer evaluates your password and warns you before using a weak one
* **💻 Local Processing Only** - Your files and passwords never leave your computer
* **👁️ Zero Data Collection** - We don't track, collect, or transmit any of your information


## 🐳 Docker Worker

BackupZCrypt includes a Docker-based worker service for **automated, unattended backups and restores**.

### How It Works

The worker uses **four volume bindings**:

| Volume | Path in Container | Purpose |
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

### Environment Variables

All CLI configuration options are available as environment variables:

| Variable | Description | Default |
|---|---|---|
| `BACKUP_PASSWORD` | Password for encryption/decryption | *(empty)* |
| `BACKUP_USE_ENCRYPTION` | Enable or disable encryption (`true`/`false`) | `true` |
| `BACKUP_ENCRYPTION_ALGORITHM` | `Aes`, `Twofish`, `Serpent`, `ChaCha20`, `Camellia` | `Aes` |
| `BACKUP_KEY_DERIVATION_ALGORITHM` | `Argon2id`, `PBKDF2`, `Scrypt` | `Argon2id` |
| `BACKUP_NAME_OBFUSCATION` | `None`, `Guid`, `Sha256`, `Sha512` | `None` |
| `BACKUP_COMPRESSION` | `None`, `ZstdFast`, `Zstd`, `ZstdBest` | `None` |
| `BACKUP_DELETE_SOURCE_FILES` | Delete source files after operation (`true`/`false`) | `false` |

## 🚀 Roadmap

BackupZCrypt is constantly evolving. Here's what we're planning for future releases:

- **🎨 Enhanced User Interface** - Upcoming UI improvements for better usability and aesthetics
- **⚙️ Advanced Parameter Configuration** - Expert mode allowing customization of encryption parameters for advanced users
- **👥 Community-Driven Development** - We highly value community suggestions and contributions to guide the project's future

We're committed to continuously improving BackupZCrypt based on user feedback and security best practices. Your suggestions are always welcome and will help shape the application's future.

## 📸 Screenshots

<p align="center">
<img width="886" height="578" alt="image" src="https://github.com/user-attachments/assets/e8c9a10d-3ad7-4cf0-bd83-9cd4fa5292c3" />
</p>

## 🔍 Security Notes

- 🔑 Your security depends on your password strength - use long, complex passwords
- 🔄 Keep your operating system and BackupZCrypt updated
- ⚠️ There is no password recovery. If you forget your password, your encrypted files cannot be decrypted.

## 💡How to Contribute

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
