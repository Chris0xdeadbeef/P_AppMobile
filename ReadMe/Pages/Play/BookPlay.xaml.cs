using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using VersOne.Epub;

namespace ReadMe.Pages.Play;

public partial class BookPlay : ContentPage
{
    private readonly Models.Book _book;

    private EpubBook? _epub;
    private readonly List<Section> _sections = new();

    private bool _initialized;
    private bool _isLoading;

    private bool _showingCover = true;
    private int _sectionIndex = 0;
    private int _pageInSection = 0;   // 0-based
    private int _pagesInSection = 1;  // calculé via JS

    // VRAIE numérotation (colonnes) sur tout le livre
    private int _totalPagesExact = 1;     // inclut cover
    private int _currentPageExact = 1;   // cover = 1

    private WebView? _webView;
    private bool _webViewLoadedOnce;

    private bool _precomputeStarted;
    private readonly SemaphoreSlim _measureLock = new(1, 1);

    private readonly Dictionary<string, int> _fileToSectionIndex = new(StringComparer.OrdinalIgnoreCase);
    private string? _pendingAnchor;

    public BookPlay(Models.Book book)
    {
        InitializeComponent();
        _book = book;

        DisplayCover(); // affichage immédiat
        UpdatePageIndicatorExact(); // 1/1 au départ
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (!_initialized)
            _ = InitializeAsync();
    }

    // --------------------
    // INIT / LOAD EPUB (background)
    // --------------------
    private async Task InitializeAsync()
    {
        if (_initialized || _isLoading)
            return;

        _isLoading = true;

        try
        {
            await Task.Run(() =>
            {
                using var ms = new MemoryStream(_book.EpubContent);
                _epub = EpubReader.ReadBook(ms);

                _sections.Clear();
                _fileToSectionIndex.Clear();

                int idx = 0;
                foreach (var xhtml in _epub.ReadingOrder)
                {
                    if (string.IsNullOrWhiteSpace(xhtml?.Content))
                        continue;

                    string key = GetContentFileKey(xhtml);
                    string norm = NormalizeEpubPath(key);
                    string fileOnly = GetFileNameOnly(norm);

                    _fileToSectionIndex[norm] = idx;
                    _fileToSectionIndex[fileOnly] = idx;

                    _sections.Add(new Section
                    {
                        Key = key,
                        RawXhtml = xhtml.Content,
                        Html = null,
                        PageCount = 1
                    });

                    idx++;
                }

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

            // total minimal : cover + 1 page par section
            RecomputeTotalPagesExact();
            MainThread.BeginInvokeOnMainThread(UpdatePageIndicatorExact);

            // Lancer le calcul réel (progressif) des pages de toutes les sections
            if (!_precomputeStarted)
            {
                _precomputeStarted = true;
                EnsureWebView(); // crée WebView principal
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

        if (DeviceInfo.Platform == DevicePlatform.Android)
            _webView.HandlerChanged += (_, __) => { };
    }

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

        // build HTML lazy
        var sec = _sections[_sectionIndex];
        if (sec.Html == null)
        {
            string built = await Task.Run(() => BuildSectionHtml(sec.RawXhtml ?? "", sec.Key ?? ""));
            sec.Html = built;
            _sections[_sectionIndex] = sec;
        }

        _webViewLoadedOnce = false;
        _webView!.Source = new HtmlWebViewSource { Html = _sections[_sectionIndex].Html! };
    }

    // Interception liens internes EPUB (évite file://android_asset/... not found)
    private async void OnWebViewNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Url))
            return;

        string url = e.Url;

        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Cancel = true;

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

        int hash = url.IndexOf('#');
        string anchor = hash >= 0 ? url[(hash + 1)..] : "";
        string beforeHash = hash >= 0 ? url[..hash] : url;

        int lastSlash = beforeHash.LastIndexOf('/');
        string filePart = lastSlash >= 0 ? beforeHash[(lastSlash + 1)..] : beforeHash;

        filePart = Uri.UnescapeDataString(filePart);
        filePart = NormalizeEpubPath(filePart);
        string fileOnly = GetFileNameOnly(filePart);

        if (_fileToSectionIndex.TryGetValue(filePart, out int targetSection) ||
            _fileToSectionIndex.TryGetValue(fileOnly, out targetSection))
        {
            if (!string.IsNullOrWhiteSpace(anchor))
                _pendingAnchor = anchor;

            await DisplaySectionAsync(targetSection, 0);
            return;
        }

        if (!string.IsNullOrWhiteSpace(anchor) && _webView != null)
        {
            await _webView.EvaluateJavaScriptAsync(
                $"window.__rm_reader && window.__rm_reader.goToAnchor && window.__rm_reader.goToAnchor({JsString(anchor)});"
            );
        }
    }

