namespace SocialTapXboxActivity;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Diagnostic mode: "--detect-to <file>" writes what the game detector sees, then exits.
        if (args.Length == 2 && args[0] == "--detect-to")
        {
            var game = GameDetector.FindRunningGame();
            File.WriteAllText(args[1], game is null ? "none" : $"{game.Title}|{game.ProcessName}|{game.CoverPath}");
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
