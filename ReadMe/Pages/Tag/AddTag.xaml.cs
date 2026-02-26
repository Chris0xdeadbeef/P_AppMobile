using ReadMe.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace ReadMe.Pages.Tag;

/// <summary>
/// Page "AddTag" : permet d'assigner plusieurs tags à un livre via une checklist.
/// UX:
/// - recherche par texte
/// - filtres par catégorie (heuristique basée sur le nom du tag)
/// - actions "tout cocher / tout décocher" appliquées à la liste filtrée (ce que l'utilisateur voit)
/// - validation: ajoute les tags cochés au livre sans doublons
/// </summary>
public partial class AddTag : ContentPage, INotifyPropertyChanged
{
    private readonly Models.Book _book;
    private readonly TagService _tagService;

    /// <summary>
    /// Liste complète (source) : tous les tags existants avec l'état "coché".
    /// On ne bind PAS directement l'UI dessus, car l'utilisateur filtre/recherche.
    /// </summary>
    public ObservableCollection<TagChoice> AvailableTags { get; } = [];

    /// <summary>
    /// Liste affichée dans l'UI : sous-ensemble filtré (catégorie + recherche).
    /// </summary>
    public ObservableCollection<TagChoice> FilteredTags { get; } = [];

    // ----------------------------
    // Recherche / Catégorie
    // ----------------------------

    private string _searchText = string.Empty;

    /// <summary>
    /// Texte de recherche saisi par l'utilisateur.
    /// A chaque modification, on recalculte la liste FilteredTags.
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            value ??= string.Empty;
            if (_searchText == value) return;