    private async void OnWebViewNavigated(object? sender, WebNavigatedEventArgs e)
    {
        if (_webView == null || e.Result != WebNavigationResult.Success)
            return;

        if (_webViewLoadedOnce)
            return;
        _webViewLoadedOnce = true;

        await Task.Delay(120);

        try
        {
            string pagesStr = await _webView.EvaluateJavaScriptAsync(
                "window.__rm_reader && window.__rm_reader.pageCount ? window.__rm_reader.pageCount() : '1'"
            );

            _pagesInSection = ParseJsInt(pagesStr, fallback: 1);

            // retry si Android renvoie 1 trop tôt
            if (_pagesInSection == 1)
            {
                await Task.Delay(250);
                pagesStr = await _webView.EvaluateJavaScriptAsync(
                    "window.__rm_reader && window.__rm_reader.pageCount ? window.__rm_reader.pageCount() : '1'"
                );
                int retry = ParseJsInt(pagesStr, fallback: 1);
                if (retry > 1) _pagesInSection = retry;
            }

            var sec = _sections[_sectionIndex];
            sec.PageCount = Math.Max(1, _pagesInSection);
            _sections[_sectionIndex] = sec;

            if (_pageInSection >= _pagesInSection)
                _pageInSection = _pagesInSection - 1;

            await _webView.EvaluateJavaScriptAsync(
                $"window.__rm_reader && window.__rm_reader.goTo && window.__rm_reader.goTo({_pageInSection});"
            );

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
            _pagesInSection = 1;
            var sec = _sections[_sectionIndex];
            sec.PageCount = 1;
            _sections[_sectionIndex] = sec;

            UpdatePageIndicatorExact();
        }
    }

