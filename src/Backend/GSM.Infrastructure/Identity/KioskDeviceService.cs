using System.Security.Cryptography;
using System.Text;
using GSM.Application.Common.Abstractions;
using GSM.Application.Identity.Abstractions;
using GSM.Domain.Common.Exceptions;
using GSM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GSM.Infrastructure.Identity;

public sealed class KioskDeviceService(GsmDbContext dbContext, IClock clock) : IKioskDeviceService
{
    // Sin caracteres ambiguos (0/O, 1/I/L): el código se digita a mano en un equipo.
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 8;

    public async Task<IReadOnlyList<KioskDeviceRecord>> ListAsync(Guid gymId, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var devices = await dbContext.KioskDevices
            .AsNoTracking()
            .Where(k => k.GymId == gymId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(cancellationToken);

        return devices.Select(d => ToRecord(d, now)).ToList();
    }

    public async Task<(KioskDeviceRecord Device, string PairingCode)> CreateAsync(
        Guid gymId, string name, Guid createdBy, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var code = GenerateCode();
        var device = KioskDevice.Create(gymId, name, createdBy, Hash(code), now);

        dbContext.KioskDevices.Add(device);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (ToRecord(device, now), code);
    }

    public async Task<KioskDeviceRecord> RevokeAsync(Guid gymId, Guid deviceId, CancellationToken cancellationToken = default)
    {
        var device = await dbContext.KioskDevices
            .FirstOrDefaultAsync(k => k.Id == deviceId && k.GymId == gymId, cancellationToken)
            ?? throw NotFoundException.For("Kiosco", deviceId);

        var now = clock.UtcNow;
        device.Revoke(now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToRecord(device, now);
    }

    public async Task<KioskPairing?> PairAsync(string pairingCode, CancellationToken cancellationToken = default)
    {
        var hash = Hash(Normalize(pairingCode));
        var now = clock.UtcNow;
        var device = await dbContext.KioskDevices
            .FirstOrDefaultAsync(k => k.PairingCodeHash == hash, cancellationToken);

        if (device is null || !device.CanPair(now))
        {
            return null;
        }

        var refresh = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        device.Pair(Hash(refresh), now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new KioskPairing(ToRecord(device, now), refresh);
    }

    public async Task<KioskDeviceRecord?> ResolveActiveAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var hash = Hash(refreshToken);
        var device = await dbContext.KioskDevices
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.RefreshTokenHash == hash && k.RevokedAt == null, cancellationToken);

        return device is null ? null : ToRecord(device, clock.UtcNow);
    }

    private static string Normalize(string code) => code.Trim().Replace("-", "").Replace(" ", "").ToUpperInvariant();

    private static string GenerateCode()
    {
        Span<char> chars = stackalloc char[CodeLength];
        for (var i = 0; i < CodeLength; i++)
        {
            chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        }

        return new string(chars);
    }

    private static string Hash(string value)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static KioskDeviceRecord ToRecord(KioskDevice d, DateTimeOffset now)
    {
        var status = d.RevokedAt is not null ? "revoked"
            : d.PairedAt is not null ? "paired"
            : d.PairingCodeExpiresAt > now ? "pending"
            : "expired";

        return new KioskDeviceRecord(
            d.Id, d.GymId, d.Name, status, d.CreatedAt, d.PairingCodeExpiresAt, d.PairedAt, d.RevokedAt);
    }
}
