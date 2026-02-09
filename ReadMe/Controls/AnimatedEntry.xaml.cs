namespace ReadMe.Controls;

/// <summary>
/// Champ de saisie personnalisé avec une animation de soulignement.
/// Utilisé pour améliorer l'expérience utilisateur lors du focus / unfocus.
/// </summary>
public partial class AnimatedEntry : ContentView
{
    /// <summary>
    /// Accès direct au contrôle Entry interne.
    /// Permet de récupérer ou modifier ses propriétés si nécessaire.
    /// </summary>
    public Entry EntryControl => InnerEntry;

    /// <summary>
    /// Constructeur principal.
    /// Initialise le composant et charge le XAML associé.
    /// </summary>
    public AnimatedEntry()
    {
        InitializeComponent();
    }

    // -----------------------------
    //        Placeholder
    // -----------------------------

    /// <summary>
    /// BindableProperty permettant de définir le placeholder du champ.
    /// </summary>
    public static readonly BindableProperty PlaceholderProperty =
        BindableProperty.Create(
            nameof(Placeholder),
            typeof(string),
            typeof(AnimatedEntry),
            default(string)
        );

    /// <summary>
    /// Texte affiché lorsque le champ est vide.
    /// </summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    // -----------------------------
    //            Text
    // -----------------------------

    /// <summary>
    /// BindableProperty permettant de lier le texte du champ.
    /// Mode TwoWay pour synchroniser automatiquement avec la vue modèle.
    /// </summary>
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(
            nameof(Text),
            typeof(string),
            typeof(AnimatedEntry),
            default(string),
            BindingMode.TwoWay
        );

    /// <summary>
    /// Texte saisi dans le champ.
    /// </summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    // -----------------------------
    //         Animations
    // -----------------------------

    /// <summary>
    /// Anime la largeur du soulignement (Underline) entre deux valeurs.
    /// </summary>
    /// <param name="from">Largeur initiale.</param>
    /// <param name="to">Largeur finale.</param>
    private void AnimateWidth(double from, double to)
    {
        var animation = new Animation(
            v => Underline.WidthRequest = v,
            from,
            to
        );

        animation.Commit(
            this,
            "UnderlineAnim",
            16,     // Fréquence
            250,    // Durée
            Easing.CubicOut
        );
    }

    /// <summary>
    /// Déclenché lorsque le champ reçoit le focus.
    /// Agrandit le soulignement pour attirer l’attention.
    /// </summary>
    private void OnFocused(object sender, FocusEventArgs e)
    {
        AnimateWidth(Underline.WidthRequest, 300);
    }

    /// <summary>
    /// Déclenché lorsque le champ perd le focus.
    /// Réduit le soulignement et synchronise le texte avec l’Entry interne.
    /// </summary>
    private void OnUnfocused(object sender, FocusEventArgs e)
    {
        AnimateWidth(Underline.WidthRequest, 100);
        Text = InnerEntry.Text;
    }
}
