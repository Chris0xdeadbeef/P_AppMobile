using ReadMe.Pages.Tag;
using ReadMe.Services;

namespace ReadMe.Pages.Book;

public partial class ShowBook : ContentPage
{
    private readonly BookService _bookService;
    private readonly AddBook _addBook;
    private readonly AddTag _addTag;

    public ShowBook(BookService bookService, AddBook addBook, AddTag addTag)
    {
        InitializeComponent();

        _bookService = bookService;
        _addBook = addBook;
        _addTag = addTag;

        // Définit la source de données pour la CollectionView
        BindingContext = new
        {
            Books = _bookService.GetAllBooks()
        };
    }

    /// <summary>
    /// Retourne à la page menu
    /// </summary>
    private async void OnBackClicked(object sender, EventArgs e)
    {
        await Navigation.PopToRootAsync();
    }

    /// <summary>
    /// Ouvre la page permettant d’ajouter un nouveau livre.
    /// </summary>
    private async void OnClickedAddBook(object sender, EventArgs e)
    {
        await Navigation.PushAsync(_addBook);
    }

    /// <summary>
    /// Ouvre la page permettant d’ajouter un tag à un livre.
    /// </summary>
    private async void OnClickedTag(object sender, EventArgs e)
    {
        await Navigation.PushAsync(_addTag);
    }
}
