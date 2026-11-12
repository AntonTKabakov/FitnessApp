namespace FitnessApp.Logging;

public class ErrorFileLoggerOptions
{
    public const string SectionName = "ErrorLogging";

    public string DirectoryPath { get; set; } = "logs";
}
