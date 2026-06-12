namespace BackupZCrypt.Desktop.Messages;

// Decoupled navigation request: a page asks the shell to switch to another page
// without holding a reference to the navigation host.
public sealed record NavigateToPageMessage(Type PageType);
