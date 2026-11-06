using DockPad.Services.UpdateHost;

namespace DockPad;

public partial class UpdatesDialog
{
    public UpdatesDialog() : this(DockPadUpdates.Service, DockPadUpdates.DialogHost) { }
}
