namespace SaigonWaterbus.Application.Common.Exceptions;

public sealed class AccountNotCompletedException : Exception
{
    public const string AccountNotCompletedCode = "ACCOUNT_NOT_COMPLETED";
    public const string PhoneNotVerifiedCode = "PHONE_NOT_VERIFIED";

    public AccountNotCompletedException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
