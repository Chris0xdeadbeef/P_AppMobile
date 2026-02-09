using ReadMe.Services;
using ReadMe.Pages;
using ReadMe.Pages.Book;
using ReadMe.Pages.Play;
using ReadMe.Pages.Tag;
using CommunityToolkit.Maui;

namespace ReadMe
{
    /// <summary>
    /// Classe statique responsable de la configuration et de la création
    /// de l'application MAUI. C'est ici que sont enregistrés les services,
    /// les pages et les dépendances nécessaires au fonctionnement global.
    /// </summary>
    public static class MauiProgram
    {
        /// <summary>
        /// Point d'entrée principal pour construire l'application MAUI.
        /// Configure les polices, les services, les pages et les outils utilisés.
        /// </summary>
        /// <returns>Une instance entièrement configurée de <see cref="MauiApp"/>.</returns>
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();

            // Configuration de base de l'application
            builder
                .UseMauiApp<App>() // Définit la classe App comme racine
                .UseMauiCommunityToolkit()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // Enregistrement des services           

            // BookService est enregistré en Singleton pour garantir
            // une instance unique partagée dans toute l'application.
            // Cela évite les doublons et permet une gestion cohérente des decks.
            builder.Services.AddSingleton<BookService>();
            builder.Services.AddSingleton<TagService>();

            // --- Enregistrement des pages ---
            // Transient = une nouvelle instance est créée à chaque navigation
            builder.Services.AddTransient<Menu>();
            builder.Services.AddTransient<AddBook>();
            builder.Services.AddTransient<ShowBook>();
            builder.Services.AddTransient<BookChoice>();
            builder.Services.AddTransient<BookPlay>();
            builder.Services.AddTransient<AddTag>();

            return builder.Build();
        }
    }
}
