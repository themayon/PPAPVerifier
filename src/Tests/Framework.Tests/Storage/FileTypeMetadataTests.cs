using DES.Framework.Storage;

namespace Framework.Tests.Storage;

public sealed class FileTypeMetadataTests
{
    #region Happy Path

    [Fact]
    public void GetRules_Should_ReturnImageRules_When_ImageRequested()
    {
        // Act
        var rules = FileTypeMetadata.GetRules(FileType.Image);

        // Assert
        rules.MaxSizeInMB.ShouldBe(5);
        rules.AllowedExtensions.ShouldContain(".png");
        rules.AllowedExtensions.ShouldContain(".jpg");
        rules.AllowedExtensions.ShouldContain(".jpeg");
        rules.AllowedExtensions.ShouldContain(".ico");
    }

    [Fact]
    public void GetRules_Should_ReturnPdfRules_When_PdfRequested()
    {
        // Act
        var rules = FileTypeMetadata.GetRules(FileType.Pdf);

        // Assert
        rules.MaxSizeInMB.ShouldBe(10);
        rules.AllowedExtensions.ShouldHaveSingleItem();
        rules.AllowedExtensions.ShouldContain(".pdf");
    }

    [Fact]
    public void GetRules_Should_ReturnDocumentRules_When_DocumentRequested()
    {
        var rules = FileTypeMetadata.GetRules(FileType.Document);

        rules.MaxSizeInMB.ShouldBe(25);
        rules.AllowedExtensions.ShouldContain(".xlsx");
        rules.AllowedExtensions.ShouldContain(".json");
        rules.AllowedExtensions.ShouldContain(".txt");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GetRules_Should_Throw_When_TypeUnsupported()
    {
        // An undefined enum value falls into the default arm.
        Should.Throw<NotSupportedException>(() => FileTypeMetadata.GetRules((FileType)999));
    }

    [Fact]
    public void FileValidationRules_Should_DefaultToFiveMb_When_NotSet()
    {
        // Arrange & Act
        var rules = new FileValidationRules();

        // Assert
        rules.MaxSizeInMB.ShouldBe(5);
        rules.AllowedExtensions.ShouldBeEmpty();
    }

    #endregion
}
