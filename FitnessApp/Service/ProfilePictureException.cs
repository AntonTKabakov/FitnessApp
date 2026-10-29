namespace FitnessApp.Service;

public sealed class ProfilePictureException(int statusCode, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public int StatusCode { get; } = statusCode;
}
