using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Iris.DesignSystem.Controls;

public enum TextFieldKind
{
    Text,
    Name,
    Organization,
    Email,
    Password,
    NewPassword,
}

public sealed partial class IrisTextField : UserControl
{
    public static readonly DependencyProperty LabelProperty = Register(nameof(Label), null);
    public static readonly DependencyProperty PlaceholderProperty = Register(nameof(Placeholder), null);
    public static readonly DependencyProperty GlyphProperty = Register(nameof(Glyph), null);
    public static readonly DependencyProperty ErrorMessageProperty = Register(nameof(ErrorMessage), null);
    public static readonly DependencyProperty HintProperty = Register(nameof(Hint), null);

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(IrisTextField), new PropertyMetadata(string.Empty, (d, _) => ((IrisTextField)d).SyncText()));

    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(nameof(Kind), typeof(TextFieldKind), typeof(IrisTextField), new PropertyMetadata(TextFieldKind.Text, (d, _) => ((IrisTextField)d).Refresh()));

    private bool _isFocused;
    private bool _isRevealed;

    public IrisTextField()
    {
        InitializeComponent();
        Refresh();
    }

    /// <summary>Raised when Enter is pressed (move to the next field or submit).</summary>
    public event EventHandler? Submitted;

    public string? Label { get => (string?)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    public string? Placeholder { get => (string?)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }

    public string? Glyph { get => (string?)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }

    public string? ErrorMessage { get => (string?)GetValue(ErrorMessageProperty); set => SetValue(ErrorMessageProperty, value); }

    public string? Hint { get => (string?)GetValue(HintProperty); set => SetValue(HintProperty, value); }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public TextFieldKind Kind { get => (TextFieldKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }

    private bool IsSecret => Kind is TextFieldKind.Password or TextFieldKind.NewPassword;

    public void FocusInput() => (IsSecret ? (Control)SecretInput : Input).Focus(FocusState.Keyboard);

    private static DependencyProperty Register(string name, string? fallback) =>
        DependencyProperty.Register(name, typeof(string), typeof(IrisTextField), new PropertyMetadata(fallback, (d, _) => ((IrisTextField)d).Refresh()));

    private void Refresh()
    {
        LabelText.Text = Label ?? string.Empty;
        LabelText.Visibility = string.IsNullOrEmpty(Label) ? Visibility.Collapsed : Visibility.Visible;
        Icon.Glyph = Glyph ?? string.Empty;
        Input.PlaceholderText = SecretInput.PlaceholderText = Placeholder ?? string.Empty;
        AutomationProperties.SetName(Input, Label ?? Placeholder ?? string.Empty);
        AutomationProperties.SetName(SecretInput, Label ?? Placeholder ?? string.Empty);

        Input.Visibility = IsSecret ? Visibility.Collapsed : Visibility.Visible;
        SecretInput.Visibility = RevealButton.Visibility = IsSecret ? Visibility.Visible : Visibility.Collapsed;
        Input.InputScope = new InputScope
        {
            Names =
            {
                new InputScopeName(Kind switch
                {
                    TextFieldKind.Email => InputScopeNameValue.EmailSmtpAddress,
                    TextFieldKind.Name => InputScopeNameValue.PersonalFullName,
                    _ => InputScopeNameValue.Default,
                }),
            },
        };
        Input.IsSpellCheckEnabled = Kind is TextFieldKind.Text;
        Input.IsTextPredictionEnabled = Kind is TextFieldKind.Text;
        SecretInput.PasswordRevealMode = _isRevealed ? PasswordRevealMode.Visible : PasswordRevealMode.Hidden;
        RevealIcon.Glyph = _isRevealed ? Glyphs.EyeSlash : Glyphs.Eye;
        var revealName = _isRevealed ? "Ocultar contraseña" : "Mostrar contraseña";
        ToolTipService.SetToolTip(RevealButton, revealName);
        AutomationProperties.SetName(RevealButton, revealName);

        var hasError = !string.IsNullOrEmpty(ErrorMessage);
        if (hasError && ErrorRow.Visibility == Visibility.Collapsed)
        {
            ErrorRow.Opacity = 0;
            Motion.Fade(ErrorRow, 1, Motion.Snappy);
        }

        ErrorText.Text = ErrorMessage ?? string.Empty;
        ErrorRow.Visibility = hasError ? Visibility.Visible : Visibility.Collapsed;
        HintText.Text = Hint ?? string.Empty;
        HintText.Visibility = !hasError && !string.IsNullOrEmpty(Hint) ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetHelpText(Input, ErrorMessage ?? Hint ?? string.Empty);
        AutomationProperties.SetHelpText(SecretInput, ErrorMessage ?? Hint ?? string.Empty);

        ApplyChrome();
    }

    private void ApplyChrome()
    {
        var hasError = !string.IsNullOrEmpty(ErrorMessage);
        Container.Background = IrisTheme.Brush(_isFocused ? "IrisSurfaceRaisedBrush" : "IrisSurfaceBrush");
        Container.BorderBrush = hasError
            ? IrisTheme.Brush("IrisDangerTintBrush")
            : _isFocused ? IrisTheme.Brush("IrisAccentGradientBrush") : IrisTheme.Brush("IrisStrokeBrush");
        Container.BorderThickness = (Thickness)Application.Current.Resources[hasError || _isFocused ? "IrisFocusStroke" : "IrisHairline"];
        Icon.Foreground = hasError
            ? IrisTheme.Brush("IrisDangerBrush")
            : IrisTheme.Brush(_isFocused ? "IrisTextPrimaryBrush" : "IrisTextTertiaryBrush");
    }

    private void SyncText()
    {
        var text = Text ?? string.Empty;
        if (Input.Text != text)
        {
            Input.Text = text;
        }

        if (SecretInput.Password != text)
        {
            SecretInput.Password = text;
        }
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e) => Text = Input.Text;

    private void OnPasswordChanged(object sender, RoutedEventArgs e) => Text = SecretInput.Password;

    private void OnInputGotFocus(object sender, RoutedEventArgs e)
    {
        _isFocused = true;
        ApplyChrome();
    }

    private void OnInputLostFocus(object sender, RoutedEventArgs e)
    {
        _isFocused = false;
        ApplyChrome();
    }

    private void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            Submitted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnRevealClick(object sender, RoutedEventArgs e)
    {
        _isRevealed = !_isRevealed;
        Refresh();
        SecretInput.Focus(FocusState.Programmatic);
    }
}
