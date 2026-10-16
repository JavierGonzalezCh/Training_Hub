namespace GSM.UI.Shared.Security;

/// <summary>
/// Cuándo está bloqueada la app (apps y vistas §7): al volver de segundo plano tras 15 minutos en modo
/// staff, y al abrirla en frío en modo staff. El modo miembro nunca bloquea. No reemplaza a la sesión.
/// </summary>
public sealed class StaffLockState(TimeProvider time)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(15);

    private DateTimeOffset? _backgroundedAt;
    private bool _initialChecked;

    public bool IsLocked { get; private set; }

    public event Action? Changed;

    /// <summary>La app pasó a segundo plano.</summary>
    public void Backgrounded() => _backgroundedAt ??= time.GetUtcNow();

    /// <summary>La app volvió. Bloquea si estaba en modo staff y pasaron 15 minutos o más.</summary>
    public void Foregrounded(bool isStaffMode)
    {
        var since = _backgroundedAt;
        _backgroundedAt = null;
        if (isStaffMode && since is not null && time.GetUtcNow() - since.Value >= Timeout)
        {
            SetLocked(true);
        }
    }

    /// <summary>
    /// Apertura en frío: si el proceso murió con la sesión de staff guardada, se pide desbloqueo una vez.
    /// Solo cuenta la primera vez que se conoce el modo.
    /// </summary>
    public void CheckInitial(bool isStaffMode)
    {
        if (_initialChecked)
        {
            return;
        }

        _initialChecked = true;
        if (isStaffMode)
        {
            SetLocked(true);
        }
    }

    public void Unlock()
    {
        _backgroundedAt = null;
        SetLocked(false);
    }

    /// <summary>Cierre de sesión o cambio a modo miembro: nada que bloquear.</summary>
    public void Reset()
    {
        _backgroundedAt = null;
        SetLocked(false);
    }

    private void SetLocked(bool value)
    {
        if (IsLocked == value)
        {
            return;
        }

        IsLocked = value;
        Changed?.Invoke();
    }
}
