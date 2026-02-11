using ReadMe.Pages.Book;
using ReadMe.Pages.Play;

namespace ReadMe.Pages;

public partial class Menu : ContentPage
{
    private readonly BookChoice _bookchoice;
    private readonly ShowBook _showBook;

    public Menu(BookChoice bookChoice, ShowBook showBook)
    {
        InitializeComponent();
        _bookchoice = bookChoice;
        _showBook = showBook;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        AnimateServo();
    }

    private async void AnimateServo()
    {
        while (true)
        {
            await ServoImage.ScaleTo(1.1, 600, Easing.SinInOut);
            await ServoImage.ScaleTo(1.0, 600, Easing.SinInOut);
        }
    }

    private async void OnClickedShowBook(object sender, EventArgs e)
    {
        await Navigation.PushAsync(_showBook);
    }

    private async void OnClickedPlay(object sender, EventArgs e)
    {
       await Navigation.PushAsync(_bookchoice);
    }
}