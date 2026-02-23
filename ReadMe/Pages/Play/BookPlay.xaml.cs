using System.Text;
using System.Text.RegularExpressions;
using VersOne.Epub;

namespace ReadMe.Pages.Play;

/// <summary>
/// Page de lecture d'un livre (EPUB) avec pagination "réelle" en colonnes via WebView.
/// - Affiche d'abord la couverture (tap pour commencer).
/// - Charge l'EPUB en arrière-plan, découpe en sections (ReadingOrder).
/// - Chaque section est transformée en HTML autonome (CSS inline + assets en data URI).
/// - La pagination est calculée côté JS (colonnes horizontales) et agrégée sur tout le livre.
/// - Les swipes changent de page/section avec une animation de flip.
/// </summary>
public partial class BookPlay : ContentPage
{
    /// <summary>Le livre local (contient l'EPUB binaire + la couverture + progression).</summary>
    private readonly Models.Book _book;

    /// <summary>Représentation EPUB parsée par VersOne.Epub.</summary>
    private EpubBook? _epub;

    /// <summary>Liste des sections (chapitres/fichiers XHTML) dans l'ordre de lecture.</summary>
    private readonly List<Section> _sections = [];

    /// <summary>Indique si l'initialisation (lecture EPUB + sections) a déjà été faite.</summary>
    private bool _initialized;

    /// <summary>Verrou anti double-chargement pendant InitializeAsync.</summary>
    private bool _isLoading;

    /// <summary>État courant : couverture affichée ou non.</summary>
    private bool _showingCover = true;

    /// <summary>Index de section courante dans <see cref="_sections"/>.</summary>
    private int _sectionIndex = 0;

    /// <summary>Page courante (0-based) à l'intérieur de la section courante.</summary>
    private int _pageInSection = 0;   // 0-based

    /// <summary>Nombre de pages (colonnes) dans la section courante. Calculé via JS.</summary>
    private int _pagesInSection = 1;  // calculé via JS

    // numérotation (colonnes) sur tout le livre
    /// <summary>Nombre total de pages exactes (cover incluse), recalculé au fil des mesures.</summary>
    private int _totalPagesExact = 1;     // inclut cover

    /// <summary>Page globale actuelle exacte (cover = 1).</summary>
    private int _currentPageExact = 1;   // cover = 1

    /// <summary>WebView principal qui affiche la section en cours (UI visible).</summary>
    private WebView? _webView;

    /// <summary>
    /// Protection : certains événements Navigated peuvent arriver plusieurs fois ;
    /// ce flag garantit qu'on ne traite la mesure qu'une seule fois par chargement.
    /// </summary>
    private bool _webViewLoadedOnce;

    /// <summary>Empêche de lancer plusieurs fois le pré-calcul des pages.</summary>
    private bool _precomputeStarted;

    /// <summary>
    /// Lock pour éviter que plusieurs mesures (precompute) se chevauchent :
    /// indispensable car WebView + JS mesure = ressource partagée.
    /// </summary>
    private readonly SemaphoreSlim _measureLock = new(1, 1);

    /// <summary>
    /// Map (chemin normalisé / nom de fichier seul) -> index de section.
    /// Utilisée pour résoudre les liens internes EPUB.
    /// </summary>
    private readonly Dictionary<string, int> _fileToSectionIndex = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Anchor (fragment) à appliquer après navigation vers une autre section.
    /// On le garde en mémoire car la WebView doit d'abord finir de charger.
    /// </summary>
    private string? _pendingAnchor;

    /// <summary>
    /// Cache du template HTML de lecture (fichier ReaderTemplate.html dans Resources/Raw).
    /// Le lire une seule fois évite de refaire des accès disque pour chaque section.
    /// </summary>
    private string? _readerTemplateHtml;

    /// <summary>
    /// Constructeur : affiche immédiatement la couverture et initialise l'indicateur de pages (1/1).
    /// Le chargement EPUB se fait au OnAppearing pour éviter de bloquer le rendu initial.
    /// </summary>
    public BookPlay(Models.Book book)
    {
        InitializeComponent();
        _book = book;

        DisplayCover(); // affichage immédiat
        UpdatePageIndicatorExact(); // 1/1 au départ
    }

