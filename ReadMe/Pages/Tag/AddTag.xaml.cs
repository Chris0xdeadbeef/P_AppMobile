using ReadMe.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace ReadMe.Pages.Tag;

public partial class AddTag : ContentPage, INotifyPropertyChanged
{
    private readonly Models.Book _book;
    private readonly TagService _tagService;

    // Liste "source" (tous les tags, avec état coché)
    public ObservableCollection<TagChoice> AvailableTags { get; } = [];

    // Liste affichée (filtrée)
    public ObservableCollection<TagChoice> FilteredTags { get; } = [];

    // --- Recherche / Catégories ---
    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value) return;
            _searchText = value ?? "";
            OnPropertyChanged();
            ApplyFilters();
        }
    }

    private TagCategory _selectedCategory = TagCategory.All;
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

    // --- Commands pour le XAML ---
    public ICommand SetCategoryCommand { get; }
    public ICommand CheckAllCommand { get; }
    public ICommand UncheckAllCommand { get; }

    public AddTag(Models.Book book, TagService tagService)
    {
        InitializeComponent();

        _book = book;
        _tagService = tagService;

        SetCategoryCommand = new Command<string>(cat =>
        {
            SelectedCategory = ParseCategory(cat);
        });

        // ces actions s’appliquent à la liste filtrée (ce que l’utilisateur voit)
        CheckAllCommand = new Command(() =>
        {
            foreach (var item in FilteredTags)
                item.IsSelected = true;
        });

        UncheckAllCommand = new Command(() =>
        {
            foreach (var item in FilteredTags)
                item.IsSelected = false;
        });

        BuildChecklist();
        ApplyFilters();

        BindingContext = this;
    }

    /// <summary>
    /// Construit AvailableTags depuis TagService et pré-coche ceux déjà sur le livre.
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
            var name = (tag.Name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            AvailableTags.Add(new TagChoice(tag)
            {
                IsSelected = alreadyOnBook.Contains(name)
            });
        }
    }

    /// <summary>
    /// Filtre par texte + catégorie.
    /// </summary>
    private void ApplyFilters()
    {
        string q = (SearchText ?? "").Trim();

        IEnumerable<TagChoice> query = AvailableTags;

        // Catégorie
        if (SelectedCategory != TagCategory.All)
            query = query.Where(t => GetCategory(t) == SelectedCategory);

        // Recherche
        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(t =>
                t.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        // Rebuild collection (simple et fiable)
        FilteredTags.Clear();
        foreach (var item in query.OrderBy(t => t.Name))
            FilteredTags.Add(item);
    }

    /// <summary>
    /// Ajoute tous les tags cochés au livre (sans doublons), puis revient en arrière.
    /// </summary>
    private async void OnAddTagsClicked(object sender, EventArgs e)
    {
        var existing = new HashSet<string>(
            _book.Tags.Select(t => (t.Name ?? "").Trim()),
            StringComparer.OrdinalIgnoreCase
        );

        int added = 0;

        // On parcourt AvailableTags (pas FilteredTags), pour prendre en compte
        // les choix même si l'utilisateur change les filtres.
        foreach (var choice in AvailableTags)
        {
            if (!choice.IsSelected)
                continue;

            string name = (choice.Tag.Name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

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
    // Catégories (heuristiques)
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

    private static TagCategory ParseCategory(string? cat)
    {
        cat = (cat ?? "").Trim();
        return cat.ToLowerInvariant() switch
        {
            "imperium" => TagCategory.Imperium,
            "spacemarines" => TagCategory.SpaceMarines,
            "admech" => TagCategory.AdMech,
            "chaos" => TagCategory.Chaos,
            "xenos" => TagCategory.Xenos,
            "themes" => TagCategory.Themes,
            "all" => TagCategory.All,
            _ => TagCategory.All
        };
    }

    /// <summary>
    /// Devine une catégorie selon le nom du tag.
    /// Comme ton Tag n'a pas de "Category", on fait une heuristique stable.
    /// </summary>
    private static TagCategory GetCategory(TagChoice t)
    {
        string n = (t.Name ?? "").Trim().ToLowerInvariant();

        // AdMech
        if (n.Contains("adeptus mechanicus") || n.Contains("admech") || n.Contains("mechanicus") || n.Contains("skitarii"))
            return TagCategory.AdMech;

        // Space Marines
        if (n.Contains("space marines") || n.Contains("adeptus astartes") || n.Contains("astartes") ||
            n.Contains("ultramarines") || n.Contains("blood angels") || n.Contains("dark angels") ||
            n.Contains("space wolves") || n.Contains("black templars") || n.Contains("imperial fists") ||
            n.Contains("salamanders") || n.Contains("raven guard") || n.Contains("white scars") ||
            n.Contains("deathwatch") || n.Contains("grey knights"))
            return TagCategory.SpaceMarines;

        // Imperium (hors SM/AdMech)
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

        // Thèmes / général
        return TagCategory.Themes;
    }

    // ----------------------------
    // INotifyPropertyChanged
    // ----------------------------

    public new event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    // ----------------------------
    // Checklist item (notifie UI)
    // ----------------------------

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

        public TagChoice(Models.Tag tag)
        {
            Tag = tag;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}