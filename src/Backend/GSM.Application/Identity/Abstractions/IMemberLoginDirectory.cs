namespace GSM.Application.Identity.Abstractions;

/// <summary>
/// FL1: lo que el login necesita de las fichas de miembro, sin que Identity lea Members. Puerto invertido: lo
/// declara Identity y lo implementa Members (como <c>IGymLoginGate</c>).
/// <para>
/// El miembro entra con su correo o su celular; la primera vez, sin cuenta, su contraseña provisional es el
/// número de documento de su ficha. Con eso se le crea la cuenta (como el canje de la invitación) y queda
/// obligado a crear su contraseña.
/// </para>
/// </summary>
public interface IMemberLoginDirectory
{
    /// <summary>
    /// Fichas activas (de cualquier gym) con ese celular colombiano. <paramref name="phone"/> es lo que la
    /// persona escribió: 10 dígitos, con o sin <c>+57</c>, espacios o guiones. Vacía si no es un celular
    /// colombiano o no hay fichas.
    /// </summary>
    Task<IReadOnlyList<MemberLoginCandidate>> FindByPhoneAsync(string phone, CancellationToken cancellationToken = default);

    /// <summary>
    /// Primer ingreso: si hay fichas activas sin cuenta con ese correo cuyo documento coincide con
    /// <paramref name="document"/>, crea la cuenta sin contraseña (debe crearla al entrar), le da acceso de
    /// miembro en esos gyms y las vincula. Devuelve el id de la cuenta, o <c>null</c> si no coincide nada.
    /// </summary>
    Task<Guid?> ProvisionFirstLoginAsync(string email, string document, CancellationToken cancellationToken = default);

    /// <summary>
    /// Segundo ingreso sin contraseña creada todavía: <c>true</c> si alguna ficha activa de esa cuenta tiene ese
    /// documento.
    /// </summary>
    Task<bool> DocumentMatchesAsync(Guid userId, string document, CancellationToken cancellationToken = default);
}

/// <param name="UserId">La cuenta vinculada a la ficha, o <c>null</c> si aún no tiene.</param>
/// <param name="Email">El correo de la ficha (para una ficha sin cuenta es con el que se crea).</param>
public sealed record MemberLoginCandidate(Guid? UserId, string? Email);
