using NebulaReader.Models;
using NebulaReader.Services;
using Xunit;

namespace NebulaReader.Tests;

public class LibraryTitlePresentationTests
{
    [Theory]
    [InlineData("Dragon Ball 01", "Dragon Ball", "#01")]
    [InlineData("Naruto #12", "Naruto", "#12")]
    [InlineData("One Piece Vol. 3", "One Piece", "#3")]
    [InlineData("Katekyo Hitman Tomo 01", "Katekyo Hitman", "#01")]
    [InlineData("Galaxia Volumen 002", "Galaxia", "#002")]
    [InlineData("Historia Capítulo 12", "Historia", "#12")]
    [InlineData("04 - Saga", "Saga", "#04")]
    [InlineData("Saga: Tomo IV", "Saga", "#IV")]
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

    [Theory]
    [InlineData("DB VOLUMEN1", "Dragon Ball", "Dragon Ball", "#1")]
    [InlineData("Katekyo Hitman Reborn - Tomo 01", "REBORN!", "REBORN!", "#01")]
    [InlineData("Documento_Sin_Numero", "Plan Empresarial", "Plan Empresarial", null)]
    public void ReplacesBaseTitleWhilePreservingEditorialVolumeNumber(
        string originalTitle, string chosenOnlineTitle, string expectedDisplayTitle, string? expectedNumber)
    {
        var updatedTitle = LibraryTitlePresentation.ReplaceName(originalTitle, chosenOnlineTitle);
        var parts = LibraryTitlePresentation.Split(updatedTitle);

        Assert.Equal(expectedDisplayTitle, parts.Name);
        Assert.Equal(expectedNumber, parts.Number);
    }
}