    /// <summary>
    /// À l'apparition de la page : déclenche le chargement si pas encore fait.
    /// </summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (!_initialized)
            _ = InitializeAsync();
    }

    // --------------------
    // INIT / LOAD EPUB (background)
    // --------------------

    /// <summary>
    /// Charge et parse l'EPUB, construit la liste des sections et les maps de navigation interne.
    /// Ensuite :
    /// - met à jour un total minimal (cover + 1 page/section)
    /// - démarre un pré-calcul progressif des pages réelles (mesure WebView)
    /// </summary>
    private async Task InitializeAsync()
    {
        // Évite de relancer si déjà fait ou en cours
        if (_initialized || _isLoading)
            return;

        _isLoading = true;

        try
        {
            // Parsing EPUB en arrière-plan (évite de bloquer UI thread)
            await Task.Run(() =>
            {
                using var ms = new MemoryStream(_book.EpubContent);
                _epub = EpubReader.ReadBook(ms);

                _sections.Clear();
                _fileToSectionIndex.Clear();

                int idx = 0;
                foreach (var xhtml in _epub.ReadingOrder)
                {
                    // On ignore les entrées vides
                    if (string.IsNullOrWhiteSpace(xhtml?.Content))
                        continue;

                    // On récupère une clé de fichier (FileName/Key) pour résoudre les chemins relatifs
                    string key = GetContentFileKey(xhtml);
                    string norm = NormalizeEpubPath(key);
                    string fileOnly = GetFileNameOnly(norm);

                    // Indexation double : chemin complet (normalisé) + nom de fichier seul
                    // Utile car certains EPUB génèrent des liens "chap.xhtml" sans dossier.
                    _fileToSectionIndex[norm] = idx;
                    _fileToSectionIndex[fileOnly] = idx;

                    _sections.Add(new Section
                    {
                        Key = key,
                        RawXhtml = xhtml.Content,
                        Html = null,     // construit en lazy (au premier affichage / mesure)
                        PageCount = 1    // valeur minimale, sera mesurée via JS
                    });

                    idx++;
                }

                // Fallback si aucun contenu valide détecté
                if (_sections.Count == 0)
                {
                    _sections.Add(new Section
                    {
                        Key = "",
                        RawXhtml = BuildFallbackHtml("Aucun contenu EPUB détecté."),
                        Html = null,
                        PageCount = 1
                    });
                }
            });

            _initialized = true;

            // Total minimal : cover + 1 page par section (avant mesures réelles)
            RecomputeTotalPagesExact();
            MainThread.BeginInvokeOnMainThread(UpdatePageIndicatorExact);

            // Lancer le calcul réel (progressif) des pages de toutes les sections
            if (!_precomputeStarted)
            {
                _precomputeStarted = true;
                EnsureWebView(); // crée WebView principal (affichage)
                _ = PrecomputeAllPageCountsAsync();
            }
        }
        finally
        {
            _isLoading = false;
        }
    }

    // --------------------
    // WEBVIEW
    // --------------------

    /// <summary>
    /// Crée la WebView principale si absente, branche les handlers (Navigating/Navigated).
    /// </summary>
    private void EnsureWebView()
    {
        if (_webView != null)
            return;

        _webView = new WebView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            BackgroundColor = Colors.Transparent
        };

        _webView.Navigated += OnWebViewNavigated;
        _webView.Navigating += OnWebViewNavigating;

        // Hack/placeholder Android : certains handlers / lifecycle peuvent être sensibles.
        if (DeviceInfo.Platform == DevicePlatform.Android)
            _webView.HandlerChanged += (_, __) => { };
    }

    /// <summary>
    /// Affiche une section et se positionne sur une page (0-based) dans la section.
    /// - Construit le HTML de la section en lazy si nécessaire.
    /// - Charge la WebView (OnWebViewNavigated fera la mesure via JS).
    /// </summary>
    private async Task DisplaySectionAsync(int sectionIndex, int pageInSection)
    {
        if (!_initialized)
            await InitializeAsync();

        if (_sections.Count == 0)
            return;

        sectionIndex = Math.Clamp(sectionIndex, 0, _sections.Count - 1);
        pageInSection = Math.Max(0, pageInSection);

        _showingCover = false;
        _sectionIndex = sectionIndex;
        _pageInSection = pageInSection;

        EnsureWebView();
        PageContent.Content = _webView;

        // Build HTML lazy : conversion XHTML -> HTML autonome (assets+css + template externe)
        var sec = _sections[_sectionIndex];
        if (sec.Html == null)
        {
            string built = await BuildSectionHtmlAsync(sec.RawXhtml ?? "", sec.Key ?? "");
            sec.Html = built;
            _sections[_sectionIndex] = sec;
        }

        // Reset du flag pour traiter Navigated une seule fois sur ce chargement
        _webViewLoadedOnce = false;
        _webView!.Source = new HtmlWebViewSource { Html = _sections[_sectionIndex].Html! };
    }

    /// <summary>
    /// Intercepte les navigations :
    /// - laisse passer les URLs externes (http/https/mailto/tel)
    /// - gère les ancres (#id) dans la même page
    /// - résout les liens internes EPUB vers une section cible (évite file://android_asset/... not found)
    /// </summary>
    private async void OnWebViewNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Url))
            return;

        string url = e.Url;

        // Liens externes : on ne les gère pas ici
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Tout le reste : on annule la navigation native et on gère nous-mêmes
        e.Cancel = true;

        // Anchor-only : même document (pas de changement de section)
        if (url.StartsWith("#"))
        {
            string anchorOnly = url.TrimStart('#');
            if (!string.IsNullOrWhiteSpace(anchorOnly) && _webView != null)
            {
                await _webView.EvaluateJavaScriptAsync(
                    $"window.__rm_reader && window.__rm_reader.goToAnchor && window.__rm_reader.goToAnchor({JsString(anchorOnly)});"
                );
            }
            return;
        }

        // Lien potentiellement "file.xhtml#anchor"
        int hash = url.IndexOf('#');
        string anchor = hash >= 0 ? url[(hash + 1)..] : "";
        string beforeHash = hash >= 0 ? url[..hash] : url;

        // On ne garde que la partie fichier après le dernier '/'
        int lastSlash = beforeHash.LastIndexOf('/');
        string filePart = lastSlash >= 0 ? beforeHash[(lastSlash + 1)..] : beforeHash;

        // Normalisation pour correspondre aux clés indexées
        filePart = Uri.UnescapeDataString(filePart);
        filePart = NormalizeEpubPath(filePart);
        string fileOnly = GetFileNameOnly(filePart);

        // Si on trouve la section correspondante, on navigue vers elle
        if (_fileToSectionIndex.TryGetValue(filePart, out int targetSection) ||
            _fileToSectionIndex.TryGetValue(fileOnly, out targetSection))
        {
            // Si anchor présent, on le garde pour l'appliquer après chargement
            if (!string.IsNullOrWhiteSpace(anchor))
                _pendingAnchor = anchor;

            await DisplaySectionAsync(targetSection, 0);
            return;
        }

        // Fallback : si pas de section trouvée mais anchor présent, on tente quand même
        if (!string.IsNullOrWhiteSpace(anchor) && _webView != null)
        {
            await _webView.EvaluateJavaScriptAsync(
                $"window.__rm_reader && window.__rm_reader.goToAnchor && window.__rm_reader.goToAnchor({JsString(anchor)});"
            );
        }
    }

    /// <summary>
    /// Après chargement de la WebView :
    /// - récupère le nombre de pages (colonnes) via JS
    /// - corrige les timings (Android peut renvoyer 1 trop tôt -> retry)
    /// - applique la position de page demandée + éventuellement l'ancre
    /// - met à jour l'indicateur global de page
    /// </summary>
    private async void OnWebViewNavigated(object? sender, WebNavigatedEventArgs e)
    {
        if (_webView == null || e.Result != WebNavigationResult.Success)
            return;

        // Empêche double-exécution si Navigated déclenché plusieurs fois
        if (_webViewLoadedOnce)
            return;
        _webViewLoadedOnce = true;

        // Petit délai : laisse le DOM/layout s'installer avant de mesurer
        await Task.Delay(120);

        try
        {
            // Mesure du nombre de pages colonnes calculées côté JS
            string pagesStr = await _webView.EvaluateJavaScriptAsync(
                "window.__rm_reader && window.__rm_reader.pageCount ? window.__rm_reader.pageCount() : '1'"
            );

            _pagesInSection = ParseJsInt(pagesStr, fallback: 1);

            // Retry : Android peut retourner 1 si on mesure trop tôt
            if (_pagesInSection == 1)
            {
                await Task.Delay(250);
                pagesStr = await _webView.EvaluateJavaScriptAsync(
                    "window.__rm_reader && window.__rm_reader.pageCount ? window.__rm_reader.pageCount() : '1'"
                );
                int retry = ParseJsInt(pagesStr, fallback: 1);
                if (retry > 1) _pagesInSection = retry;
            }

            // Persist de la mesure dans la section (sert au total global)
            var sec = _sections[_sectionIndex];
            sec.PageCount = Math.Max(1, _pagesInSection);
            _sections[_sectionIndex] = sec;

            // Clamp la page demandée si elle dépasse (après mesure)
            if (_pageInSection >= _pagesInSection)
                _pageInSection = _pagesInSection - 1;

            // Déplacement vers la page demandée
            await _webView.EvaluateJavaScriptAsync(
                $"window.__rm_reader && window.__rm_reader.goTo && window.__rm_reader.goTo({_pageInSection});"
            );

            // Anchor différée (après nav vers nouvelle section)
            if (!string.IsNullOrWhiteSpace(_pendingAnchor))
            {
                string a = _pendingAnchor;
                _pendingAnchor = null;

                await Task.Delay(60);
                await _webView.EvaluateJavaScriptAsync(
                    $"window.__rm_reader && window.__rm_reader.goToAnchor && window.__rm_reader.goToAnchor({JsString(a)});"
                );
            }

            UpdatePageIndicatorExact();
        }
        catch
        {
            // Fallback robuste : si JS échoue, on force 1 page
            _pagesInSection = 1;
            var sec = _sections[_sectionIndex];
            sec.PageCount = 1;
            _sections[_sectionIndex] = sec;

            UpdatePageIndicatorExact();
        }
    }

    // --------------------
    // PRÉCALCUL DES PAGES POUR TOUTES LES SECTIONS
    // --------------------

    /// <summary>
    /// Mesure progressivement le PageCount de chaque section via une WebView dédiée (MeasureWebView).
    /// Objectif : améliorer la précision du total global sans attendre que l'utilisateur visite chaque section.
    /// Note : sérialisé via <see cref="_measureLock"/> pour éviter conflits WebView/JS.
    /// </summary>
    private async Task PrecomputeAllPageCountsAsync()
    {
        // attendre l'init
        while (!_initialized)
            await Task.Delay(50);

        // MeasureWebView : WebView "cachée" déclarée dans le XAML (non visible)
        if (MeasureWebView == null || _sections.Count == 0)
            return;

        await _measureLock.WaitAsync();
        try
        {
            for (int i = 0; i < _sections.Count; i++)
            {
                // si déjà mesuré correctement, skip
                if (_sections[i].PageCount > 1)
                    continue;

                var sec = _sections[i];

                // Build HTML lazy si nécessaire (même HTML final que pour l'affichage)
                if (sec.Html == null)
                {
                    string built = await BuildSectionHtmlAsync(sec.RawXhtml ?? "", sec.Key ?? "");
                    sec.Html = built;
                    _sections[i] = sec;
                }

                // On attend explicitement la navigation terminée de MeasureWebView
                var tcs = new TaskCompletionSource<bool>();

                void Handler(object? s, WebNavigatedEventArgs e)
                {
                    MeasureWebView.Navigated -= Handler;
                    tcs.TrySetResult(e.Result == WebNavigationResult.Success);
                }

                MeasureWebView.Navigated += Handler;

                // Le changement de Source doit se faire sur le thread UI
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    MeasureWebView.Source = new HtmlWebViewSource { Html = _sections[i].Html! };
                });

                bool ok = await tcs.Task;
                if (!ok)
                    continue;

                // Délai pour laisser layout/colonnes se calculer
                await Task.Delay(160);

                // Mesure JS
                string pagesStr = await MeasureWebView.EvaluateJavaScriptAsync(
                    "window.__rm_reader && window.__rm_reader.pageCount ? window.__rm_reader.pageCount() : '1'"
                );

                int pc = ParseJsInt(pagesStr, fallback: 1);

                // Retry si 1 trop tôt (Android)
                if (pc == 1)
                {
                    await Task.Delay(260);
                    pagesStr = await MeasureWebView.EvaluateJavaScriptAsync(
                        "window.__rm_reader && window.__rm_reader.pageCount ? window.__rm_reader.pageCount() : '1'"
                    );
                    int retry = ParseJsInt(pagesStr, fallback: 1);
                    if (retry > 1) pc = retry;
                }

                // Persist mesure
                sec = _sections[i];
                sec.PageCount = Math.Max(1, pc);
                _sections[i] = sec;

                // update UI (total global peut changer)
                MainThread.BeginInvokeOnMainThread(UpdatePageIndicatorExact);
            }
        }
        catch
        {
            // ignore : on ne doit pas casser la lecture
        }
        finally
        {
            _measureLock.Release();
        }
    }

    // --------------------
    // COVER
    // --------------------

    /// <summary>
    /// Affiche la couverture (Image) et configure un Tap pour démarrer la lecture (section 0 page 0).
    /// Remet aussi l'état interne au "mode cover".
    /// </summary>
    private void DisplayCover()
    {
        _showingCover = true;
        _sectionIndex = 0;
        _pageInSection = 0;
        _pagesInSection = 1;

        var cover = new Image
        {
            // Si la couverture est fournie, on l'utilise, sinon une image de fallback
            Source = _book.CoverImage?.Length > 0
                ? ImageSource.FromStream(() => new MemoryStream(_book.CoverImage))
                : "bookcover.jpg",
            Aspect = Aspect.AspectFit
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, __) =>
        {
            if (!_initialized)
                await InitializeAsync();

            await DisplaySectionAsync(0, 0);
            UpdatePageIndicatorExact();
        };
        cover.GestureRecognizers.Add(tap);

        PageContent.Content = new Grid
        {
            Padding = new Thickness(14),
            Children = { cover }
        };

        UpdatePageIndicatorExact();
    }

    // --------------------
    // PAGE TURN (swipe)
    // --------------------

    /// <summary>
    /// Animation simple "page turn" (rotation Y). Ne change pas le contenu,
    /// uniquement l'effet visuel pendant la navigation.
    /// </summary>
    private async Task AnimatePageTurn(bool forward)
    {
        PageContent.AnchorX = forward ? 1 : 0;
        await PageContent.RotateYTo(forward ? -90 : 90, 160u, Easing.CubicIn);
        PageContent.RotationY = forward ? 90 : -90;
        await PageContent.RotateYTo(0, 160u, Easing.CubicOut);
    }

    /// <summary>
    /// Swipe gauche : avance d'une page, sinon passe à la section suivante,
    /// ou depuis la cover démarre au début.
    /// </summary>
    private async void OnSwipeLeft(object sender, SwipedEventArgs e)
    {
        if (!_initialized)
            await InitializeAsync();

        if (_showingCover)
        {
            await AnimatePageTurn(true);
            await DisplaySectionAsync(0, 0);
            UpdatePageIndicatorExact();
            return;
        }

        // Page suivante dans la section
        if (_pageInSection < _pagesInSection - 1)
        {
            _pageInSection++;
            await AnimatePageTurn(true);
            if (_webView != null)
                await _webView.EvaluateJavaScriptAsync(
                    $"window.__rm_reader && window.__rm_reader.goTo && window.__rm_reader.goTo({_pageInSection});"
                );
        }
        // Sinon section suivante
        else if (_sectionIndex < _sections.Count - 1)
        {
            await AnimatePageTurn(true);
            await DisplaySectionAsync(_sectionIndex + 1, 0);
        }

        UpdatePageIndicatorExact();
    }

    /// <summary>
    /// Swipe droite : recule d'une page, sinon va à la fin de la section précédente,
    /// ou revient à la cover si on est au tout début.
    /// </summary>
    private async void OnSwipeRight(object sender, SwipedEventArgs e)
    {
        if (!_initialized)
            await InitializeAsync();

        if (_showingCover)
            return;

        // Page précédente dans la section
        if (_pageInSection > 0)
        {
            _pageInSection--;
            await AnimatePageTurn(false);
            if (_webView != null)
                await _webView.EvaluateJavaScriptAsync(
                    $"window.__rm_reader && window.__rm_reader.goTo && window.__rm_reader.goTo({_pageInSection});"
                );
        }
        // Sinon section précédente (dernière page)
        else if (_sectionIndex > 0)
        {
            int prev = _sectionIndex - 1;
            int lastPage = Math.Max(0, Math.Max(1, _sections[prev].PageCount) - 1);

            await AnimatePageTurn(false);
            await DisplaySectionAsync(prev, lastPage);
        }
        // Sinon retour cover
        else
        {
            await AnimatePageTurn(false);
            DisplayCover();
            return;
        }

        UpdatePageIndicatorExact();
    }

    /// <summary>
    /// Bouton retour : sauvegarde la progression (page globale) dans le modèle Book,
    /// puis quitte la page.
    /// </summary>
    private async void OnBackClicked(object sender, EventArgs e)
    {
        // Sauvegarde : page globale (1-based, cover incluse)
        _book.LastPageRead = Math.Max(0, _currentPageExact - 1);
        await Navigation.PopAsync();
    }

    // --------------------
    // PAGE INDICATOR
    // --------------------

    /// <summary>
    /// Recalcule le nombre total de pages globales à partir des PageCount mesurés.
    /// Toujours >= 1 (cover).
    /// </summary>
    private void RecomputeTotalPagesExact()
    {
        int total = 1; // cover
        for (int i = 0; i < _sections.Count; i++)
            total += Math.Max(1, _sections[i].PageCount);

        _totalPagesExact = Math.Max(1, total);
    }

    /// <summary>
    /// Calcule la page globale actuelle (1-based) :
    /// cover = 1, puis somme des pages des sections précédentes + page courante dans section.
    /// </summary>
    private int ComputeCurrentPageExact()
    {
        if (_showingCover)
            return 1;

        int page = 1; // cover
        for (int i = 0; i < _sectionIndex; i++)
            page += Math.Max(1, _sections[i].PageCount);

        page += (_pageInSection + 1);
        return Math.Max(1, page);
    }

    /// <summary>
    /// Met à jour l'indicateur UI "current/total" en tenant compte des mesures actuelles.
    /// Peut être appelé fréquemment (navigation, precompute, cover, etc.).
    /// </summary>
    private void UpdatePageIndicatorExact()
    {
        RecomputeTotalPagesExact();
        _currentPageExact = ComputeCurrentPageExact();

        if (PageIndicator != null)
            PageIndicator.Text = $"{_currentPageExact}/{_totalPagesExact}";
    }

    /// <summary>
    /// Parse robuste d'un entier renvoyé par JS (souvent string avec guillemets / bruit).
    /// Ex: "\"12\"" -> 12, "12" -> 12, autre -> fallback.
    /// </summary>
    private static int ParseJsInt(string? jsValue, int fallback)
    {
        var digits = Regex.Match(jsValue ?? "", "\\d+").Value;
        return int.TryParse(digits, out int v) ? v : fallback;
    }

    // --------------------
    // HTML BUILD (template externe)
    // --------------------

    /// <summary>
    /// Construit le HTML final d'une section :
    /// - inline des stylesheets linkées
    /// - conversion des assets (img/fonts/...) vers data URI
    /// - extraction CSS et body
    /// - injection dans ReaderTemplate.html (Resources/Raw) via placeholders
    ///
    /// Placeholders attendus dans le fichier ReaderTemplate.html :
    /// - {{extraCss}}
    /// - {{innerBodyHtml}}
    /// </summary>
    private async Task<string> BuildSectionHtmlAsync(string rawXhtml, string baseKey)
    {
        if (_epub == null)
            return BuildFallbackHtml("Livre non chargé.");

        string processed = InlineStylesheets(rawXhtml, baseKey);
        processed = RewriteHtmlAssetUrlsToDataUris(processed, baseKey);
        processed = RewriteStyleBlocksUrlsToDataUris(processed, baseKey);

        string extractedCss = ExtractAllStyleBlocks(processed);
        string bodyInner = ExtractBodyInnerHtml(processed);

        // Charge une seule fois le template depuis l'app package, puis réutilise le cache.
        _readerTemplateHtml ??= await LoadHtmlTemplateAsync("ReaderTemplate.html");

        return _readerTemplateHtml
            .Replace("{{extraCss}}", extractedCss ?? string.Empty)
            .Replace("{{innerBodyHtml}}", bodyInner ?? string.Empty);
    }

    /// <summary>
    /// Lit un fichier contenu dans l'app package (MAUIAsset).
    /// Pour que ça marche :
    /// - placer ReaderTemplate.html dans Resources/Raw/
    /// - Build Action: MauiAsset (automatique si dans Resources/Raw)
    /// </summary>
    private static async Task<string> LoadHtmlTemplateAsync(string fileName)
    {
        await using var stream = await FileSystem.OpenAppPackageFileAsync(fileName);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    /// <summary>
    /// Extrait le contenu interne du &lt;body&gt; d'un document HTML/XHTML.
    /// Si non trouvé, retourne le HTML tel quel.
    /// </summary>
    private static string ExtractBodyInnerHtml(string html)
    {
        var m = Regex.Match(html, "<body[^>]*>(?<b>[\\s\\S]*?)</body>", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups["b"].Value : html;
    }

    /// <summary>
    /// Concatène le contenu de tous les blocs &lt;style&gt; présents dans le HTML.
    /// Permet ensuite d'injecter ce CSS dans le template final.
    /// </summary>
    private static string ExtractAllStyleBlocks(string html)
    {
        var matches = Regex.Matches(html, "<style(?:(?!>).)*>(?<css>[\\s\\S]*?)</style>", RegexOptions.IgnoreCase);
        if (matches.Count == 0) return "";

        var sb = new StringBuilder();
        foreach (Match m in matches)
            sb.AppendLine(m.Groups["css"].Value);

        return sb.ToString();
    }

    // --------------------
    // EPUB CSS / ASSETS
    // --------------------

    /// <summary>
    /// Remplace les &lt;link rel="stylesheet" href="..."/&gt; par des &lt;style&gt; inline
    /// en récupérant le fichier CSS depuis l'EPUB (avec résolution de chemin).
    /// </summary>
    private string InlineStylesheets(string html, string baseKey)
    {
        if (_epub?.Content?.Css == null)
            return html;

        var rx = new Regex("<link(?:(?!>).)*rel=[\\\"']?stylesheet[\\\"']?(?:(?!>).)*>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return rx.Replace(html, m =>
        {
            var hrefMatch = Regex.Match(m.Value, "href=[\\\"'](?<h>[^\\\"']+)[\\\"']", RegexOptions.IgnoreCase);
            if (!hrefMatch.Success)
                return m.Value;

            string href = hrefMatch.Groups["h"].Value;
            if (IsExternalUrl(href))
                return m.Value;

            // Résolution du chemin CSS relatif à la section courante
            string resolvedPath = ResolvePath(baseKey, href);

            // Lookup flexible : chemin complet ou clé, puis fallback au nom de fichier seul
            if (!_epub.Content.Css.TryGetLocalFileByFilePath(resolvedPath, out var cssFile) &&
                !_epub.Content.Css.TryGetLocalFileByKey(resolvedPath, out cssFile))
            {
                string fileOnly = GetFileNameOnly(resolvedPath);
                if (!_epub.Content.Css.TryGetLocalFileByFilePath(fileOnly, out cssFile) &&
                    !_epub.Content.Css.TryGetLocalFileByKey(fileOnly, out cssFile))
                    return m.Value;
            }

            if (cssFile?.Content == null)
                return m.Value;

            // Rewrite url(...) du CSS vers data URI pour éviter les accès file://
            string css = RewriteCssUrlsToDataUris(cssFile.Content, resolvedPath);
            return $"<style>\n{css}\n</style>";
        });
    }

    /// <summary>
    /// Réécrit les url(...) dans les blocs &lt;style&gt; inline du HTML vers des data URI.
    /// </summary>
    private string RewriteStyleBlocksUrlsToDataUris(string html, string baseKey)
    {
        var rx = new Regex("<style(?:(?!>).)*>(?<css>[\\s\\S]*?)</style>", RegexOptions.IgnoreCase);
        return rx.Replace(html, m =>
        {
            string css = m.Groups["css"].Value;
            css = RewriteCssUrlsToDataUris(css, baseKey);
            return m.Value.Replace(m.Groups["css"].Value, css);
        });
    }

    /// <summary>
    /// Réécrit les attributs src/href (images, liens vers assets) vers des data URI si possible.
    /// Gère aussi xlink:href (SVG).
    /// </summary>
    private string RewriteHtmlAssetUrlsToDataUris(string html, string baseKey)
    {
        // src="..." / href="..."
        Regex? rx = new("(?<attr>(?:src|href))=\\\"(?<url>[^\\\"]+)\\\"", RegexOptions.IgnoreCase);

        html = rx.Replace(html, m =>
        {
            string attr = m.Groups["attr"].Value;
            string url = m.Groups["url"].Value;

            // On ignore externes et ancres
            if (IsExternalUrl(url) || url.StartsWith("#"))
                return m.Value;

            string resolved = ResolvePath(baseKey, url);

            // Cherche l'asset dans l'EPUB (chemin complet, puis fallback fileOnly)
            if (TryGetBinaryResource(resolved, out var bytes, out var mime) ||
                TryGetBinaryResource(GetFileNameOnly(resolved), out bytes, out mime))
            {
                return $"{attr}=\"{ToDataUri(bytes, mime)}\"";
            }

            // Si on ne trouve pas, on laisse tel quel
            return m.Value;
        });

        // xlink:href="..." (SVG)
        var rx2 = new Regex("xlink:href=\\\"(?<url>[^\\\"]+)\\\"", RegexOptions.IgnoreCase);
        html = rx2.Replace(html, m =>
        {
            string url = m.Groups["url"].Value;
            if (IsExternalUrl(url) || url.StartsWith("#"))
                return m.Value;

            string resolved = ResolvePath(baseKey, url);

            if (TryGetBinaryResource(resolved, out var bytes, out var mime) ||
                TryGetBinaryResource(GetFileNameOnly(resolved), out bytes, out mime))
            {
                return $"xlink:href=\"{ToDataUri(bytes, mime)}\"";
            }

            return m.Value;
        });

        return html;
    }

    /// <summary>
    /// Réécrit les url(...) dans du CSS en data URI lorsque l'asset est trouvé dans l'EPUB.
    /// Ignore les url externes et les data: déjà présentes.
    /// </summary>
    private string RewriteCssUrlsToDataUris(string css, string baseKey)
    {
        var rx = new Regex("url\\((?<u>[^)]+)\\)", RegexOptions.IgnoreCase);
        return rx.Replace(css, m =>
        {
            string raw = m.Groups["u"].Value.Trim().Trim('"', '\'', ' ');

            if (string.IsNullOrWhiteSpace(raw) || IsExternalUrl(raw) || raw.StartsWith("data:"))
                return m.Value;

            string resolved = ResolvePath(baseKey, raw);

            if (TryGetBinaryResource(resolved, out var bytes, out var mime) ||
                TryGetBinaryResource(GetFileNameOnly(resolved), out bytes, out mime))
            {
                return $"url('{ToDataUri(bytes, mime)}')";
            }

            return m.Value;
        });
    }

    /// <summary>
    /// Tente de récupérer une ressource binaire dans l'EPUB (images, fonts, autres fichiers).
    /// - Essaie Images, Fonts, puis AllFiles.
    /// - Devine le MIME via l'extension.
    /// </summary>
    private bool TryGetBinaryResource(string pathOrKey, out byte[] bytes, out string mime)
    {
        bytes = Array.Empty<byte>();
        mime = "application/octet-stream";

        if (_epub?.Content == null)
            return false;

        if (_epub.Content.Images != null &&
            (_epub.Content.Images.TryGetLocalFileByFilePath(pathOrKey, out var img) ||
             _epub.Content.Images.TryGetLocalFileByKey(pathOrKey, out img)) &&
            img?.Content != null)
        {
            bytes = img.Content;
            mime = GuessMime(pathOrKey);
            return true;
        }

        if (_epub.Content.Fonts != null &&
            (_epub.Content.Fonts.TryGetLocalFileByFilePath(pathOrKey, out var font) ||
             _epub.Content.Fonts.TryGetLocalFileByKey(pathOrKey, out font)) &&
            font?.Content != null)
        {
            bytes = font.Content;
            mime = GuessMime(pathOrKey);
            return true;
        }

        if (_epub.Content.AllFiles != null &&
            (_epub.Content.AllFiles.TryGetLocalFileByFilePath(pathOrKey, out var any) ||
             _epub.Content.AllFiles.TryGetLocalFileByKey(pathOrKey, out any)))
        {
            // Cas où la lib expose un EpubLocalByteContentFile
            if (any is EpubLocalByteContentFile bf && bf.Content != null)
            {
                bytes = bf.Content;
                mime = GuessMime(pathOrKey);
                return true;
            }

            // Fallback réflexion : certains types ont une propriété Content byte[]
            var prop = any?.GetType().GetProperty("Content");
            if (prop?.GetValue(any) is byte[] b && b.Length > 0)
            {
                bytes = b;
                mime = GuessMime(pathOrKey);
                return true;
            }
        }

        return false;
    }

    // --------------------
    // HELPERS
    // --------------------

    /// <summary>
    /// Détecte si une URL doit être considérée externe (donc non réécrite / non interceptée).
    /// </summary>
    private static bool IsExternalUrl(string url)
        => url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
           || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
           || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
           || url.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)
           || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Résout un chemin relatif (href/src/url()) à partir de la "clé" de la section courante.
    /// Gère '.' et '..' et normalise en slash '/'.
    /// </summary>
    private static string ResolvePath(string baseKey, string relative)
    {
        string baseDir = NormalizeEpubPath(baseKey ?? "");
        int lastSlash = baseDir.LastIndexOf('/');
        baseDir = lastSlash >= 0 ? baseDir.Substring(0, lastSlash + 1) : "";

        var parts = new List<string>();
        foreach (var seg in (baseDir + relative).Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (seg == ".") continue;
            if (seg == "..")
            {
                if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                continue;
            }
            parts.Add(seg);
        }
        return string.Join('/', parts);
    }

    /// <summary>
    /// Normalise les chemins EPUB : backslashes -> '/', retire "./" au début.
    /// </summary>
    private static string NormalizeEpubPath(string p)
    {
        if (string.IsNullOrWhiteSpace(p)) return "";
        p = p.Replace('\\', '/');
        if (p.StartsWith("./", StringComparison.Ordinal)) p = p.Substring(2);
        return p;
    }

    /// <summary>
    /// Extrait le nom de fichier (sans dossier) d'un chemin.
    /// </summary>
    private static string GetFileNameOnly(string p)
    {
        p = NormalizeEpubPath(p);
        int slash = p.LastIndexOf('/');
        return slash >= 0 ? p[(slash + 1)..] : p;
    }

    /// <summary>
    /// Devine le MIME type à partir de l'extension.
    /// Sert à construire correctement le data URI.
    /// </summary>
    private static string GuessMime(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".mp3" => "audio/mpeg",
            ".mp4" => "video/mp4",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// Convertit un binaire en data URI base64.
    /// </summary>
    private static string ToDataUri(byte[] bytes, string mime)
    {
        string b64 = Convert.ToBase64String(bytes);
        return $"data:{mime};base64,{b64}";
    }

    /// <summary>
    /// HTML minimal de secours (affiche un message).
    /// Utilisé si EPUB absent / section vide.
    /// </summary>
    private static string BuildFallbackHtml(string message)
    {
        var safe = System.Net.WebUtility.HtmlEncode(message);
        return $@"<!doctype html>
<html><head><meta name='viewport' content='width=device-width, initial-scale=1.0' />
<style>body{{font-family: sans-serif; padding:24px;}}</style></head>
<body><h3>{safe}</h3></body></html>";
    }

    /// <summary>
    /// Récupère une clé de fichier depuis un objet de VersOne.Epub (via réflexion),
    /// car la structure exacte dépend de la version/du type.
    /// </summary>
    private static string GetContentFileKey(object file)
    {
        var t = file.GetType();
        var pFileName = t.GetProperty("FileName");
        var pKey = t.GetProperty("Key");
        return (pFileName?.GetValue(file) as string)
               ?? (pKey?.GetValue(file) as string)
               ?? "";
    }

    /// <summary>
    /// Encode une string en littéral JS (simple quotes) de manière safe (échappe \ et ').
    /// </summary>
    private static string JsString(string s)
        => "'" + (s ?? "").Replace("\\", "\\\\").Replace("'", "\\'") + "'";

    /// <summary>
    /// Données d'une section EPUB :
    /// - Key : identifiant/chemin original (sert à résoudre ressources)
    /// - RawXhtml : contenu brut
    /// - Html : contenu final template + assets inline (lazy)
    /// - PageCount : nombre de pages colonnes mesuré (>=1)
    /// </summary>
    private struct Section
    {
        public string Key;
        public string? RawXhtml;
        public string? Html;
        public int PageCount;
    }
}