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

    private void AnimateServo()
    {
        // Animation personnalisée
        var animation = new Animation();

        animation.Add(0, 0.5, new Animation(v =>
        {
            ServoImage.Scale = v;
        }, 1.0, 1.1, Easing.SinInOut));

        animation.Add(0.5, 1.0, new Animation(v =>
        {
            ServoImage.Scale = v;
        }, 1.1, 1.0, Easing.SinInOut));

        // Lancer l’animation en boucle
        animation.Commit(
            owner: this,
            name: "servoPulse",
            length: 1200,
            repeat: () => true
        );
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        ServoImage.AbortAnimation("servoPulse");
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