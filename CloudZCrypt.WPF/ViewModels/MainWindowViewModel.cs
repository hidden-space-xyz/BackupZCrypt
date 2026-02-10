using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using CloudZCrypt.Application.Orchestrators.Interfaces;
using CloudZCrypt.Application.Services.Interfaces;
using CloudZCrypt.Application.ValueObjects;
using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Exceptions;
using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Domain.ValueObjects.FileCrypt;
using CloudZCrypt.WPF.Commands;
using CloudZCrypt.WPF.Services.Interfaces;

namespace CloudZCrypt.WPF.ViewModels;

public class MainWindowViewModel : ObservableObjectBase
{
    private readonly IDialogService dialogService;
    private readonly IFileCryptOrchestrator fileProcessingOrchestrator;

    private CancellationTokenSource? cancellationTokenSource;

    private string sourceFilePath;
    private string destinationPath;

    private string password = string.Empty;
    private string confirmPassword = string.Empty;

    private bool isPasswordVisible;
    private bool isConfirmPasswordVisible;

    private EncryptionAlgorithm selectedEncryptionAlgorithm;
    private KeyDerivationAlgorithm selectedKeyDerivationAlgorithm;
    private NameObfuscationMode selectedNameObfuscationMode;
    private CompressionMode selectedCompressionMode;

    private bool isProcessing;
    private double progressValue;
    private string progressText = string.Empty;
    private bool areControlsEnabled = true;

    public IPasswordService PasswordService { get; }

    public string SourceFilePath
    {
        get => sourceFilePath;
        set
        {
            if (SetProperty(ref sourceFilePath, value))
            {
                RefreshProcessCommands();
            }
        }
    }

    public string DestinationPath
    {
        get => destinationPath;
        set
        {
            if (SetProperty(ref destinationPath, value))
            {
                RefreshProcessCommands();
            }
        }
    }

    public string Password
    {
        get => password;
        set
        {
            if (SetProperty(ref password, value))
            {
                RefreshProcessCommands();
            }
        }
    }

    public string ConfirmPassword
    {
        get => confirmPassword;
        set
        {
            if (SetProperty(ref confirmPassword, value))
            {
                RefreshProcessCommands();
            }
        }
    }

    public bool IsPasswordVisible
    {
        get => isPasswordVisible;
        set => SetProperty(ref isPasswordVisible, value);
    }

    public bool IsConfirmPasswordVisible
    {
        get => isConfirmPasswordVisible;
        set => SetProperty(ref isConfirmPasswordVisible, value);
    }

    public EncryptionAlgorithm SelectedEncryptionAlgorithm
    {
        get => selectedEncryptionAlgorithm;
        set
        {
            if (SetProperty(ref selectedEncryptionAlgorithm, value))
            {
                OnPropertyChanged(nameof(SelectedEncryptionAlgorithmInfo));
            }
        }
    }

    public KeyDerivationAlgorithm SelectedKeyDerivationAlgorithm
    {
        get => selectedKeyDerivationAlgorithm;
        set
        {
            if (SetProperty(ref selectedKeyDerivationAlgorithm, value))
            {
                OnPropertyChanged(nameof(SelectedKeyDerivationAlgorithmInfo));
            }
        }
    }

    public NameObfuscationMode SelectedNameObfuscationMode
    {
        get => selectedNameObfuscationMode;
        set
        {
            if (SetProperty(ref selectedNameObfuscationMode, value))
            {
                OnPropertyChanged(nameof(SelectedNameObfuscationModeInfo));
            }
        }
    }

    public CompressionMode SelectedCompressionMode
    {
        get => selectedCompressionMode;
        set
        {
            if (SetProperty(ref selectedCompressionMode, value))
            {
                OnPropertyChanged(nameof(SelectedCompressionModeInfo));
            }
        }
    }

    public IEncryptionAlgorithmStrategy? SelectedEncryptionAlgorithmInfo =>
        AvailableEncryptionAlgorithms.FirstOrDefault(a => a.Id == selectedEncryptionAlgorithm);

