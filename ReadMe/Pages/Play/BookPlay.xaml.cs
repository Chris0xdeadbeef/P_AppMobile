using System.Text.RegularExpressions;
using VersOne.Epub;

namespace ReadMe.Pages.Play;

public partial class BookPlay : ContentPage
{
    private readonly Models.Book _book;

    private List<string> _pages = new();
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

        // Supprime les balises HTML
        string text = Regex.Replace(html, "<.*?>", string.Empty);

        // Remplace les entités HTML (&nbsp; etc.)
        text = System.Net.WebUtility.HtmlDecode(text);

        // Nettoyage final
        return text.Trim();
    }


    private void LoadBook()
    {
        using var ms = new MemoryStream(_book.EpubContent);
        var epub = EpubReader.ReadBook(ms);

        // Page 0 = couverture
        _pages.Add("[COVER]");

        // Récupérer le texte propre
        string fullText = string.Join("\n\n",
            epub.ReadingOrder
                .Select(c => StripHtml(c.Content)) // Nettoyage HTML → texte
                .Where(t => !string.IsNullOrWhiteSpace(t))
        );

        // Découper en pages
        const int pageSize = 1500;
        for (int i = 0; i < fullText.Length; i += pageSize)
        {
            int length = Math.Min(pageSize, fullText.Length - i);
            _pages.Add(fullText.Substring(i, length));
        }
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
            PageContent.Content = new ScrollView
            {
                Content = new Label
                {
                    Text = _pages[index],
                    TextColor = Colors.White,
                    FontSize = 20
                }
            };
        }
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
}
