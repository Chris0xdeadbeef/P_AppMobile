using ReadMe.Models;
using System.Collections.ObjectModel;

namespace ReadMe.Services
{
    /// <summary>
    /// Service responsable de la gestion des tags.
    /// Contient les tags par défaut et permet d'en ajouter de nouveaux.
    /// </summary>
    public class TagService
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
                // Univers général
                new("Warhammer 40K"),
                new("41e Millénaire"),
                new("Grimdark"),
                new("Hérésie d’Horus"),
                new("Croisade Indomitus"),

                // Imperium
                new("Imperium"),
                new("Adeptus Terra"),
                new("Inquisition"),
                new("Ecclésiarchie"),
                new("Adeptus Custodes"),
                new("Astra Militarum"),
                new("Sœurs de Bataille"),
                new("Chevaliers Impériaux"),

                // Space Marines
                new("Space Marines"),
                new("Ultramarines"),
                new("Blood Angels"),
                new("Dark Angels"),
                new("Space Wolves"),
                new("Black Templars"),
                new("Salamanders"),
                new("Iron Hands"),
                new("Raven Guard"),
                new("Deathwatch"),
                new("Grey Knights"),

                // Adeptus Mechanicus
                new("Adeptus Mechanicus"),
                new("Tech-Priests"),
                new("Skitarii"),
                new("Legio Cybernetica"),
                new("Machine-Esprit"),
                new("Omnimessie"),
                new("Forge Worlds"),
                new("Titan Legions"),

                // Chaos
                new("Chaos"),
                new("Chaos Space Marines"),
                new("Black Legion"),
                new("World Eaters"),
                new("Death Guard"),
                new("Thousand Sons"),
                new("Emperor’s Children"),
                new("Démons du Chaos"),
                new("Khorne"),
                new("Tzeentch"),
                new("Nurgle"),
                new("Slaanesh"),

                // Xenos
                new("Xenos"),
                new("Orks"),
                new("Eldars"),
                new("Drukhari"),
                new("Harlequins"),
                new("Tyranids"),
                new("Genestealer Cults"),
                new("Necrons"),
                new("Dynasties Nécrons"),
                new("T’au"),
                new("Leagues of Votann"),

                // Thèmes narratifs
                new("Guerre"),
                new("Croisade"),
                new("Hérésie"),
                new("Exterminatus"),
                new("Relique Archéotech"),
                new("Forteresse Monastère"),
                new("Monde-Forge"),
                new("Secteur Impérial"),
                new("Conflit Planétaire")
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
