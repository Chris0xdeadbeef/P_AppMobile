namespace ReadMe.Pages;

/// <summary>
/// Page d’accueil affichée au lancement de l’application.
/// Sert d’écran de chargement avant d’accéder au menu principal.
/// </summary>
public partial class SplashScreen : ContentPage
{
    private readonly Menu _menu;

    /// <summary>
    /// Constructeur principal.
    /// Initialise l’écran de splash et stocke la page Menu
    /// qui sera affichée après l’animation.
    /// </summary>
    /// <param name="menu">Page du menu principal.</param>
    public SplashScreen(Menu menu)
    {
        InitializeComponent();
        _menu = menu;
    }

    /// <summary>
    /// Déclenché lorsque la page apparaît.
    /// Attend quelques secondes puis remplace la page principale
    /// par une NavigationPage contenant le menu.
    /// </summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await Task.Delay(5000);

        Application.Current.MainPage = new NavigationPage(_menu);       
    }
}
