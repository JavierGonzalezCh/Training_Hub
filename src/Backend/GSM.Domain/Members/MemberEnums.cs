namespace GSM.Domain.Members;

/// <summary>Tipo de documento de identidad. Cédula de ciudadanía, tarjeta de identidad, etc.</summary>
public enum DocumentType
{
    Cc,
    Ti,
    Ce,
    Pa,
    Ppt,
    Nit,
    Other,
}

public enum Gender
{
    Female,
    Male,
    Other,
    Undisclosed,
}

/// <summary>Cómo se enteró el miembro del gym.</summary>
public enum MemberSource
{
    WalkIn,
    Referral,
    SocialMedia,
    Google,
    Flyer,
    Corporate,
    Other,
}

public enum MemberGoal
{
    WeightLoss,
    MuscleGain,
    Fitness,
    Health,
    Sport,
    Other,
}

/// <summary>Desde dónde se hizo un cambio de la ficha.</summary>
public enum MemberChangeSource
{
    Panel,
    App,
    Import,
    System,
}

/// <summary>Rol de quien hizo un cambio de la ficha.</summary>
public enum MemberChangeRole
{
    Member,
    Staff,
    Owner,
    Superadmin,
    System,
}

/// <summary>Tipo de autorización legal que firma el miembro (Members v2, §3.4).</summary>
public enum ConsentType
{
    /// <summary>Autorización de tratamiento de datos (Ley 1581).</summary>
    DataProcessing,

    /// <summary>Exoneración de responsabilidad.</summary>
    LiabilityWaiver,
}

/// <summary>Por dónde se aceptó una autorización.</summary>
public enum ConsentChannel
{
    App,
    FrontDesk,
    Import,
}
