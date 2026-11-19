using NebulaReader.Models;
using NebulaReader.Services;
using Xunit;

namespace NebulaReader.Tests;

public class LibraryTitlePresentationTests
{
    [Theory]
    [InlineData("Dragon Ball 01", "Dragon Ball", "01")]
    [InlineData("Naruto #12", "Naruto", "#12")]
    [InlineData("One Piece Vol. 3", "One Piece", "Vol. 3")]
    [InlineData("04 - Saga", "Saga", "04")]
    [InlineData("Saga: Tomo IV", "Saga", "Tomo IV")]
    public void SeparatesEditorialNumber(string title, string name, string number)
    {
        var parts = LibraryTitlePresentation.Split(title);

        Assert.Equal(name, parts.Name);
        Assert.Equal(number, parts.Number);
    }

    [Theory]
    [InlineData("1984")]
    [InlineData("Informe 2024")]
    [InlineData("Dragon Ball Z")]
    public void KeepsTitlesWithoutEditorialNumber(string title)
    {
        var item = LibraryDisplayItem.FromSingleBook(new BookItem { Title = title });

        Assert.Equal(title, item.DisplayTitle);
        Assert.Equal(title, item.Title);
        Assert.False(item.HasTitleNumber);
    }
}
