using System.Globalization;
using NebulaReader.Models;

namespace NebulaReader.Views;

public partial class LibraryPage
{
    private async Task<DocumentMetadataEdit?> ShowEditDocumentMetadataDialogAsync(BookItem book)
    {
        var form = new VerticalStackLayout { Spacing = 10 };

        Entry AddField(string label, string? value, string placeholder, int maxLength)
        {
            var entry = new Entry
            {
                Text = value ?? string.Empty,
                Placeholder = placeholder,
                MaxLength = maxLength,
                TextColor = Color.FromArgb("#F5F3FF"),
                PlaceholderColor = Color.FromArgb("#9F94C4"),
                BackgroundColor = Colors.Transparent,
                HorizontalOptions = LayoutOptions.Fill
            };
            form.Children.Add(CreateMetadataSection(label, entry));
            return entry;
        }

        var title = AddField("Título", book.Title, "Título del documento", 255);
        var series = AddField("Serie", book.SeriesName, "Serie o saga", 255);
        var author = AddField("Autor", book.Author, "Nombre del autor", 255);
        var date = AddField("Fecha de publicación", book.PublicationDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "AAAA-MM-DD", 10);
        var publisher = AddField("Editorial", book.Publisher, "Nombre de la editorial", 255);
        var language = AddField("Idioma", book.Language, "Idioma del documento", 100);

        var description = new Editor
        {
            Text = book.Description ?? string.Empty,
            Placeholder = "Sinopsis o notas del documento",
            MaxLength = 4000,
            AutoSize = EditorAutoSizeOption.TextChanges,
            HeightRequest = 90,
            TextColor = Color.FromArgb("#F5F3FF"),
            PlaceholderColor = Color.FromArgb("#9F94C4"),
            BackgroundColor = Colors.Transparent
        };
        form.Children.Add(CreateMetadataSection("Descripción", description));
        var tags = AddField("Etiquetas", book.Tags, "Separa las etiquetas con comas", 1000);

        DocumentMetadataEdit? editedMetadata = null;
        bool CanSave()
        {
            string? error = null;
            DateTime? publicationDate = null;
            var titleText = (title.Text ?? string.Empty).Trim();
            var tagValues = (tags.Text ?? string.Empty)
                .Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var normalizedTags = string.Join(", ", tagValues);

            if (titleText.Length == 0)
                error = "Escribe un título para el documento.";
            else if (!string.IsNullOrWhiteSpace(date.Text))
            {
                if (DateTime.TryParseExact(date.Text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsedDate))
                    publicationDate = parsedDate.Date;
                else
                    error = "La fecha debe tener el formato AAAA-MM-DD y ser válida.";
            }

            if (error == null && tagValues.Length > 32)
                error = "Puedes guardar hasta 32 etiquetas.";
            else if (error == null && normalizedTags.Length > 1000)
                error = "Las etiquetas superan el límite de 1000 caracteres.";
            else if (error == null && tagValues.Distinct(StringComparer.OrdinalIgnoreCase).Count() != tagValues.Length)
                error = "Hay etiquetas repetidas. Quita las duplicadas.";

            NebulaDialogErrorLabel.Text = error ?? string.Empty;
            NebulaDialogErrorLabel.IsVisible = error != null;
            if (error != null) return false;

            editedMetadata = new DocumentMetadataEdit(titleText, series.Text, author.Text,
                publicationDate, publisher.Text, language.Text, description.Text, normalizedTags);
            return true;
        }

        var result = await ShowNebulaDialogCoreAsync("EDITAR METADATOS", "Metadatos del documento",
            "Completa los campos que correspondan al documento.",
            acceptText: "Guardar", cancelText: "Cancelar", customViews: [form], canAccept: CanSave);
        return result == "__ACCEPT__" ? editedMetadata : null;
    }

    private static Border CreateMetadataSection(string label, View field)
    {
        var content = new VerticalStackLayout { Spacing = 4 };
        content.Children.Add(new Label
        {
            Text = label,
            TextColor = Color.FromArgb("#D884FF"),
            FontFamily = "OpenSansSemibold",
            FontSize = 13
        });
        content.Children.Add(field);
        return new Border
        {
            BackgroundColor = Color.FromArgb("#1C1830"),
            Stroke = Color.FromArgb("#2A2346"),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(12, 8),
            Content = content
        };
    }
}
