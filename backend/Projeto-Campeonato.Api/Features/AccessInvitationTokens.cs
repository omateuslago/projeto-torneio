using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Projeto_Campeonato.Api.Features;

public interface IAccessInvitationTokens
{
    string Create(Guid invitationId);
    bool TryRead(string token, out Guid invitationId);
}

public sealed class AccessInvitationTokens(IDataProtectionProvider provider) : IAccessInvitationTokens
{
    private readonly ITimeLimitedDataProtector protector = provider
        .CreateProtector("Brakvyo.AccessInvitation.v1").ToTimeLimitedDataProtector();

    public string Create(Guid invitationId) => protector.Protect(invitationId.ToString("N"), TimeSpan.FromHours(72));

    public bool TryRead(string token, out Guid invitationId)
    {
        invitationId = default;
        if (string.IsNullOrWhiteSpace(token)) return false;
        try
        {
            return Guid.TryParseExact(protector.Unprotect(token), "N", out invitationId);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
