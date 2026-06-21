namespace SaigonWaterbus.Application.Common.Interfaces;

internal sealed class NoOpDatabaseExceptionClassifier : IDatabaseExceptionClassifier
{
    public static readonly NoOpDatabaseExceptionClassifier Instance = new();

    private NoOpDatabaseExceptionClassifier()
    {
    }

    public bool IsUniqueConstraintViolation(Exception exception) => false;

    public bool IsExclusionConstraintViolation(Exception exception) => false;
}