    // --------------------
    // PRÉCALCUL DES PAGES (vraies colonnes) POUR TOUTES LES SECTIONS
    // --------------------
    private async Task PrecomputeAllPageCountsAsync()
    {
        // attendre l'init
        while (!_initialized)
            await Task.Delay(50);

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

                if (sec.Html == null)
                {
                    string built = await Task.Run(() => BuildSectionHtml(sec.RawXhtml ?? "", sec.Key ?? ""));
                    sec.Html = built;
                    _sections[i] = sec;
                }

                var tcs = new TaskCompletionSource<bool>();

                void Handler(object? s, WebNavigatedEventArgs e)
                {
                    MeasureWebView.Navigated -= Handler;
                    tcs.TrySetResult(e.Result == WebNavigationResult.Success);
                }

                MeasureWebView.Navigated += Handler;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    MeasureWebView.Source = new HtmlWebViewSource { Html = _sections[i].Html! };
                });

                bool ok = await tcs.Task;
                if (!ok)
                    continue;

                await Task.Delay(160);

                string pagesStr = await MeasureWebView.EvaluateJavaScriptAsync(
                    "window.__rm_reader && window.__rm_reader.pageCount ? window.__rm_reader.pageCount() : '1'"
                );

                int pc = ParseJsInt(pagesStr, fallback: 1);

                if (pc == 1)
                {
                    await Task.Delay(260);
                    pagesStr = await MeasureWebView.EvaluateJavaScriptAsync(
                        "window.__rm_reader && window.__rm_reader.pageCount ? window.__rm_reader.pageCount() : '1'"
                    );
                    int retry = ParseJsInt(pagesStr, fallback: 1);
                    if (retry > 1) pc = retry;
                }

                sec = _sections[i];
                sec.PageCount = Math.Max(1, pc);
                _sections[i] = sec;

                // update UI
                MainThread.BeginInvokeOnMainThread(UpdatePageIndicatorExact);
            }
        }
        catch
        {
            // ignore
        }
        finally
        {
            _measureLock.Release();
        }
    }

    // --------------------
    // COVER
    // --------------------
    private void DisplayCover()
    {
        _showingCover = true;
        _sectionIndex = 0;
        _pageInSection = 0;
        _pagesInSection = 1;

        var cover = new Image
        {
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
    private async Task AnimatePageTurn(bool forward)
    {
        PageContent.AnchorX = forward ? 1 : 0;
        await PageContent.RotateYTo(forward ? -90 : 90, 160u, Easing.CubicIn);
        PageContent.RotationY = forward ? 90 : -90;
        await PageContent.RotateYTo(0, 160u, Easing.CubicOut);
    }

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

        if (_pageInSection < _pagesInSection - 1)
        {
            _pageInSection++;
            await AnimatePageTurn(true);
            if (_webView != null)
                await _webView.EvaluateJavaScriptAsync(
                    $"window.__rm_reader && window.__rm_reader.goTo && window.__rm_reader.goTo({_pageInSection});"
                );
        }
        else if (_sectionIndex < _sections.Count - 1)
        {
            await AnimatePageTurn(true);
            await DisplaySectionAsync(_sectionIndex + 1, 0);
        }

        UpdatePageIndicatorExact();
    }

    private async void OnSwipeRight(object sender, SwipedEventArgs e)
    {
        if (!_initialized)
            await InitializeAsync();

        if (_showingCover)
            return;

        if (_pageInSection > 0)
        {
            _pageInSection--;
            await AnimatePageTurn(false);
            if (_webView != null)
                await _webView.EvaluateJavaScriptAsync(
                    $"window.__rm_reader && window.__rm_reader.goTo && window.__rm_reader.goTo({_pageInSection});"
                );
        }
        else if (_sectionIndex > 0)
        {
            int prev = _sectionIndex - 1;
            int lastPage = Math.Max(0, Math.Max(1, _sections[prev].PageCount) - 1);

            await AnimatePageTurn(false);
            await DisplaySectionAsync(prev, lastPage);
        }
        else
        {
            await AnimatePageTurn(false);
            DisplayCover();
            return;
        }

        UpdatePageIndicatorExact();
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        // Sauvegarde : page globale (1-based, cover incluse)
        _book.LastPageRead = Math.Max(0, _currentPageExact - 1);
        await Navigation.PopAsync();
    }

    // --------------------
    // PAGE INDICATOR (vrai)
    // --------------------
    private void RecomputeTotalPagesExact()
    {
        int total = 1; // cover
        for (int i = 0; i < _sections.Count; i++)
            total += Math.Max(1, _sections[i].PageCount);

        _totalPagesExact = Math.Max(1, total);
    }

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

    private void UpdatePageIndicatorExact()
    {
        RecomputeTotalPagesExact();
        _currentPageExact = ComputeCurrentPageExact();

        if (PageIndicator != null)
            PageIndicator.Text = $"{_currentPageExact}/{_totalPagesExact}";
    }

    private static int ParseJsInt(string? jsValue, int fallback)
    {
        var digits = Regex.Match(jsValue ?? "", "\\d+").Value;
        return int.TryParse(digits, out int v) ? v : fallback;
    }

    // --------------------
    // HTML BUILD
    // --------------------
    private string BuildSectionHtml(string rawXhtml, string baseKey)
    {
        if (_epub == null)
            return BuildFallbackHtml("Livre non chargé.");

        string processed = InlineStylesheets(rawXhtml, baseKey);
        processed = RewriteHtmlAssetUrlsToDataUris(processed, baseKey);
        processed = RewriteStyleBlocksUrlsToDataUris(processed, baseKey);

        string extractedCss = ExtractAllStyleBlocks(processed);
        string bodyInner = ExtractBodyInnerHtml(processed);

        return WrapWithReaderTemplate(bodyInner, extractedCss);
    }

    private static string ExtractBodyInnerHtml(string html)
    {
        var m = Regex.Match(html, "<body[^>]*>(?<b>[\\s\\S]*?)</body>", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups["b"].Value : html;
    }

    private static string ExtractAllStyleBlocks(string html)
    {
        var matches = Regex.Matches(html, "<style(?:(?!>).)*>(?<css>[\\s\\S]*?)</style>", RegexOptions.IgnoreCase);
        if (matches.Count == 0) return "";

        var sb = new StringBuilder();
        foreach (Match m in matches)
            sb.AppendLine(m.Groups["css"].Value);

        return sb.ToString();
    }

    // Template : scroll horizontal + pagination colonne (vraies pages)
    private string WrapWithReaderTemplate(string innerBodyHtml, string extraCss)
    {
        return $$"""
<!doctype html>
<html>
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no" />
  <style>
    {{extraCss}}

    html, body {
      margin: 0;
      padding: 0;
      height: 100%;
      width: 100%;
      overflow: hidden;
      background: transparent;
      -webkit-text-size-adjust: 100%;
    }

    #rm_viewport {
      position: fixed;
      inset: 0;
      background: #F6F1E7;
      overflow-x: scroll;
      overflow-y: hidden;
      -webkit-overflow-scrolling: touch;
      scrollbar-width: none;
    }
    #rm_viewport::-webkit-scrollbar { display: none; }

    #rm_content {
      height: 100%;
      box-sizing: border-box;
      padding: 0;
      column-gap: 0px;
      column-fill: auto;
    }

    .rm_page_pad {
      padding: 26px 26px;
      box-sizing: border-box;
      height: 100%;
    }

    img, svg, video { max-width: 100%; height: auto; }
  </style>
</head>
<body>
  <div id="rm_viewport">
    <div id="rm_content">
      <div class="rm_page_pad" id="rm_pad">
        {{innerBodyHtml}}
      </div>
    </div>
  </div>

  <script>
    (function () {
      var viewport = null;
      var content = null;

      function vw() {
        return (viewport && viewport.clientWidth) || document.documentElement.clientWidth || window.innerWidth || 1;
      }

      function layoutColumns() {
        viewport = document.getElementById('rm_viewport');
        content  = document.getElementById('rm_content');
        if (!viewport || !content) return;

        var w = Math.max(1, Math.floor(vw()));
        content.style.columnWidth = w + 'px';
        content.style.columnGap = '0px';
        void content.offsetHeight;
      }

      function pageCount() {
        if (!viewport || !content) {
          viewport = document.getElementById('rm_viewport');
          content  = document.getElementById('rm_content');
          if (!viewport || !content) return 1;
        }
        var w = vw();
        var sw = content.scrollWidth || 0;
        return Math.max(1, Math.ceil(sw / w));
      }

      function goTo(pageIndex) {
        if (!viewport) viewport = document.getElementById('rm_viewport');
        if (!viewport) return;

        var w = vw();
        var target = Math.max(0, pageIndex) * w;
        viewport.scrollLeft = target;
      }

      function goToAnchor(id) {
        try {
          if (!viewport) viewport = document.getElementById('rm_viewport');
          var el = document.getElementById(id);
          if (!viewport || !el) return;

          var x = 0;
          var node = el;
          while (node) {
            if (typeof node.offsetLeft === 'number') x += node.offsetLeft;
            node = node.offsetParent;
          }

          var w = vw();
          var page = Math.max(0, Math.floor(x / w));
          goTo(page);
        } catch (e) {}
      }

      window.__rm_reader = {
        layoutColumns: layoutColumns,
        pageCount: pageCount,
        goTo: goTo,
        goToAnchor: goToAnchor
      };

      function relayoutLater() {
        layoutColumns();
        requestAnimationFrame(function(){ layoutColumns(); });
        setTimeout(layoutColumns, 80);
        setTimeout(layoutColumns, 220);
      }

      relayoutLater();

      if (document.fonts && document.fonts.ready) {
        document.fonts.ready.then(function(){ relayoutLater(); });
      }

      window.addEventListener('resize', function () {
        if (!viewport) viewport = document.getElementById('rm_viewport');
        var w = vw();
        var p = Math.max(0, Math.round((viewport ? viewport.scrollLeft : 0) / w));
        layoutColumns();
        goTo(p);
      });
    })();
  </script>
</body>
</html>
""";
    }

    // --------------------
    // EPUB CSS / ASSETS
    // --------------------
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

            string resolvedPath = ResolvePath(baseKey, href);

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

            string css = RewriteCssUrlsToDataUris(cssFile.Content, resolvedPath);
            return $"<style>\n{css}\n</style>";
        });
    }

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

    private string RewriteHtmlAssetUrlsToDataUris(string html, string baseKey)
    {
        var rx = new Regex("(?<attr>(?:src|href))=\\\"(?<url>[^\\\"]+)\\\"", RegexOptions.IgnoreCase);
        html = rx.Replace(html, m =>
        {
            string attr = m.Groups["attr"].Value;
            string url = m.Groups["url"].Value;

            if (IsExternalUrl(url) || url.StartsWith("#"))
                return m.Value;

            string resolved = ResolvePath(baseKey, url);

            if (TryGetBinaryResource(resolved, out var bytes, out var mime) ||
                TryGetBinaryResource(GetFileNameOnly(resolved), out bytes, out mime))
            {
                return $"{attr}=\"{ToDataUri(bytes, mime)}\"";
            }

            return m.Value;
        });

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
            if (any is EpubLocalByteContentFile bf && bf.Content != null)
            {
                bytes = bf.Content;
                mime = GuessMime(pathOrKey);
                return true;
            }

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
    private static bool IsExternalUrl(string url)
        => url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
           || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
           || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
           || url.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)
           || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase);

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

    private static string NormalizeEpubPath(string p)
    {
        if (string.IsNullOrWhiteSpace(p)) return "";
        p = p.Replace('\\', '/');
        if (p.StartsWith("./", StringComparison.Ordinal)) p = p.Substring(2);
        return p;
    }

    private static string GetFileNameOnly(string p)
    {
        p = NormalizeEpubPath(p);
        int slash = p.LastIndexOf('/');
        return slash >= 0 ? p[(slash + 1)..] : p;
    }

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

    private static string ToDataUri(byte[] bytes, string mime)
    {
        string b64 = Convert.ToBase64String(bytes);
        return $"data:{mime};base64,{b64}";
    }

    private static string BuildFallbackHtml(string message)
    {
        var safe = System.Net.WebUtility.HtmlEncode(message);
        return $@"<!doctype html>
<html><head><meta name='viewport' content='width=device-width, initial-scale=1.0' />
<style>body{{font-family: sans-serif; padding:24px;}}</style></head>
<body><h3>{safe}</h3></body></html>";
    }

    private static string GetContentFileKey(object file)
    {
        var t = file.GetType();
        var pFileName = t.GetProperty("FileName");
        var pKey = t.GetProperty("Key");
        return (pFileName?.GetValue(file) as string)
               ?? (pKey?.GetValue(file) as string)
               ?? "";
    }

    private static string JsString(string s)
        => "'" + (s ?? "").Replace("\\", "\\\\").Replace("'", "\\'") + "'";

    private struct Section
    {
        public string Key;
        public string? RawXhtml;
        public string? Html;
        public int PageCount;
    }
}
