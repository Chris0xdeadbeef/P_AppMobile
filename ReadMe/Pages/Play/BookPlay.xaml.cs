namespace ReadMe.Pages.Play;

public partial class BookPlay : ContentPage
{
	public BookPlay()
	{
		InitializeComponent();
	}

    /// <summary>
    /// Retourne à la page précédente si possible.
    /// </summary>
    private async void OnBackClicked(object sender, EventArgs e)
    {
        if (Navigation.NavigationStack.Count > 1)
            await Navigation.PopAsync();
    }
}