            _searchText = value;
            OnPropertyChanged();
            ApplyFilters();
        }
    }

    private TagCategory _selectedCategory = TagCategory.All;

    /// <summary>
    /// Catégorie active (onglet / filtre).
    /// </summary>
    public TagCategory SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (_selectedCategory == value) return;

            _selectedCategory = value;
            OnPropertyChanged();
            ApplyFilters();
        }
    }

    // ----------------------------
    // Commands (utilisés dans le XAML)
    // ----------------------------

    public ICommand SetCategoryCommand { get; }
    public ICommand CheckAllCommand { get; }
    public ICommand UncheckAllCommand { get; }

    public AddTag(Models.Book book, TagService tagService)
    {
        InitializeComponent();

        _book = book;
        _tagService = tagService;

        // Change la catégorie depuis un string ("imperium", "xenos", etc.)
        SetCategoryCommand = new Command<string>(cat => SelectedCategory = ParseCategory(cat));

        // "Tout cocher / décocher" : appliqué uniquement à la liste affichée.
        CheckAllCommand = new Command(() => SetSelectionOnFiltered(true));
        UncheckAllCommand = new Command(() => SetSelectionOnFiltered(false));

        // Prépare la source + applique le filtrage initial
        BuildChecklist();
        ApplyFilters();

        BindingContext = this;
    }

    // ----------------------------
    // Data build / Filtering
    // ----------------------------

    /// <summary>
    /// Construit la liste source (AvailableTags) à partir du TagService.
    /// Pré-coche les tags déjà présents sur le livre.
    /// </summary>
    private void BuildChecklist()
    {
        AvailableTags.Clear();

        var alreadyOnBook = new HashSet<string>(
            _book.Tags.Select(t => (t.Name ?? "").Trim()),
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var tag in _tagService.GetAll())
        {
            string name = (tag.Name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            AvailableTags.Add(new TagChoice(tag)
            {
                IsSelected = alreadyOnBook.Contains(name)
            });
        }
    }

    /// <summary>
    /// Recalcule FilteredTags depuis AvailableTags selon:
    /// - catégorie
    /// - texte de recherche
    /// </summary>
    private void ApplyFilters()
    {
        string queryText = (SearchText ?? "").Trim();

        IEnumerable<TagChoice> query = AvailableTags;

        if (SelectedCategory != TagCategory.All)
            query = query.Where(t => GetCategoryFromName(t.Name) == SelectedCategory);

        if (!string.IsNullOrWhiteSpace(queryText))
            query = query.Where(t => t.Name.Contains(queryText, StringComparison.OrdinalIgnoreCase));

        FilteredTags.Clear();
        foreach (var item in query.OrderBy(t => t.Name))
            FilteredTags.Add(item);
    }

    /// <summary>
    /// Coche/décoche tous les éléments actuellement visibles (FilteredTags).
    /// </summary>
    private void SetSelectionOnFiltered(bool selected)
    {
        foreach (var item in FilteredTags)
            item.IsSelected = selected;
    }

    // ----------------------------
    // Actions UI
    // ----------------------------

    /// <summary>
    /// Ajoute au livre tous les tags cochés (sans doublons).
    /// Important: on parcourt AvailableTags (la source) pour ne pas perdre
    /// des sélections si l'utilisateur change les filtres avant de valider.
    /// </summary>
    private async void OnAddTagsClicked(object sender, EventArgs e)
    {
        var existing = new HashSet<string>(
            _book.Tags.Select(t => (t.Name ?? "").Trim()),
            StringComparer.OrdinalIgnoreCase
        );

        int added = 0;

        foreach (var choice in AvailableTags)
        {
            if (!choice.IsSelected) continue;

            string name = (choice.Tag.Name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            if (existing.Add(name))
            {
                _book.Tags.Add(new Models.Tag(name));
                added++;
            }
        }

        await DisplayAlert(
            "Rite accompli",
            added > 0 ? $"{added} sigil(s) gravé(s) sur le Tome." : "Aucun nouveau sigil à graver.",
            "Ave Omnissiah"
        );

        await Navigation.PopAsync();
    }

    /// <summary>
    /// Retour à la page précédente.
    /// </summary>
    private async void OnBackClicked(object sender, EventArgs e)
    {
        if (Navigation.NavigationStack.Count > 1)
            await Navigation.PopAsync();
    }

    // ----------------------------
    // Catégories (heuristique)
    // ----------------------------

    public enum TagCategory
    {
        All,
        Imperium,
        SpaceMarines,
        AdMech,
        Chaos,
        Xenos,
        Themes
    }

    private static TagCategory ParseCategory(string? value)
    {
        value = (value ?? "").Trim().ToLowerInvariant();
        return value switch
        {
            "imperium" => TagCategory.Imperium,
            "spacemarines" => TagCategory.SpaceMarines,
            "admech" => TagCategory.AdMech,
            "chaos" => TagCategory.Chaos,
            "xenos" => TagCategory.Xenos,
            "themes" => TagCategory.Themes,
            _ => TagCategory.All
        };
    }

    /// <summary>
    /// Déduit une catégorie à partir du texte du tag.
    /// (Ton modèle Tag n'a pas de champ "Category".)
    /// </summary>
    private static TagCategory GetCategoryFromName(string? tagName)
    {
        string n = (tagName ?? "").Trim().ToLowerInvariant();

        // Adeptus Mechanicus
        if (n.Contains("adeptus mechanicus") || n.Contains("admech") || n.Contains("mechanicus") || n.Contains("skitarii"))
            return TagCategory.AdMech;

        // Space Marines
        if (n.Contains("space marines") || n.Contains("adeptus astartes") || n.Contains("astartes") ||
            n.Contains("ultramarines") || n.Contains("blood angels") || n.Contains("dark angels") ||
            n.Contains("space wolves") || n.Contains("black templars") || n.Contains("imperial fists") ||
            n.Contains("salamanders") || n.Contains("raven guard") || n.Contains("white scars") ||
            n.Contains("deathwatch") || n.Contains("grey knights"))
            return TagCategory.SpaceMarines;

        // Imperium (hors SM / AdMech)
        if (n.Contains("imperium") || n.Contains("imperial") || n.Contains("astra militarum") || n.Contains("guard") ||
            n.Contains("adepta sororitas") || n.Contains("sororitas") || n.Contains("sisters of battle") ||
            n.Contains("custodes") || n.Contains("inquisition") || n.Contains("knights") ||
            n.Contains("rogue trader") || n.Contains("navis") || n.Contains("officio assassinorum") ||
            n.Contains("ecclesiarchy"))
            return TagCategory.Imperium;

        // Chaos
        if (n.Contains("chaos") || n.Contains("heretic") || n.Contains("traitor") ||
            n.Contains("black legion") || n.Contains("death guard") || n.Contains("thousand sons") ||
            n.Contains("world eaters") || n.Contains("emperors children") ||
            n.Contains("khorne") || n.Contains("nurgle") || n.Contains("tzeentch") || n.Contains("slaanesh") ||
            n.Contains("daemons") || n.Contains("daemon") || n.Contains("chaos knights"))
            return TagCategory.Chaos;

        // Xenos
        if (n.Contains("xenos") || n.Contains("necron") || n.Contains("aeldari") || n.Contains("eldar") ||
            n.Contains("drukhari") || n.Contains("ork") || n.Contains("t'au") || n.Contains("tau") ||
            n.Contains("tyranid") || n.Contains("genestealer") || n.Contains("votann"))
            return TagCategory.Xenos;

        return TagCategory.Themes;
    }

    // ----------------------------
    // INotifyPropertyChanged
    // ----------------------------

    public new event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    // ----------------------------
    // Checklist Item
    // ----------------------------

    /// <summary>
    /// Représente une ligne de checklist:
    /// - Tag d'origine
    /// - bool IsSelected qui notifie l'UI quand il change
    /// </summary>
    public sealed class TagChoice : INotifyPropertyChanged
    {
        public Models.Tag Tag { get; }

        public string Name => Tag.Name;

        private bool _isSelected;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public TagChoice(Models.Tag tag) => Tag = tag;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}