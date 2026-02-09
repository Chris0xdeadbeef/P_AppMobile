using ReadMe.Models;
using ReadMe.Services;

namespace ReadMe.Pages.Book;

public partial class ShowBook : ContentPage
{
    private readonly BookService _bookService;

    public ShowBook(BookService bookService)
    {
        InitializeComponent();

        _bookService = bookService;

        // Définit la source de données pour la CollectionView
        BindingContext = new
        {
            Books = _bookService.GetAllBooks()
        };
    }

    /// <summary>
    /// Retourne à la page précédente si possible.
    /// </summary>
    private async void OnBackClicked(object sender, EventArgs e)
    {
        if (Navigation.NavigationStack.Count > 1)
            await Navigation.PopAsync();
    }

    /// <summary>
    /// Permet de fermer le clavier si on tape en dehors.
    /// </summary>
    private void OnBackgroundTapped(object sender, EventArgs e)
    {
        Focus(); // simple, efficace
    }
}
