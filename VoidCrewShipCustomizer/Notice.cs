using System;
using Gameplay.Chat;

namespace VoidCrewShipCustomizer
{
    /// <summary>Messages the player must see wherever they are: a line in their own text chat, plus the log.</summary>
    internal static class Notice
    {
        private const string Sender = "Ship Customizer";

        public static void Show(string message)
        {
            Log.Info(message);
            try
            {
                if (TextChat.Instance != null) TextChat.Instance.AddLog(new LogEntry(Sender, message));
            }
            catch (Exception e)
            {
                Log.Verbose("Could not show a notice in the chat: " + e.Message);
            }
            TerminalUi.ShowToast(message);
        }
    }
}
