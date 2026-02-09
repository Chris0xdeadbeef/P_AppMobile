using ReadMe.Models;
using System.Collections.ObjectModel;

namespace ReadMe.Services
{
    /// <summary>
    /// Service centralisé responsable de la gestion des livres.
    /// Il agit comme une source de vérité unique pour toute l'application.
    /// </summary>
    public class BookService
    {
        /// <summary>
        /// Liste observable contenant tous les livres de l'application.
        /// ObservableCollection permet une mise à jour automatique de l'UI.
        /// </summary>
        public ObservableCollection<Book> Books { get; private set; } = [];

        public ObservableCollection<Book> GetAllBooks() => Books;

        /// <summary>
        /// Ajoute un nouveau livre à la collection globale.
        /// </summary>
        /// <param name="book">Livre à ajouter.</param>
        public void AddDeck(Book book)
        {
            Books.Add(book);
        }
    }
}
