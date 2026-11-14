using SQLite;

namespace NebulaReader.Models;

[Table("BookBookmarks")]
public sealed class BookBookmark
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    [Indexed]
    public string BookId { get; set; } = string.Empty;

    public int PageNumber { get; set; }

    public static string CreateId(string bookId, int pageNumber) => $"{bookId}:{pageNumber}";
}
