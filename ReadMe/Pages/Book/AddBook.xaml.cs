using MySqlConnector;
using ReadMe.Services;
using System.Text.RegularExpressions;
using VersOne.Epub;

namespace ReadMe.Pages.Book;

public partial class AddBook : ContentPage
{
    private readonly BookService _bookService;

    // Livre importé (local ou DB), prêt à être "encodé" dans la mémoire locale
    private Models.Book? _importedBook;

    // Etat du menu animé
    private bool _menuVisible;

    private const string CONNECTION_STRING =
        "Server=10.0.2.2;Port=3306;Database=appdb;User ID=root;Password=root;";

    private const string TABLE_NAME = "book";

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

    // ----------------------------
    // IMPORT LOCAL (fichier EPUB)
    // ----------------------------

    /// <summary>
    /// Ouvre le sélecteur de fichiers et importe un EPUB depuis le téléphone.
    /// Le livre est stocké en mémoire dans <see cref="_importedBook"/> jusqu'au clic sur "Ajouter".
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

            byte[] epubBytes = await ReadAllBytesAsync(result);
            _importedBook = await CreateImportedBookFromEpubAsync(
                epubBytes: epubBytes,
                fallbackFileName: result.FileName,
                tagFromDb: null,
                lastPageReadFromDb: 0
            );

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

    // ----------------------------------------
    // IMPORT ONLINE (MySQL -> EPUB en LONGBLOB)
    // ----------------------------------------

