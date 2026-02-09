using Microsoft.Maui.Layouts;
using ReadMe.Pages.Book;
using ReadMe.Pages.Play;

namespace ReadMe.Pages;

public partial class Menu : ContentPage
{
    private bool _starsCreated;
    private readonly BookChoice _bookchoice;
    private readonly ShowBook _showBook;

    public Menu(BookChoice bookChoice, ShowBook showBook)
    {
        InitializeComponent();
        _bookchoice = bookChoice;
        _showBook = showBook;

        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object? sender, EventArgs e)
    {
        if (_starsCreated || Width <= 0 || Height <= 0)
            return;

        _starsCreated = true;
        CreateStars(40);
    }

    private void CreateStars(byte count)
    {
        Random random = new();

        for (byte i = 0; i < count; ++i)
        {
            float size = 0.01f;

            BoxView star = new()
            {
                Color = Colors.White,
                Opacity = random.NextDouble() * 0.3f,
                CornerRadius = 100
            };

            AbsoluteLayout.SetLayoutBounds(star, new Rect(
                random.NextDouble(),
                random.NextDouble(),
                size,
                size
            ));
            AbsoluteLayout.SetLayoutFlags(star, AbsoluteLayoutFlags.All);

            StarsLayer.Children.Add(star);

            AnimateStar(star);
        }
    }

    private static async void AnimateStar(View star)
    {
        Random random = new();

        while (true)
        {
            // Fade vers 1
            await star.FadeTo(1.0, (uint)random.Next(500, 1500), Easing.SinInOut);

            // Pause courte
            await Task.Delay(random.Next(100, 500));

            // Fade vers 0
            await star.FadeTo(0.0, (uint)random.Next(500, 1500), Easing.SinInOut);

            // Nouvelle position aléatoire
            double size = AbsoluteLayout.GetLayoutBounds(star).Width;
            double newX = random.NextDouble();
            double newY = random.NextDouble();

            AbsoluteLayout.SetLayoutBounds(star, new Rect(newX, newY, size, size));
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