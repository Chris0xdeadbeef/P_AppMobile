using ReadMe.Models;
using System.Collections.ObjectModel;

namespace ReadMe.Services
{
    /// <summary>
    /// Service responsable de la gestion des tags.
    /// Contient les tags par défaut et permet d'en ajouter de nouveaux.
    /// </summary>
    internal class TagService
    {
        /// <summary>
        /// Liste observable contenant tous les tags de l'application.
        /// ObservableCollection permet une mise à jour automatique de l'UI.
        /// </summary>
        public ObservableCollection<Tag> _tags;

        /// <summary>
        /// Initialise le service avec des tags par défaut.
        /// </summary>
        public TagService()
        {
            _tags =
            [
                new("Warhammer 40K"),
                new("Space Marines"),
                new("Adeptus Mechanicus"),
                new("Imperium"),
                new("Chaos"),
                new("Xenos"),
            ];
        }

        /// <summary>
        /// Retourne tous les tags disponibles.
        /// </summary>
        public IEnumerable<Tag> GetAll() => _tags;

        /// <summary>
        /// Ajoute un nouveau tag à la collection.
        /// </summary>
        public void Add(Tag tag) => _tags.Add(tag);

        /// <summary>
        /// Recherche un tag par son identifiant.
        /// </summary>
        public Tag? GetById(Guid id) =>
            _tags.FirstOrDefault(t => t.Id == id);
    }
}
