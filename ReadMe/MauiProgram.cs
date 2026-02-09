using ReadMe.Services;

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


            return builder.Build();
        }
    }
}