    public IKeyDerivationAlgorithmStrategy? SelectedKeyDerivationAlgorithmInfo =>
        AvailableKeyDerivationAlgorithms.FirstOrDefault(a =>
            a.Id == selectedKeyDerivationAlgorithm
        );

    public INameObfuscationStrategy? SelectedNameObfuscationModeInfo =>
        AvailableNameObfuscationModes.FirstOrDefault(a => a.Id == selectedNameObfuscationMode);

    public ICompressionStrategy? SelectedCompressionModeInfo =>
        AvailableCompressionModes.FirstOrDefault(a => a.Id == selectedCompressionMode);

    public bool IsProcessing
    {
        get => isProcessing;
        set
        {
            if (SetProperty(ref isProcessing, value))
            {
                UpdateControlState();
                RefreshProcessCommands();
            }
        }
    }

    public double ProgressValue
    {
        get => progressValue;
        set => SetProperty(ref progressValue, value);
    }

    public string ProgressText
    {
        get => progressText;
        set => SetProperty(ref progressText, value);
    }

    public bool AreControlsEnabled
    {
        get => areControlsEnabled;
        set => SetProperty(ref areControlsEnabled, value);
    }

    public ObservableCollection<IEncryptionAlgorithmStrategy> AvailableEncryptionAlgorithms { get; }

    public ObservableCollection<IKeyDerivationAlgorithmStrategy> AvailableKeyDerivationAlgorithms { get; }

    public ObservableCollection<INameObfuscationStrategy> AvailableNameObfuscationModes { get; }

    public ObservableCollection<ICompressionStrategy> AvailableCompressionModes { get; }

    public ICommand GenerateStrongPasswordCommand { get; }

    public ICommand SelectSourceFileCommand { get; }

    public ICommand SelectSourceDirectoryCommand { get; }

    public ICommand SelectDestinationPathCommand { get; }

    public ICommand TogglePasswordVisibilityCommand { get; }

    public ICommand ToggleConfirmPasswordVisibilityCommand { get; }

    public ICommand EncryptFileCommand { get; }

    public ICommand DecryptFileCommand { get; }

    public ICommand CancelCommand { get; }

    public MainWindowViewModel(
        IDialogService dialogService,
        IFileCryptOrchestrator orchestrator,
        IPasswordService passwordService,
        IEnumerable<IEncryptionAlgorithmStrategy> encryptionStrategies,
        IEnumerable<IKeyDerivationAlgorithmStrategy> keyDerivationStrategies,
        IEnumerable<INameObfuscationStrategy> nameObfuscationStrategies,
        IEnumerable<ICompressionStrategy> compressionStrategies
    )
    {
        this.dialogService = dialogService;
        this.fileProcessingOrchestrator = orchestrator;
        this.PasswordService = passwordService;

        AvailableEncryptionAlgorithms = new(encryptionStrategies.OrderBy(a => a.DisplayName));

        AvailableKeyDerivationAlgorithms = new(keyDerivationStrategies.OrderBy(a => a.DisplayName));

        AvailableNameObfuscationModes = new(nameObfuscationStrategies.OrderBy(a => a.DisplayName));

        AvailableCompressionModes = new(compressionStrategies.OrderBy(a => a.DisplayName));

        selectedEncryptionAlgorithm = AvailableEncryptionAlgorithms
            .First(x => x.Id == EncryptionAlgorithm.Aes)
            .Id;
        selectedKeyDerivationAlgorithm = AvailableKeyDerivationAlgorithms
            .First(x => x.Id == KeyDerivationAlgorithm.Argon2id)
            .Id;
        selectedNameObfuscationMode = AvailableNameObfuscationModes
            .First(x => x.Id == NameObfuscationMode.Guid)
            .Id;
        selectedCompressionMode = AvailableCompressionModes
            .First(x => x.Id == CompressionMode.None)
            .Id;

        GenerateStrongPasswordCommand = new RelayCommand(GenerateStrongPassword);
        SelectSourceFileCommand = new RelayCommand(SelectSourceFile);
        SelectSourceDirectoryCommand = new RelayCommand(SelectSourceDirectory);
        SelectDestinationPathCommand = new RelayCommand(SelectDestinationPath);
        TogglePasswordVisibilityCommand = new RelayCommand(() =>
            IsPasswordVisible = !IsPasswordVisible
        );
        ToggleConfirmPasswordVisibilityCommand = new RelayCommand(() =>
            IsConfirmPasswordVisible = !IsConfirmPasswordVisible
        );

        EncryptFileCommand = new RelayCommand(
            async () =>
            {
                try
                {
                    await ProcessFileAsync(EncryptOperation.Encrypt);
                }
                catch (OperationCanceledException)
                { /* ignore */
                }
            },
            CanExecuteProcessFile
        );

        DecryptFileCommand = new RelayCommand(
            async () =>
            {
                try
                {
                    await ProcessFileAsync(EncryptOperation.Decrypt);
                }
                catch (OperationCanceledException)
                { /* ignore */
                }
            },
            CanExecuteProcessFile
        );

        CancelCommand = new RelayCommand(CancelProcessing, () => IsProcessing);

#if DEBUG
        sourceFilePath = @"D:\WorkSpace\EncryptionTest\ToEncrypt";
        destinationPath = @"D:\WorkSpace\EncryptionTest\Encrypted";
#endif
        UpdateControlState();
    }

