using System.Collections.Generic;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Config;

namespace Plugin.BarcoG60
{
    public class BarcoG60ControllerFactory:EssentialsPluginDeviceFactory<BarcoG60Controller>
    {
        public BarcoG60ControllerFactory()
        {
            TypeNames = new List<string> {"barcoG60", "barcoG60Projector" };

            MinimumEssentialsFrameworkVersion = "1.10.3";
        }

        #region Overrides of EssentialsDeviceFactory<BarcoG60Controller>

        public override EssentialsDevice BuildDevice(DeviceConfig dc)
        {
            var comms = CommFactory.CreateCommForDevice(dc);

            if (comms == null) return null;

            var config = dc.Properties.ToObject<BarcoG60PropertiesConfig>();

            return config == null ? null : new BarcoG60Controller(dc.Key, dc.Name, config, comms);
        }

        #endregion
    }
}