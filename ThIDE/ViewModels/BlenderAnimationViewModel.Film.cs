// What the Storyboard tab says of the film as a whole, beside its shots: the title and the end
// card, the language of the texts the film writes itself (a tour's), and saving the film as a
// composite preset of the user's, which the list of composites then offers.
//
// The VM holds no film logic: OverlaySpec, StoryboardExpander and PresetStore compute.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Blender;
using Therion.Blender.Plan;
using Therion.Blender.Presets;
using ThIDE.Resources;

namespace ThIDE.ViewModels;

public sealed partial class BlenderAnimationViewModel
{
    // ---- the title and the end card ----

    /// <summary>The film's title; blank is none. <c>{cave}</c> is the cave's name.</summary>
    public string FilmTitle
    {
        get => _filmTexts?.Title ?? "";
        set => SetFilmTexts(t => t with { Title = Text(value) });
    }

    /// <summary>A smaller line under the title.</summary>
    public string FilmSubtitle
    {
        get => _filmTexts?.Subtitle ?? "";
        set => SetFilmTexts(t => t with { Subtitle = Text(value) });
    }

    /// <summary>How long the title stands as a card in the middle of the picture; 0 keeps it at
    /// the top throughout.</summary>
    public double FilmTitleSeconds
    {
        get => _filmTexts?.TitleSeconds ?? 0;
        set => SetFilmTexts(t => t with { TitleSeconds = value > 0 && double.IsFinite(value) ? value : null });
    }

    /// <summary>What the end card says; blank is no end card.</summary>
    public string FilmEndCardText
    {
        get => _filmTexts?.EndCard?.Text ?? "";
        set => SetFilmTexts(t => t with { EndCard = Text(value) is { } text ? (t.EndCard ?? new EndCardSpec()) with { Text = text } : null });
    }

    /// <summary>A smaller line under the end card's text.</summary>
    public string FilmEndCardSubtitle
    {
        get => _filmTexts?.EndCard?.Subtitle ?? "";
        set => SetFilmTexts(t => t.EndCard is { } card ? t with { EndCard = card with { Subtitle = Text(value) } } : t);
    }

    /// <summary>How long the end card stays (seconds).</summary>
    public double FilmEndCardSeconds
    {
        get => _filmTexts?.EndCard?.Seconds ?? new EndCardSpec().Seconds;
        set => SetFilmTexts(t => t.EndCard is { } card && value > 0 && double.IsFinite(value) ? t with { EndCard = card with { Seconds = value } } : t);
    }

    public bool HasFilmEndCard => _filmTexts?.EndCard is not null;

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private void SetFilmTexts(Func<OverlaySpec, OverlaySpec> change)
    {
        var texts = change(_filmTexts ?? new OverlaySpec());
        if (texts == _filmTexts) return;
        _filmTexts = IsBlank(texts) ? null : texts;
        FilmTextsChanged();
        RevalidateStoryboard();
    }

    private static readonly IReadOnlyList<CaptionSpec> NoCaptions = [];

    /// <summary>True when the texts say nothing that the default texts do not: the film then has
    /// none. (Two empty lists are not equal as a record compares them, so both are made one.)</summary>
    private static bool IsBlank(OverlaySpec t)
        => t.Captions.Count == 0 && t.CornerCaptions.Count == 0
           && t with { Captions = NoCaptions, CornerCaptions = NoCaptions } == new OverlaySpec { Captions = NoCaptions, CornerCaptions = NoCaptions };

    private void FilmTextsChanged()
    {
        OnPropertyChanged(nameof(FilmTitle));
        OnPropertyChanged(nameof(FilmSubtitle));
        OnPropertyChanged(nameof(FilmTitleSeconds));
        OnPropertyChanged(nameof(FilmEndCardText));
        OnPropertyChanged(nameof(FilmEndCardSubtitle));
        OnPropertyChanged(nameof(FilmEndCardSeconds));
        OnPropertyChanged(nameof(HasFilmEndCard));
    }

