using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using VersOne.Epub;

namespace ReadMe.Pages.Play;

public partial class BookPlay : ContentPage
{
    private readonly Models.Book _book;

    private List<string> _pages = [];
    private int _currentPage = 0;

    public BookPlay(Models.Book book)
    {
        InitializeComponent();
        _book = book;

        LoadBook();
        DisplayPage(0);
    }

    private string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";

        string text = Regex.Replace(html, "<.*?>", string.Empty);
        text = System.Net.WebUtility.HtmlDecode(text);
        return text.Trim();
    }

    private List<string> PaginateText(string fullText)
    {
        const int charsPerPage = 1200; // Ajuste si besoin

        List<string> pages = new();
        int index = 0;

        while (index < fullText.Length)
        {
            int length = Math.Min(charsPerPage, fullText.Length - index);
            pages.Add(fullText.Substring(index, length));
            index += length;
        }

        return pages;
    }

    private void LoadBook()
    {
        using var ms = new MemoryStream(_book.EpubContent);
        var epub = EpubReader.ReadBook(ms);

        var fullText = new StringBuilder();

        foreach (var htmlFile in epub.ReadingOrder)
        {
            if (htmlFile?.Content == null)
                continue;

            string html = htmlFile.Content;
            string text = StripHtml(html);

            if (!string.IsNullOrWhiteSpace(text))
                fullText.AppendLine(text);
        }

        var textPages = PaginateText(fullText.ToString());

        _pages = new List<string>();
        _pages.Add("[COVER]");      // page 0 = couverture
        _pages.AddRange(textPages); // pages 1..N = texte
    }

    private void DisplayPage(int index)
    {
        if (index < 0 || index >= _pages.Count)
            return;

        _currentPage = index;

        if (_pages[index] == "[COVER]")
        {
            PageContent.Content = new Image
            {
                Source = _book.CoverImage?.Length > 0
                    ? ImageSource.FromStream(() => new MemoryStream(_book.CoverImage))
                    : "bookcover.jpg",
                Aspect = Aspect.AspectFit
            };
        }
        else
        {
            var webView = new WebView();

            string htmlTemplate = @"
<html>
<head>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <style>
        body {
            background-color: black;
            color: white;
            font-size: 18px;
            padding: 20px;
            line-height: 1.6;
        }
        img {
            max-width: 100%;
            height: auto;
        }
    </style>
</head>
<body>
    {CONTENT}
</body>
</html>";

            string finalHtml = htmlTemplate.Replace("{CONTENT}", _pages[index]);

            webView.Source = new HtmlWebViewSource
            {
                Html = finalHtml
            };

            PageContent.Content = webView;
        }

        UpdateProgress();
    }

    private void UpdateProgress()
    {
        if (_pages.Count == 0)
            return;

        double ratio = (double)(_currentPage + 1) / _pages.Count;

        // Même correction visuelle que CardPlay
        double correctedRatio = ratio * ((250.0 - 110.0) / 250.0);

        ProgressViewport.ScaleX = correctedRatio;
    }

    private async Task AnimatePageTurn(bool forward)
    {
        PageContent.AnchorX = forward ? 1 : 0;

        await PageContent.RotateYTo(forward ? -90 : 90, 200);
        DisplayPage(_currentPage);
        PageContent.RotationY = forward ? 90 : -90;
        await PageContent.RotateYTo(0, 200);
    }

    private async void OnSwipeLeft(object sender, SwipedEventArgs e)
    {
        if (_currentPage < _pages.Count - 1)
        {
            _currentPage++;
            await AnimatePageTurn(true);
        }
    }

    private async void OnSwipeRight(object sender, SwipedEventArgs e)
    {
        if (_currentPage > 0)
        {
            _currentPage--;
            await AnimatePageTurn(false);
        }
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    // --- Animation de la bande de fond (comme CardPlay) ---

    private async void AnimateFill()
    {
        double speed = 80;
        double tileWidth = 250;
        double position = 0;

        var stopwatch = new Stopwatch();
        stopwatch.Start();

        while (true)
        {
            double dt = stopwatch.Elapsed.TotalSeconds;
            stopwatch.Restart();

            position += speed * dt;
            position %= tileWidth;

            FillBand.TranslationX = -position;

            await Task.Delay(16);
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        AnimateFill();
    }
}
