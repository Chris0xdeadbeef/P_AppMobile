namespace ReadMe.Models
{
    /// <summary>
    /// Représente un tag permettant de classifier les livres.
    /// Utilisé pour filtrer et organiser la bibliothèque.
    /// </summary>
    internal class Tag
    {
        /// <summary>
        /// Identifiant unique du tag (GUID).
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Nom du tag (ex: "Fantasy", "Horreur", "Classique").
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Liste des livres associés à ce tag.
        /// Permet la navigation inverse (optionnelle selon ton ORM).
        /// </summary>
        public List<Book> Books { get; set; }

        /// <summary>
        /// Constructeur principal pour créer un tag valide.
        /// </summary>
        public Tag(string name)
        {
            Id = Guid.NewGuid();
            Name = name;
            Books = [];
        }
    }
}
