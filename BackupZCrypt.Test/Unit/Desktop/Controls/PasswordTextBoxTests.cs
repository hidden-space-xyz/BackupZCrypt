using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;

using BackupZCrypt.Desktop.Controls;
using BackupZCrypt.Test.Common;

namespace BackupZCrypt.Test.Unit.Desktop.Controls;

/// <summary>
/// UI tests for <see cref="PasswordTextBox"/>: accessibility clients must never read a masked password, only
/// the mask, while the box still behaves as an editable text field for them.
/// </summary>
public sealed class PasswordTextBoxTests
{
    [Fact]
    internal Task AutomationValue_WhileMasked_IsOneMaskCharacterPerTypedCharacter()
    {
        return WithPeerAsync(
            () => new PasswordTextBox { PasswordChar = '*', Text = "secret" },
            static (_, value, _) => Assert.Equal("******", value.Value)
        );
    }

    [Fact]
    internal Task AutomationValue_WhileMaskedAndEmpty_IsEmpty()
    {
        return WithPeerAsync(
            () => new PasswordTextBox { PasswordChar = '*' },
            static (_, value, _) => Assert.Equal(string.Empty, value.Value)
        );
    }

    [Theory]
    [InlineData('*', true)]
    [InlineData('\0', false)]
    internal Task AutomationValue_WhenRevealedOrUnmasked_IsTheText(char passwordChar, bool reveal)
    {
        return WithPeerAsync(
            () => new PasswordTextBox { PasswordChar = passwordChar, RevealPassword = reveal, Text = "secret" },
            static (_, value, _) => Assert.Equal("secret", value.Value)
        );
    }

    [Fact]
    internal Task AutomationPeer_DescribesAnEditableFieldWithItsPlaceholder()
    {
        return WithPeerAsync(
            () => new PasswordTextBox { PasswordChar = '*', PlaceholderText = "Password", IsReadOnly = true },
            static (box, value, peer) =>
            {
                value.SetValue("typed");

                Assert.Multiple(
                    () => Assert.Equal("typed", box.Text),
                    () => Assert.True(value.IsReadOnly),
                    () => Assert.Equal(AutomationControlType.Edit, peer.GetAutomationControlType()),
                    () => Assert.Equal("Password", peer.GetPlaceholderText())
                );
            }
        );
    }

    /// <summary>
    /// Shows a password box and hands its automation peer to the assertions.
    /// </summary>
    /// <param name="createBox">Creates the box to show, on the UI thread.</param>
    /// <param name="assert">The assertions over the box, its value provider, and its peer.</param>
    /// <returns>A task that completes once the test has run on the UI thread.</returns>
    private static Task WithPeerAsync(
        Func<PasswordTextBox> createBox,
        Action<PasswordTextBox, IValueProvider, AutomationPeer> assert
    )
    {
        return HeadlessUi.RunAsync(() =>
        {
            var box = createBox();
            var window = HeadlessUi.Show(box);

            try
            {
                var peer = ControlAutomationPeer.CreatePeerForElement(box);
                assert(box, Assert.IsAssignableFrom<IValueProvider>(peer), peer);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
