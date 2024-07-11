using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;
using StardewModdingAPI.Internal.ConsoleWriting;
using System.Diagnostics;
using VdfConverter;
using VdfParser;
using System.Dynamic;
using Gameloop.Vdf;
using Gameloop.Vdf.Linq;
using System.ComponentModel.DataAnnotations;

namespace StardewModdingAPI.Installer.Framework
{
    internal class SteamIntegration
    {
        private const long STEAMID_LONG_BASE = 76561197960265728;
        private const string STARDEW_APP_ID = "413150";

        /// <summary>Handles writing text to the console.</summary>
        private IConsoleWriter ConsoleWriter;

        /// <summary>Print a warning message.</summary>
        /// <param name="text">The text to print.</param>
        private void PrintError(string text)
        {
            this.ConsoleWriter.WriteLine(text, ConsoleLogLevel.Error);
        }

        /// <summary>Print a debug message.</summary>
        /// <param name="text">The text to print.</param>
        private void PrintInfo(string text)
        {
            this.ConsoleWriter.WriteLine(text, ConsoleLogLevel.Info);
        }

        internal SteamIntegration(IConsoleWriter ConsoleWriter)
        {
            this.ConsoleWriter = ConsoleWriter;
        }

        /// <summary>Check if Steam is running</summary>
        internal bool IsSteamRunning()
        {
            Process[] processes = Process.GetProcessesByName("steam");
            // If there are processes, the length will be 1 or higher.
            return processes.Length > 0;
        }

        /// <summary>Convert a long Steam ID, found in loginusers.vdf to the ones used in localconfig.vdf.</summary>
        /// <param name="longID">The long Steam ID.</param>
        private string ShortenSteamId(string longID)
        {
            // Convert the long Steam ID string to a long
            if (!long.TryParse(longID, out long longSteamID))
            {
                throw new ArgumentException("Invalid Long SteamID format");
            }

            // Calculate the short ID
            long steamID32 = longSteamID - STEAMID_LONG_BASE;

            return steamID32.ToString();
        }

        internal string? GetSelectedSteamUser()
        {
            string loginUsersPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Steam",
                "config",
                "loginusers.vdf"
            );

            using var loginUsersVdfFile = File.OpenRead(loginUsersPath);
            var loginUsersDeserializer = new VdfDeserializer();
            if (loginUsersDeserializer.Deserialize(loginUsersVdfFile)
                is not IDictionary<string, object> loginUsersResult)
            {
                this.PrintError($"Failed to parse {loginUsersPath}");
                return null;
            }

            object? loginUsers = null;
            if (!loginUsersResult.TryGetValue("users", out loginUsers))
            {
                this.PrintError($"Failed to parse {loginUsersPath}");
                return null;
            }

            string? recentFullID = null;

            if (loginUsers is IDictionary<string, object> loginDictionary)
            {
                // Loop through the dictionary's key/value pairs, and find the one where its value's "MostRecent" field is 1, and then return that key.
                foreach (var kvp in loginDictionary)
                {
                    if (kvp.Value is IDictionary<string, object> valueDict &&
                        valueDict.TryGetValue("MostRecent", out var recentValue) &&
                        (recentValue is string || recentValue is int) && recentValue.ToString() == "1")
                    {
                        recentFullID = kvp.Key;
                    }
                }
            }

            if (recentFullID is null)
            {
                this.PrintError($"Failed to find active Steam account.");
                return null;
            }

            return this.ShortenSteamId(recentFullID);

        }

        /// <summary>Interactively wait for steam to close.</summary>
        /// <param name="printLine">A callback which prints a message to the console.</param>
        /// <param name="message">The message to print.</param>
        /// <param name="options">The allowed options (not case sensitive).</param>
        /// <param name="indent">The indentation to prefix to output.</param>
        internal string InteractivelyAwaitSteamClose(string message, string[] options, string indent = "", Action<string>? printLine = null)
        {
            printLine ??= this.PrintInfo;

            while (true)
            {
                printLine(indent + message);
                Console.Write(indent);
                string? input = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (input == null || !options.Contains(input))
                {
                    printLine($"{indent}That's not a valid option.");
                    continue;
                } else if (this.IsSteamRunning())
                {
                    printLine($"{indent}Steam is still running!");
                    continue;
                }
                return input;
            }
        }

        /// <summary>Modify launch options for Stardew Valley.</summary>
        /// <param name="newLaunchOption">The new launch option for the game</param>
        internal void ModifyLaunchOptions(string newLaunchOption)
        {
            string selectedSteamUser = this.GetSelectedSteamUser();
            if (selectedSteamUser is null)
            {
                this.PrintError("Failed to obtain current Steam account.");
                return;
            }

            string localConfigPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Steam",
                "userdata",
                selectedSteamUser,
                "config",
                "localconfig.vdf"
            );
            string contents = File.ReadAllText(localConfigPath);
            File.WriteAllText(localConfigPath + ".bak", contents);
            this.PrintInfo("Backed up localconfig.");
            dynamic result = VdfConvert.Deserialize(contents, new VdfSerializerSettings() { MaximumTokenSize = 16384, UsesEscapeSequences = true });

            var SDV = result.Value.Software.Valve.Steam.apps[STARDEW_APP_ID];
            if (SDV is null)
            {
                this.PrintError($"Failed to find Stardew Valley in {localConfigPath}");
                return;
            }

            SDV.LaunchOptions = newLaunchOption;

            string output = VdfConvert.Serialize(result);
            File.WriteAllText(localConfigPath, output);

            Console.WriteLine();

        }
    }
}
