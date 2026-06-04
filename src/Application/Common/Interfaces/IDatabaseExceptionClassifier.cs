namespace SaigonWaterbus.Application.Common.Interfaces;

public interface IDatabaseExceptionClassifier
{
    bool IsUniqueConstraintViolation(Exception exception);
}
