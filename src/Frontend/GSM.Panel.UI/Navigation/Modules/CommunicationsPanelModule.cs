using MudBlazor;

namespace GSM.Panel.UI.Navigation.Modules;

/// <summary>Lo que Communications aporta al menú y a la configuración del panel.</summary>
public sealed class CommunicationsPanelModule : IPanelModule
{
    public string Module => "Communications";

    public IEnumerable<PanelMenuEntry> MenuEntries =>
    [
        new("Comunicaciones", Icons.Material.Outlined.Campaign, "/communications", PanelSections.Business, 40),
    ];

    public IEnumerable<PanelSettingsSection> SettingsSections =>
        [new("Mensajes a los miembros", 40, typeof(Pages.Settings.CommunicationsTab), Group: PanelSettingsGroups.Communications,
            Description: "Aviso de vencimiento, felicitación de cumpleaños y plantillas de WhatsApp.",
            TracksUnsavedChanges: true)];
}
