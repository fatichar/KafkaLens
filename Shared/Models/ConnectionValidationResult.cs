namespace KafkaLens.Shared.Models;

public sealed record ConnectionValidationResult(bool Succeeded, string? ErrorMessage, string? ErrorDetails)
{
    public static ConnectionValidationResult Success() => new(true, null, null);

    public static ConnectionValidationResult Failed(string? errorMessage = null, string? errorDetails = null) =>
        new(false, errorMessage, errorDetails);
}
