namespace GSM.Application.Routines;

/// <summary>
/// Galería de imágenes para las portadas de las plantillas de rutina (RT2): fotos de Unsplash servidas por
/// <c>GSM.UI.Shared/wwwroot/routine-images</c>, recortadas a 1200×675. <c>Source</c> es la foto original (licencia de
/// Unsplash: uso gratuito, sin atribución obligatoria). Una plantilla solo puede usar una
/// imagen de esta lista; mientras no haya subidas propias del gym, es la única fuente.
/// </summary>
public static class RoutineImageGallery
{
    public const string Base = "/_content/GSM.UI.Shared/routine-images/";

    public sealed record Image(string Key, string Title, string Source)
    {
        public string Url => Base + Key + ".jpg";
    }

    public static readonly IReadOnlyList<Image> All =
    [
        new("sala-mancuernas", "Sala con mancuernas", "https://unsplash.com/photos/CQfNt66ttZM"),
        new("curl-barra", "Curl con barra", "https://unsplash.com/photos/7kEpUPB8vNk"),
        new("mancuerna-gimnasio", "Mancuerna en el gimnasio", "https://unsplash.com/photos/k47w6BeapCs"),
        new("prensa-piernas", "Prensa de piernas", "https://unsplash.com/photos/xwMlVSqP20U"),
        new("banco-gimnasio", "Máquina de pierna", "https://unsplash.com/photos/SMSpk9fprcU"),
        new("prensa-inclinada", "Prensa inclinada", "https://unsplash.com/photos/JD6RMKGwSCQ"),
        new("press-banca-pareja", "Press de banca con compañero", "https://unsplash.com/photos/i2GS_MtW9hM"),
        new("press-banca", "Press de banca", "https://unsplash.com/photos/kWmtX_NY5zM"),
        new("press-banca-asistido", "Press de banca asistido", "https://unsplash.com/photos/drmdDkStg5Q"),
        new("peso-muerto-preparacion", "Preparando el peso muerto", "https://unsplash.com/photos/WvDYdXDzkhs"),
        new("peso-muerto", "Peso muerto", "https://unsplash.com/photos/AeKsg1Uk1ZU"),
        new("barra-sobre-cabeza", "Barra sobre la cabeza", "https://unsplash.com/photos/h4i9G-de7Po"),
        new("cintas-fila", "Fila de cintas", "https://unsplash.com/photos/MsCgmHuirDo"),
        new("gimnasio-moderno", "Gimnasio moderno", "https://unsplash.com/photos/R_cN3dJLcK8"),
        new("cintas-correr", "Cintas de correr", "https://unsplash.com/photos/pCT8ag1o3nU"),
    ];

    public static bool Contains(string? url) => url is not null && All.Any(i => i.Url == url);

    public static string UrlOf(string key) => All.Single(i => i.Key == key).Url;
}
