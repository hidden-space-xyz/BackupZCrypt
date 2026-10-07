using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;

namespace BackupZCrypt.Desktop.Controls;

/// <summary>
/// A text box for passwords whose accessibility value never carries the typed text while it is masked.
/// </summary>
/// <remarks>
/// The stock text box publishes its text through UI Automation even when a password character hides
/// it on screen, so any accessibility client — a screen reader reading it aloud, or another program
/// polling it — could read the password. This control reports the masked characters instead, and
/// only exposes the text while the user has chosen to reveal it.
/// </remarks>
internal sealed class PasswordTextBox : TextBox
{
    /// <summary>
    /// Gets the type whose styles and template the control uses: those of a regular text box.
    /// </summary>
    protected override Type StyleKeyOverride => typeof(TextBox);

    /// <summary>
    /// Gets a value indicating whether the text is currently hidden behind the password character.
    /// </summary>
    private bool IsMasked => this.PasswordChar != default && !this.RevealPassword;

    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new PasswordTextBoxAutomationPeer(this);
    }

    /// <summary>
    /// The accessibility peer of a <see cref="PasswordTextBox"/>: an edit control whose value is the
    /// masked text while the password is hidden.
    /// </summary>
    /// <param name="owner">The password text box the peer describes.</param>
    private sealed class PasswordTextBoxAutomationPeer(PasswordTextBox owner)
        : ControlAutomationPeer(owner),
            IValueProvider
    {
        /// <summary>
        /// Gets a value indicating whether the text box is read-only.
        /// </summary>
        public bool IsReadOnly => owner.IsReadOnly;

        /// <summary>
        /// Gets the text the peer exposes: one password character per typed character while the text
        /// is masked, and the text itself only while it is revealed on screen.
        /// </summary>
        public string? Value =>
            owner.IsMasked
                ? new string(owner.PasswordChar, owner.Text?.Length ?? 0)
                : owner.Text;

        /// <summary>
        /// Replaces the text, as typing would.
        /// </summary>
        /// <param name="value">The new text.</param>
        public void SetValue(string? value)
        {
            owner.Text = value;
        }

        /// <inheritdoc/>
        protected override AutomationControlType GetAutomationControlTypeCore()
        {
            return AutomationControlType.Edit;
        }

        /// <inheritdoc/>
        protected override string? GetPlaceholderTextCore()
        {
            return owner.PlaceholderText;
        }
    }
}
