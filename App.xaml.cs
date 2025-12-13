using System;
using System.Windows;

namespace WowQuestTtsTool
{
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // Verhindert dass App sich schliesst wenn kein Fenster offen ist
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            try
            {
                // Startup-Dialog anzeigen (modal)
                var startupDialog = new StartupDialog();
                startupDialog.ShowDialog(); // Blockiert bis Dialog geschlossen wird

                System.Diagnostics.Debug.WriteLine($"StartupDialog closed. UserChoice: {startupDialog.UserChoice}");

                if (startupDialog.UserChoice == StartupDialog.StartupChoice.Cancel)
                {
                    System.Diagnostics.Debug.WriteLine("User cancelled, shutting down.");
                    Shutdown();
                    return;
                }

                System.Diagnostics.Debug.WriteLine("Creating MainWindow...");

                // MainWindow mit der Benutzerauswahl starten
                var mainWindow = new MainWindow(
                    startupDialog.UserChoice,
                    startupDialog.SelectedProjectPath,
                    startupDialog.LoadOnlyVoiced);

                System.Diagnostics.Debug.WriteLine("MainWindow created successfully.");

                // Ab jetzt: App schliesst wenn MainWindow geschlossen wird
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                MainWindow = mainWindow;

                System.Diagnostics.Debug.WriteLine("Showing MainWindow...");
                mainWindow.Show();
                System.Diagnostics.Debug.WriteLine("MainWindow shown.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Fehler beim Starten der Anwendung:\n\n{ex.Message}\n\n{ex.StackTrace}",
                    "Startfehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown();
            }
        }
    }
}
