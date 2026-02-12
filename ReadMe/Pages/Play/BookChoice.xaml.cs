using ReadMe.Services;
using ReadMe.Models;

namespace ReadMe.Pages.Play;

public partial class BookChoice : ContentPage
{
    private readonly BookService _bookService;

    public BookChoice(BookService bookService)
    {
        InitializeComponent();
        _bookService = bookService;

        BindingContext = new
        {
            Books = _bookService.GetAllBooks()
        };
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    private async void OnBookTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is Models.Book selectedBook)
        {
            await Navigation.PushAsync(new BookPlay(selectedBook));
        }
    }
}
