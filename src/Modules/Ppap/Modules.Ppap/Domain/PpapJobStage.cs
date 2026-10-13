namespace DES.Modules.Ppap.Domain;

public enum PpapJobStage
{
    UploadVerification = 0,
    PreprocessingExtraction = 1,
    DocumentClassification = 2,
    CrossValidation = 3,
    ReportGeneration = 4,
    NotificationDelivery = 5
}
