using ReadMe.Services;

namespace ReadMe.Pages.Book;

public partial class AddBook : ContentPage
{
    private readonly BookService _bookService;

    // Livre temporaire importé mais pas encore ajouté
    private Models.Book? _importedBook;

    public AddBook(BookService bookService)
    {
        InitializeComponent();
        _bookService = bookService;
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        if (Navigation.NavigationStack.Count > 1)
            await Navigation.PopAsync();
    }

    /// <summary>
    /// Ouvre le sélecteur de fichiers et importe un EPUB depuis le téléphone.
    /// </summary>
    private async void ImportBook(object sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Sélectionner un fichier EPUB",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.Android, new[] { "application/epub+zip", ".epub" } },
                    { DevicePlatform.iOS, new[] { "org.idpf.epub-container", ".epub" } },
                    { DevicePlatform.WinUI, new[] { ".epub" } }
                })
            });

            if (result == null)
                return;

            // Lecture du fichier EPUB
            using var stream = await result.OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var epubBytes = ms.ToArray();

            // Extraction des métadonnées
            var (title, author, coverBytes) = await ExtractEpubMetadata(epubBytes, result.FileName);

            // Création du livre mais PAS encore ajouté
            _importedBook = new Models.Book(
                title: title,
                author: author,
                epubContent: epubBytes,
                coverImage: coverBytes
            );

            await DisplayAlert("Import réussi", "Le livre a été importé. Cliquez sur 'Créer le deck' pour l'ajouter.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erreur", $"Impossible d'importer le fichier : {ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Ajoute le livre importé dans le BookService.
    /// </summary>
    private async void OnAddBookClicked(object sender, EventArgs e)
    {
        if (_importedBook == null)
        {
            await DisplayAlert("Erreur", "Aucun livre importé. Cliquez d'abord sur 'Parcourir'.", "OK");
            return;
        }

        _bookService.AddDeck(_importedBook);
        await DisplayAlert("Succès", "Le livre a été ajouté à votre bibliothèque.", "OK");

        // Optionnel : reset
        _importedBook = null;

        // Retour à la page précédente
        await Navigation.PopAsync();
    }

    private Task<(string title, string author, byte[] cover)> ExtractEpubMetadata(byte[] epubBytes, string fileName)
    {
        string title = Path.GetFileNameWithoutExtension(fileName);
        string author = "Auteur inconnu";
        byte[] cover = Array.Empty<byte>();

        return Task.FromResult((title, author, cover));
    }
}