    // ---- the language of the film's own texts ----

    /// <summary>The languages a film's own texts can be written in; the first follows the interface.</summary>
    public IReadOnlyList<FilmLanguageItem> FilmLanguages { get; } =
    [
        new(null, "Blender_UI_FilmLanguageUi"),
        new("en", "Blender_UI_FilmLanguageEn"),
        new("ro", "Blender_UI_FilmLanguageRo"),
    ];

    private FilmLanguageItem? _selectedFilmLanguage;

    /// <summary>The language the film says it is in; the first item leaves it to the interface.</summary>
    public FilmLanguageItem SelectedFilmLanguage
    {
        get => _selectedFilmLanguage ?? FilmLanguages[0];
        set
        {
            if (value == SelectedFilmLanguage) return;
            _selectedFilmLanguage = value;
            OnPropertyChanged();
            BranchTableStale();
        }
    }

    private string? FilmLanguage => SelectedFilmLanguage.Code;

    private void SetFilmLanguage(string? code)
    {
        _selectedFilmLanguage = FilmLanguages.FirstOrDefault(l => l.Code == code) ?? FilmLanguages[0];
        OnPropertyChanged(nameof(SelectedFilmLanguage));
    }

    /// <summary>The interface's language, when a film can be written in it; else English.</summary>
    internal static string InterfaceFilmLanguage
        => Tr.Culture.TwoLetterISOLanguageName is var code && TourBuilder.Languages.Contains(code, StringComparer.Ordinal) ? code : "en";

    // ---- saving the film as a composite ----

    /// <summary>The name the film is saved under as a composite preset.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompositeNameProblem))]
    [NotifyCanExecuteChangedFor(nameof(SaveCompositeCommand))]
    private string _newCompositeName = "";

    [ObservableProperty] private string _newCompositeDescription = "";

    /// <summary>Why the name cannot be saved under; null when it can.</summary>
    public string? CompositeNameProblem
        => PresetStore.NameProblem(NewCompositeName, composite: true) is Therion.Blender.Presets.PresetNameProblem.BuiltIn ? Tr.Get("Blender_Composite_NameBuiltIn") : null;

    private bool CanSaveComposite => _presetStore is not null && Shots.Count > 0 && StoryboardBlockers.Count == 0
                                     && !string.IsNullOrWhiteSpace(NewCompositeName) && CompositeNameProblem is null;

    /// <summary>
    /// Saves the film as a composite preset of the user's (one of the same name is replaced). The
    /// user's presets it names are written into its shots, so that it renders the same where only
    /// the built-in presets are known (the command line, another machine); where it is written is
    /// for whoever renders it. Its language is saved only when one was chosen.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSaveComposite))]
    private void SaveComposite()
    {
        if (_presetStore is null) return;
        string name = NewCompositeName.Trim();
        var film = StoryboardExpander.InlinePresets(CurrentStoryboard, FindPreset);
        var composite = new CompositePreset
        {
            Name = name,
            Description = Text(NewCompositeDescription?.Trim()),
            Storyboard = film with { Output = film.Output with { OutputDirectory = "" } },
        };
        try { _presetStore.SaveComposite(composite); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            LastError = ex.Message.Split(" (Parameter", 2)[0];
            return;
        }
        LoadComposites();
        SelectedComposite = Composites.LastOrDefault(c => !c.Composite.BuiltIn && c.Name == name) ?? SelectedComposite;
        StatusMessage = string.Format(CultureInfo.CurrentCulture, Tr.Get("Blender_Status_CompositeSaved"), name);
        NewCompositeName = "";
        NewCompositeDescription = "";
    }
}

/// <summary>A language of the film's own texts in the tab's list; a null code follows the
/// interface. Its label is in the interface's language of the moment.</summary>
public sealed record FilmLanguageItem(string? Code, string LabelKey)
{
    public string Label => Tr.Get(LabelKey);

    public override string ToString() => Label;
}
