namespace BackupZCrypt.Desktop.Messages;

/// <summary>
/// Decoupled navigation request: a page asks the shell to switch to another page without holding a
/// reference to the navigation host.
/// </summary>
/// <param name="PageType">The ViewModel type of the page to navigate to.</param>
public sealed record NavigateToPageMessage(Type PageType);