    private void GenerateStrongPassword()
    {
        try
        {
            if (
                !string.IsNullOrEmpty(Password)
                && !dialogService.ShowConfirmation(
                    "This will replace your current password. Are you sure you want to generate a new one?",
                    "Replace Password"
                )
            )
            {
                return;
            }

            int length = 128;
            PasswordGenerationOptions options =
                PasswordGenerationOptions.IncludeUppercase
                | PasswordGenerationOptions.IncludeLowercase
                | PasswordGenerationOptions.IncludeNumbers
                | PasswordGenerationOptions.IncludeSpecialCharacters;

            string generated = PasswordService.GeneratePassword(length, options);
            Password = ConfirmPassword = generated;

            bool copied = TryCopyToClipboard(generated);
            dialogService.ShowMessage(
                BuildPasswordGeneratedMessage(copied),
                "Password Generated",
                MessageBoxImage.Information
            );
        }
        catch (Exception ex)
        {
            ShowError(ex, "generating password");
        }
    }

    private static bool TryCopyToClipboard(string value)
    {
        try
        {
            System.Windows.Clipboard.SetText(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildPasswordGeneratedMessage(bool clipboard)
    {
        return "🔐 Strong password generated successfully!\n\n"
            + "✅ 128 characters long\n"
            + "✅ Includes uppercase, lowercase, numbers, and symbols\n"
            + (
                clipboard
                    ? "✅ Copied to clipboard for your convenience\n\n"
                    : "⚠️ Could not copy to clipboard - please copy it manually\n\n"
            )
            + "⚠️ Please store this password securely - it cannot be recovered if lost!";
    }

    private void SelectSourceFile()
    {
        SelectPath(dialogService.ShowOpenFileDialog("Select file to encrypt/decrypt"), File.Exists);
    }

    private void SelectSourceDirectory()
    {
        SelectPath(
            dialogService.ShowFolderDialog("Select directory to encrypt/decrypt"),
            Directory.Exists
        );
    }

    private void SelectPath(string? selectedPath, Func<string, bool> exists)
    {
        try
        {
            if (!string.IsNullOrEmpty(selectedPath))
            {
                if (exists(selectedPath))
                {
                    SourceFilePath = selectedPath;
                }
                else
                {
                    dialogService.ShowMessage(
                        "The selected path no longer exists.",
                        "Not Found",
                        MessageBoxImage.Warning
                    );
                }
            }
        }
        catch (Exception ex)
        {
            ShowError(ex, "selecting path");
        }
    }

    private void SelectDestinationPath()
    {
        try
        {
            if (File.Exists(SourceFilePath))
            {
                string defaultName = Path.GetFileNameWithoutExtension(SourceFilePath);
                string? selectedPath = dialogService.ShowSaveFileDialog(
                    "Select destination for processed file",
                    "All files (*.*)|*.*",
                    defaultName
                );

                if (!string.IsNullOrEmpty(selectedPath))
                {
                    DestinationPath = selectedPath;
                }
            }
            else
            {
                string? selectedPath = dialogService.ShowFolderDialog(
                    "Select destination directory"
                );
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    DestinationPath = selectedPath;
                }
            }
        }
        catch (Exception ex)
        {
            ShowError(ex, "selecting destination");
        }
    }

    private bool CanExecuteProcessFile()
    {
        return !IsProcessing
            && new[] { SourceFilePath, DestinationPath, Password, ConfirmPassword }.All(s =>
                !string.IsNullOrWhiteSpace(s)
            );
    }

    private async Task ProcessFileAsync(EncryptOperation operation)
    {
        // Compose base request
        FileCryptRequest baseRequest = new(
            SourceFilePath,
            DestinationPath,
            Password,
            ConfirmPassword,
            SelectedEncryptionAlgorithm,
            SelectedKeyDerivationAlgorithm,
            operation,
            SelectedNameObfuscationMode,
            SelectedCompressionMode,
            ProceedOnWarnings: false
        );

        IsProcessing = true;

        try
        {
            Progress<FileCryptStatus> progress = new(OnProgressUpdate);
            cancellationTokenSource = new();

            // First call: perform validations and possibly process if no warnings
            Result<FileCryptResult> result = await fileProcessingOrchestrator.ExecuteAsync(
                baseRequest,
                progress,
                cancellationTokenSource.Token
            );

            if (!result.IsSuccess)
            {
                dialogService.ShowMessage(
                    $"Failed to {operation.ToString().ToLower()}: {string.Join(", ", result.Errors)}",
                    "Error",
                    MessageBoxImage.Error
                );
                return;
            }

            FileCryptResult response = result.Value;

            // Handle validation errors
            if (response.HasErrors && response.TotalFiles == 0 && response.ProcessedFiles == 0)
            {
                dialogService.ShowValidationErrors(response.Errors);
                return;
            }

            // Handle warnings: ask user to proceed
            if (response.HasWarnings && !baseRequest.ProceedOnWarnings)
            {
                string warningMessage =
                    $"⚠️ Please review the following concerns:\n\n{string.Join("\n\n", response.Warnings.Select(w => $"• {w}"))}";
                bool proceed = dialogService.ShowConfirmation(
                    $"{warningMessage}\n\nDo you want to continue with the {operation.ToString().ToLower()} operation?",
                    "Confirm Operation"
                );

                if (!proceed)
                {
                    throw new OperationCanceledException();
                }

                // Re-run with permission to proceed
                FileCryptRequest proceedRequest = baseRequest with
                {
                    ProceedOnWarnings = true,
                };

                result = await fileProcessingOrchestrator.ExecuteAsync(
                    proceedRequest,
                    progress,
                    cancellationTokenSource.Token
                );

                if (!result.IsSuccess)
                {
                    dialogService.ShowMessage(
                        $"Failed to {operation.ToString().ToLower()}: {string.Join(", ", result.Errors)}",
                        "Error",
                        MessageBoxImage.Error
                    );
                    return;
                }

                response = result.Value;
            }

            // Show final processing result
            string sourceType = File.Exists(SourceFilePath) ? "file" : "directory";
            dialogService.ShowProcessingResult(response, operation, sourceType);
        }
        catch (OperationCanceledException)
        { /* ignore */
        }
        catch (Exception ex)
        {
            string operationText =
                operation == EncryptOperation.Encrypt ? "Encrypting" : "Decrypting";
            ShowError(ex, operationText.ToLower(), operation);
        }
        finally
        {
            IsProcessing = false;
            cancellationTokenSource?.Dispose();
            cancellationTokenSource = null;
        }
    }

    private void CancelProcessing()
    {
        try
        {
            cancellationTokenSource?.Cancel();
            ProgressText = "Cancelling...";
        }
        catch (Exception ex)
        {
            ShowError(ex, "cancelling operation");
        }
        finally
        {
            ((RelayCommand)CancelCommand).NotifyCanExecuteChanged();
        }
    }

    private void OnProgressUpdate(FileCryptStatus update)
    {
        double progress =
            update.TotalBytes > 0 ? (double)update.ProcessedBytes / update.TotalBytes * 100 : 100;
        double bytesPerSecond =
            update.Elapsed.TotalSeconds > 0
                ? update.ProcessedBytes / update.Elapsed.TotalSeconds
                : 0;
        TimeSpan eta =
            update.TotalBytes > 0 && bytesPerSecond > 0
                ? TimeSpan.FromSeconds((update.TotalBytes - update.ProcessedBytes) / bytesPerSecond)
                : TimeSpan.Zero;

        ProgressValue = progress;
        ProgressText =
            $"Processing: {update.ProcessedFiles}/{update.TotalFiles} files ({progress:F1}%) - ETA: {eta:hh\\:mm\\:ss}";
    }

    private void UpdateControlState()
    {
        AreControlsEnabled = !IsProcessing;
        if (!IsProcessing)
        {
            ProgressValue = 0;
            ProgressText = string.Empty;
        }
    }

    private void RefreshProcessCommands()
    {
        ((RelayCommand)EncryptFileCommand).NotifyCanExecuteChanged();
        ((RelayCommand)DecryptFileCommand).NotifyCanExecuteChanged();
        ((RelayCommand)CancelCommand).NotifyCanExecuteChanged();
    }

    private void ShowError(Exception ex, string context, EncryptOperation? operation = null)
    {
        (string title, string message) = BuildErrorMessage(ex, context, operation);
        dialogService.ShowMessage(message, title, MessageBoxImage.Error);
    }

    private static (string Title, string Message) BuildErrorMessage(
        Exception ex,
        string context,
        EncryptOperation? operation
    )
    {
        switch (ex)
        {
            case EncryptionException enc:
            {
                (string Title, string? Advice) = enc.Code switch
                {
                    EncryptionErrorCode.AccessDenied => (
                        "Access Denied",
                        "Check file or folder permissions or run as administrator."
                    ),
                    EncryptionErrorCode.InsufficientDiskSpace => (
                        "Insufficient Disk Space",
                        "Free disk space or choose another destination."
                    ),
                    EncryptionErrorCode.InvalidPassword => (
                        "Invalid Password",
                        "Verify the password and try again."
                    ),
                    EncryptionErrorCode.FileCorruption => (
                        "File Corruption",
                        "The file may be damaged or not properly encrypted."
                    ),
                    EncryptionErrorCode.KeyDerivationFailed => (
                        "Key Derivation Error",
                        "A problem occurred while deriving the encryption key."
                    ),
                    EncryptionErrorCode.FileNotFound => (
                        "File Not Found",
                        "Ensure the file exists and is accessible."
                    ),
                    EncryptionErrorCode.CipherOperationFailed => (
                        "Encryption Error",
                        "The cryptographic operation failed."
                    ),
                    _ => ("Operation Failed", null),
                };

                string msg = enc.Message ?? enc.Code.ToString();
                return (Title, Advice is null ? msg : $"{msg}\n\n{Advice}");
            }
            case ValidationException val:
            {
                string title = "Validation Error";
                string msg = string.IsNullOrWhiteSpace(val.Message)
                    ? val.Code.ToString()
                    : val.Message;
                return (title, msg);
            }
            default:
            {
                string opText = operation is null
                    ? string.Empty
                    : $" during {operation.Value.ToString().ToLower()}";
                string raw = ex.Message ?? "Unknown error.";
                return ("Operation Failed", $"An error occurred{opText} while {context}: {raw}");
            }
        }
    }
}
