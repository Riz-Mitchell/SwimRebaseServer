using System.Security.Cryptography;
using Auth.Application.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace Auth.Infrastructure;

public sealed class DeletionConfirmation(IDataProtectionProvider dataProtection) : IDeletionConfirmation
{
    private readonly ITimeLimitedDataProtector protector =
        dataProtection.CreateProtector("Auth.AccountDeletion").ToTimeLimitedDataProtector();

    public string Issue(Guid userId, DateTimeOffset expiresAt) => protector.Protect(userId.ToString(), expiresAt);

    public bool IsValid(Guid userId, string token)
    {
        try
        {
            return protector.Unprotect(token) == userId.ToString();
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