    /// <summary>
    /// Importe un livre depuis MySQL.
    /// Ordre DB (selon ta table) : id, epub (LONGBLOB), tag (text), page_de_lecture (int).
    /// </summary>
    private async void ImportBookOnline(object sender, EventArgs e)
    {
        try
        {
            string? title = await DisplayPromptAsync(
                                    "Datavault",
                                    "Entrez le TITRE du livre à extraire (colonne title).",
                                    accept: "Extraire",
                                    cancel: "Annuler"
                                );

            if (string.IsNullOrWhiteSpace(title))
                return;

            title = title.Trim();

            // Lecture DB (title, epub, tag, page_de_lecture)
            (byte[] epubBytes, string? tag, int lastPage) = await FetchBookFromDatabaseAsync(title);

            // Rebuild du modèle Book à partir de l'epub
            _importedBook = await CreateImportedBookFromEpubAsync(
                epubBytes: epubBytes,
                fallbackFileName: $"{title}.epub",
                tagFromDb: tag,
                lastPageReadFromDb: lastPage
            );

            await DisplayAlert(
                "Datavault consulté",
                "Le manuscrit a été extrait du Datavault. Tu peux maintenant l'encoder dans la Mémoire Sacrée.",
                "OK"
            );
        }
        catch (InvalidOperationException ex)
        {
            // Erreurs "logiques" (ex: pas trouvé)
            await DisplayAlert("Datavault", ex.Message, "OK");
        }
        catch (MySqlException ex)
        {
            await DisplayAlert("Erreur MySQL", ex.Message, "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erreur", $"Import impossible : {ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Récupère depuis MySQL un livre sous forme binaire (epub) + tag + progression.
    /// </summary>
    private static async Task<(byte[] epubBytes, string? tag, int lastPageRead)> FetchBookFromDatabaseAsync(string title)
    {
        string sql = $@"
                    SELECT title, epub, tag, page_de_lecture
                    FROM {TABLE_NAME}
                    WHERE title = @title
                    LIMIT 1;";

        await using var conn = new MySqlConnection(CONNECTION_STRING);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@title", title);

        await using var reader = await cmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            throw new InvalidOperationException("Aucun manuscrit trouvé pour ce titre.");

        // epub: LONGBLOB
        int epubOrdinal = reader.GetOrdinal("epub");
        if (reader.IsDBNull(epubOrdinal))
            throw new InvalidOperationException("Le champ EPUB est vide en base de données.");

        byte[] epubBytes = (byte[])reader["epub"];

        string? tag = reader["tag"] as string;

        int lastPage = 0;
        int pageOrdinal = reader.GetOrdinal("page_de_lecture");
        if (!reader.IsDBNull(pageOrdinal))
        {
            // page_de_lecture est "unsigned int" côté DB sur ton screenshot -> on convert proprement
            lastPage = Convert.ToInt32(reader["page_de_lecture"]);
            if (lastPage < 0) lastPage = 0;
        }

        return (epubBytes, tag, lastPage);
    }

    // ----------------------------
    // AJOUT LOCAL (BookService)
    // ----------------------------

    /// <summary>
    /// Ajoute le livre importé dans le BookService (collection locale de l'app).
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

        _importedBook = null;
        await Navigation.PopAsync();
    }

    // ----------------------------
    // UI / MENU ANIMÉ
    // ----------------------------

    private async void OnBorderTapped(object sender, EventArgs e)
    {
        const uint animationSpeed = 250;

        // Coord X fermé = caché, ouvert = 0
        double closedX = -220;
        double openedX = 0;

        if (_menuVisible)
        {
            await Task.WhenAll(
                BtnImport.TranslateTo(closedX, 0, animationSpeed, Easing.SinInOut),
                BtnImportOnline.TranslateTo(closedX, 0, animationSpeed, Easing.SinInOut),
                BtnAdd.TranslateTo(closedX, 0, animationSpeed, Easing.SinInOut)
            );
        }
        else
        {
            await Task.WhenAll(
                BtnImport.TranslateTo(openedX, 0, animationSpeed, Easing.SinInOut),
                BtnImportOnline.TranslateTo(openedX, 0, animationSpeed, Easing.SinInOut),
                BtnAdd.TranslateTo(openedX, 0, animationSpeed, Easing.SinInOut)
            );
        }

        _menuVisible = !_menuVisible;
    }

    // ----------------------------
    // HELPERS (lecture + parsing EPUB)
    // ----------------------------

    /// <summary>
    /// Lit un fichier choisi via FilePicker en mémoire (byte[]).
    /// </summary>
    private static async Task<byte[]> ReadAllBytesAsync(FileResult file)
    {
        await using var stream = await file.OpenReadAsync();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Construit un <see cref="Models.Book"/> depuis des bytes EPUB.
    /// - Lit l'EPUB pour estimer un PageCount (heuristique)
    /// - Applique un tag DB si présent
    /// - Applique la progression de lecture si fournie
    /// </summary>
    private static async Task<Models.Book> CreateImportedBookFromEpubAsync(
        byte[] epubBytes,
        string fallbackFileName,
        string? tagFromDb,
        int lastPageReadFromDb)
    {
        // Lire l'EPUB
        EpubBook epubBook;
        using (var epubStream = new MemoryStream(epubBytes))
        {
            epubBook = EpubReader.ReadBook(epubStream);
        }

        int pageCount = EstimateEpubPages(epubBook);

        // Métadonnées (simple pour l'instant)
        var (title, author, coverBytes) = await ExtractEpubMetadataAsync(epubBytes, fallbackFileName);

        var book = new Models.Book(
            title: title,
            author: author,
            epubContent: epubBytes,
            coverImage: coverBytes
        );

        book.PageCount = pageCount;
        book.LastPageRead = Math.Max(0, lastPageReadFromDb);

        // Tag DB (ta table a 1 champ texte)
        if (!string.IsNullOrWhiteSpace(tagFromDb))
            book.Tags.Add(new Models.Tag(tagFromDb.Trim()));

        return book;
    }

    /// <summary>
    /// Estimation simple des pages : on convertit chaque section XHTML en texte
    /// puis on applique une heuristique (mots/page).
    /// </summary>
    private static int EstimateEpubPages(EpubBook epubBook)
    {
        // Page 1 = cover / page de garde
        int total = 1;

        // Heuristique : ~280-320 mots par page "livre" sur mobile
        const int wordsPerPage = 300;

        foreach (var xhtml in epubBook.ReadingOrder)
        {
            if (string.IsNullOrWhiteSpace(xhtml?.Content))
                continue;

            string text = StripHtmlToText(xhtml.Content);
            int words = CountWords(text);

            // au minimum 1 page par section
            int pages = Math.Max(1, (int)Math.Ceiling(words / (double)wordsPerPage));
            total += pages;
        }

        return Math.Max(1, total);
    }

    /// <summary>
    /// Nettoyage HTML -> texte brut pour compter des mots.
    /// </summary>
    private static string StripHtmlToText(string html)
    {
        html = Regex.Replace(html, "<script.*?</script>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        html = Regex.Replace(html, "<style.*?</style>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        html = Regex.Replace(html, "<.*?>", " ", RegexOptions.Singleline);
        html = System.Net.WebUtility.HtmlDecode(html);
        return Regex.Replace(html, "\\s+", " ").Trim();
    }

    /// <summary>
    /// Compte des mots = séquences lettres/chiffres Unicode.
    /// </summary>
    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        return Regex.Matches(text, "[\\p{L}\\p{N}]+").Count;
    }

    /// <summary>
    /// Extraction minimale (placeholder) : tu pourras ensuite lire Title/Author/Cover depuis epubBook.Metadata.
    /// </summary>
    private static Task<(string title, string author, byte[] cover)> ExtractEpubMetadataAsync(byte[] epubBytes, string fileName)
    {
        string title = Path.GetFileNameWithoutExtension(fileName);
        string author = "Auteur inconnu";
        byte[] cover = Array.Empty<byte>();

        return Task.FromResult((title, author, cover));
    }
}