namespace ReadMe.Pages.Play;

public partial class BookChoice : ContentPage
{
	public BookChoice()
	{
		InitializeComponent();
	}

    /// <summary>
    /// Retourne à la page précédente si possible.
    /// </summary>
    private async void OnBackClicked(object sender, EventArgs e)
    {
        await Navigation.PopToRootAsync();
    }
}