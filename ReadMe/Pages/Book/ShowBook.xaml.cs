using System.Windows.Input;
using ReadMe.Pages.Tag;
using ReadMe.Services;

namespace ReadMe.Pages.Book;

public partial class ShowBook : ContentPage
{
    private readonly BookService _bookService;
    private readonly TagService _tagService;
    private readonly AddBook _addBook;

    public ShowBook(BookService bookService, TagService tagService, AddBook addBook)
    {
        InitializeComponent();

        _bookService = bookService;
        _tagService = tagService;
        _addBook = addBook;

        // BindingContext = ViewModel simple (anonyme) avec une Command
        BindingContext = new ShowBookVm(
            books: _bookService.GetAllBooks(),
            openTagPage: async book =>
            {
                if (book == null) return;
                await Navigation.PushAsync(new AddTag(book, _tagService));
            }
        );
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        await Navigation.PopToRootAsync();
    }

    private async void OnClickedAddBook(object sender, EventArgs e)
    {
        await Navigation.PushAsync(_addBook);
    }

    /// <summary>
    /// Mini-VM pour éviter les hacks de SelectedItem et capter un vrai Tap sur toute la carte.
    /// </summary>
    private sealed class ShowBookVm
    {
        public object Books { get; }
        public ICommand OpenTagPageCommand { get; }

        public ShowBookVm(object books, Func<Models.Book?, Task> openTagPage)
        {
            Books = books;

            OpenTagPageCommand = new Command<Models.Book>(async (b) =>
            {
                try { await openTagPage(b); }
                catch { /* on évite de casser l'UI */ }
            });
        }
    }
}