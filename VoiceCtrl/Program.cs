using System.Windows.Forms;

namespace VoiceCtrl;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var app = new VoiceCtrlApplicationContext();
        Application.Run(app);
    }
}
