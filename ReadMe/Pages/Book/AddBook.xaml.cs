using ReadMe.Services;
using VersOne.Epub;

namespace ReadMe.Pages.Book;

public partial class AddBook : ContentPage
{
    private readonly BookService _bookService;

    // Livre temporaire importé mais pas encore ajouté
    private Models.Book? _importedBook;
    private bool _menuVisible = false;

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

            // Lecture du fichier EPUB en bytes
            using var stream = await result.OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var epubBytes = ms.ToArray();

            // Lecture de l'EPUB via un MemoryStream
            using var epubStream = new MemoryStream(epubBytes);
            EpubBook epubBook = EpubReader.ReadBook(epubStream);

            // Nombre de "pages" = nombre de sections
            int pageCount = epubBook.ReadingOrder.Count;

            // Extraction des métadonnées (pour l'instant basique)
            var (title, author, coverBytes) = await ExtractEpubMetadata(epubBytes, result.FileName);

            // Création du livre
            _importedBook = new Models.Book(
                title: title,
                author: author,
                epubContent: epubBytes,
                coverImage: coverBytes
            );

            // On remplit PageCount
            _importedBook.PageCount = pageCount;

            await DisplayAlert(
                "Import réussi",
                "Le manuscrit a été importé. Maintenant il faut l'encoder dans la Mémoire Sacrée.",
                "OK"
            );
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
            await DisplayAlert(
                    "Anomalie du Cogitator",
                    "Aucun manuscrit n'a été détecté. Initiez d'abord le Rite d'Acquisition.",
                    "Ave Omnissiah"
                );

            return;
        }

        _bookService.AddDeck(_importedBook);
        await DisplayAlert(
                "Rituel Accompli",
                "Le manuscrit a été sanctifié et inscrit dans la Mémoire Sacrée.",
                "Gloire au Machine-Esprit"
            );


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

    private async void OnBorderTapped(object sender, EventArgs e)
    {
        const uint animationSpeed = 250;

        if (_menuVisible)
        {
            await Task.WhenAll(
                BtnImport.TranslateTo(-220, 0, animationSpeed, Easing.SinInOut),
                BtnAdd.TranslateTo(-220, 0, animationSpeed, Easing.SinInOut)
            );
        }
        else
        {
            await Task.WhenAll(
                BtnImport.TranslateTo(0, 0, animationSpeed, Easing.SinInOut),
                BtnAdd.TranslateTo(0, 0, animationSpeed, Easing.SinInOut)
            );
        }

        _menuVisible = !_menuVisible;
    }

}
