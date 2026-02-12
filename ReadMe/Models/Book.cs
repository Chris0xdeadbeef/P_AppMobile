using System.Collections.ObjectModel;

namespace ReadMe.Models
{
    /// <summary>
    /// Représente un livre stocké dans l'application ReadME.
    /// Contient les métadonnées, le contenu EPUB et les informations
    /// nécessaires à la gestion de la lecture.
    /// </summary>
    public class Book
    {
        /// <summary>
        /// Identifiant unique du livre (GUID).
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Titre du livre (extrait des métadonnées EPUB).
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Auteur du livre (extrait des métadonnées EPUB).
        /// </summary>
        public string Author { get; set; }

        /// <summary>
        /// Image de couverture en miniature (extrait de l'EPUB).
        /// Stockée en BLOB dans la base de données.
        /// </summary>
        public byte[] CoverImage { get; set; }

        /// <summary>
        /// Date d'ajout du livre dans la base de données.
        /// Permet le tri par date d’ajout.
        /// </summary>
        public DateTime DateAdded { get; set; }

        /// <summary>
        /// Contenu complet du fichier EPUB (format BLOB).
        /// </summary>
        public byte[] EpubContent { get; set; }

        /// <summary>
        /// Date d'insertion du fichier EPUB dans la base.
        /// Peut être identique à DateAdded selon l’implémentation.
        /// </summary>
        public DateTime InsertionDate { get; set; }

        /// <summary>
        /// Dernière page lue par l'utilisateur.
        /// Permet la reprise automatique de la lecture.
        /// </summary>
        private int _lastPageRead;
        public int PageCount { get; set; }

        public int LastPageRead
        {
            get => _lastPageRead;
            set => _lastPageRead = Math.Max(0, value);
        }

        /// <summary>
        /// Liste des tags associés au livre.
        /// Permet le filtrage et la classification.
        /// </summary>
        public ObservableCollection<Tag> Tags { get; set; } = [];

        /// <summary>
        /// Constructeur principal permettant d'initialiser un livre
        /// avec ses informations essentielles.
        /// </summary>
        public Book(string title, string author, byte[] epubContent, byte[] coverImage)
        {
            Id = Guid.NewGuid();
            Title = title;
            Author = author;
            EpubContent = epubContent;
            CoverImage = coverImage;


            DateAdded = DateTime.UtcNow;
            InsertionDate = DateAdded;

            LastPageRead = 0;
            Tags = [];
        }

    }
}
