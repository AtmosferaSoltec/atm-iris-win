using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iris.Core.Models;
using Iris.Core.Services;
using Iris.Shared.Projection;
using Microsoft.UI.Xaml.Media;

namespace Iris.Features.Modules;

/// <summary>A typeface in the list, drawn in its own font.</summary>
public sealed partial class FontOptionViewModel(ProjectionFontFamily family, ProjectionSettingsViewModel owner) : ObservableObject
{
    public ProjectionFontFamily Family { get; } = family;

    public ProjectionSettingsViewModel Owner { get; } = owner;

    public string Name => ProjectionFonts.DisplayName(Family);

    public FontFamily Font => ProjectionFonts.FontFamily(Family);

    /// <summary>Divider above every row but the first.</summary>
    public bool HasDivider => Family != ProjectionFontFamily.System;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>A quick-pick size ("88").</summary>
public sealed partial class FontSizeOptionViewModel(int size, ProjectionSettingsViewModel owner) : ObservableObject
{
    public int Size { get; } = size;

    public ProjectionSettingsViewModel Owner { get; } = owner;

    public string Text => Size.ToString(System.Globalization.CultureInfo.InvariantCulture);

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>"Ninguno" (black) or one of the backgrounds of the console's picker.</summary>
public sealed partial class DefaultBackgroundOptionViewModel(ProjectionBackground? model, ProjectionSettingsViewModel owner) : ObservableObject
{
    public ProjectionBackground? Model { get; } = model;

    public ProjectionSettingsViewModel Owner { get; } = owner;

    public string? Id => Model?.Id;

    public string Name => Model?.Name ?? "Ninguno";

    public Brush Swatch => Model is { } background ? Iris.DesignSystem.IrisTheme.BackgroundBrush(background) : Iris.DesignSystem.IrisTheme.Brush("IrisBlackBrush");

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>
/// Proyección (api-contract §6): typeface, size and default background of the projected lyrics, the same on every
/// console of the church. Every change saves right away, one after another; a failed save puts the previous value
/// back. Every canvas of this PC (thumbnails, EN VIVO, the TV) follows the change at once.
/// </summary>
public sealed partial class ProjectionSettingsViewModel : ObservableObject
{
    private const int Step = 4;

    private readonly IProjectionSettingsRepository _repository;
    private readonly IBackgroundRepository _backgrounds;

    public ProjectionSettingsViewModel(IProjectionSettingsRepository repository, IBackgroundRepository backgrounds)
    {
        _repository = repository;
        _backgrounds = backgrounds;
        Fonts = Enum.GetValues<ProjectionFontFamily>().Select(f => new FontOptionViewModel(f, this)).ToList();
        Sizes = ProjectionSettings.SuggestedFontSizes.Select(s => new FontSizeOptionViewModel(s, this)).ToList();
    }

    public IReadOnlyList<FontOptionViewModel> Fonts { get; }

    public IReadOnlyList<FontSizeOptionViewModel> Sizes { get; }

    [ObservableProperty]
    public partial IReadOnlyList<DefaultBackgroundOptionViewModel> Backgrounds { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewFrame), nameof(FontSizeText), nameof(CanDecreaseFontSize), nameof(CanIncreaseFontSize))]
    public partial ProjectionSettings Settings { get; private set; } = ProjectionSettings.Default;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; } = true;

    /// <summary>The tail of the save chain (tests await it).</summary>
    public Task SaveTask { get; private set; } = Task.CompletedTask;

    public string FontSizeText => $"{Settings.FontSizePt} pt";

    public bool CanDecreaseFontSize => Settings.FontSizePt > ProjectionSettings.MinFontSizePt;

    public bool CanIncreaseFontSize => Settings.FontSizePt < ProjectionSettings.MaxFontSizePt;

    /// <summary>A sample stanza, so the typeface, size and background are seen exactly as the TV will show them.</summary>
    public ProjectionFrame PreviewFrame => new(
        Backgrounds.FirstOrDefault(b => b.Id is not null && b.Id == Settings.DefaultBackgroundId)?.Model,
        new TextContent("Sublime gracia del Señor\nque a un pecador salvó", "Sublime gracia · Estrofa 1"));

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            Settings = await _repository.SettingsAsync();
        }
        catch (Exception)
        {
            Settings = ProjectionSettings.Default;
        }

        IReadOnlyList<ProjectionBackground> backgrounds;
        try
        {
            backgrounds = await _backgrounds.BackgroundsAsync();
        }
        catch (Exception)
        {
            backgrounds = BuiltInBackgrounds.All;
        }

        Backgrounds = [new(null, this), .. backgrounds.Select(b => new DefaultBackgroundOptionViewModel(b, this))];
        IsLoading = false;
        ProjectionTypography.Set(Settings);
        Sync();
    }

    [RelayCommand]
    private void SelectFont(FontOptionViewModel option) => Save(Settings with { FontFamily = option.Family });

    [RelayCommand]
    private void SelectSize(FontSizeOptionViewModel option) => SetFontSize(option.Size);

    [RelayCommand]
    private void IncreaseFontSize() => SetFontSize(Settings.FontSizePt + Step);

    [RelayCommand]
    private void DecreaseFontSize() => SetFontSize(Settings.FontSizePt - Step);

    /// <summary>"Ninguno" (null) is black: every service starts dark until something is put up.</summary>
    [RelayCommand]
    private void SelectBackground(DefaultBackgroundOptionViewModel option) => Save(Settings with { DefaultBackgroundId = option.Id });

    public void SetFontSize(int pt) =>
        Save(Settings with { FontSizePt = Math.Clamp(pt, ProjectionSettings.MinFontSizePt, ProjectionSettings.MaxFontSizePt) });

    private void Save(ProjectionSettings updated)
    {
        if (updated == Settings)
        {
            return;
        }

        var previous = Settings;
        ErrorMessage = null;
        Apply(updated);
        SaveTask = SaveAfter(SaveTask, previous, updated);
    }

    // Saves run one after another so a slower earlier write can never overwrite a newer one.
    private async Task SaveAfter(Task pending, ProjectionSettings previous, ProjectionSettings updated)
    {
        await pending;
        try
        {
            await _repository.SaveAsync(updated);
        }
        catch (Exception)
        {
            // Revert only if nothing newer was chosen meanwhile.
            if (Settings == updated)
            {
                Apply(previous);
            }

            ErrorMessage = "Algo salió mal. Inténtalo de nuevo.";
        }
    }

    private void Apply(ProjectionSettings settings)
    {
        Settings = settings;
        ProjectionTypography.Set(settings);
        Sync();
    }

    private void Sync()
    {
        foreach (var font in Fonts)
        {
            font.IsSelected = font.Family == Settings.FontFamily;
        }

        foreach (var size in Sizes)
        {
            size.IsSelected = size.Size == Settings.FontSizePt;
        }

        foreach (var background in Backgrounds)
        {
            background.IsSelected = background.Id == Settings.DefaultBackgroundId;
        }

        OnPropertyChanged(nameof(PreviewFrame));
    }
}
