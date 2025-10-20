using ASCOM.Tools;
using System;
using ASCOM.Alpaca;

namespace GS.Alpaca.Server
{
    public class DriverManager
    {
        internal static void LoadTelescope(int DeviceID)
        {
            var dev = new Simulator.Telescope(DeviceID, Logging.Log, new XMLProfile(ServerSettings.SettingsFolderName, DeviceManager.Telescope, (uint)DeviceID));
            DeviceManager.LoadTelescope(DeviceID, dev, dev.DeviceName, dev.UniqueID);
        }

        /// <summary>
        /// Reset all device settings profiles.
        /// </summary>
        internal static void Reset()
        {
            try
            {
                Simulator.TelescopeHardware.ClearProfile();
            }
            catch (Exception ex)
            {
                Logging.LogError($"Failed to reset Telescope settings with error: {ex.Message}");
            }
        }
    }
}