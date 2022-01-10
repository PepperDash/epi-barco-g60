using PepperDash.Essentials.Core;

namespace Plugin.BarcoG60
{
    public class BarcoG60BridgeJoinMap : JoinMapBaseAdvanced
    {
        #region Digitals

        //[JoinName("PowerOff")]
        //public JoinDataComplete PowerOff = new JoinDataComplete(
        //    new JoinData
        //    {
        //        JoinNumber = 1,
        //        JoinSpan = 1
        //    },
        //    new JoinMetadata
        //    {
        //        Description = "Power Off",
        //        JoinCapabilities = eJoinCapabilities.FromSIMPL,
        //        JoinType = eJoinType.Digital
        //    });

        //[JoinName("PowerOn")]
        //public JoinDataComplete PowerOn = new JoinDataComplete(
        //    new JoinData
        //    {
        //        JoinNumber = 2,
        //        JoinSpan = 1
        //    },
        //    new JoinMetadata
        //    {
        //        Description = "Power On",
        //        JoinCapabilities = eJoinCapabilities.ToFromSIMPL,
        //        JoinType = eJoinType.Digital
        //    });

        //[JoinName("IsTwoWayDisplay")]
        //public JoinDataComplete IsTwoWayDisplay = new JoinDataComplete(
        //    new JoinData
        //    {
        //        JoinNumber = 3,
        //        JoinSpan = 1
        //    },
        //    new JoinMetadata
        //    {
        //        Description = "Is Two Way Display",
        //        JoinCapabilities = eJoinCapabilities.ToSIMPL,
        //        JoinType = eJoinType.Digital
        //    });

        //[JoinName("InputSelectOffset")]
        //public JoinDataComplete InputSelectOffset = new JoinDataComplete(
        //    new JoinData
        //    {
        //        JoinNumber = 11,
        //        JoinSpan = 10
        //    },
        //    new JoinMetadata
        //    {
        //        Description = "Input Select",
        //        JoinCapabilities = eJoinCapabilities.ToFromSIMPL,
        //        JoinType = eJoinType.Digital
        //    });

        [JoinName("HasLamps")]
        public JoinDataComplete HasLamps = new JoinDataComplete(
            new JoinData
            {
                JoinNumber = 31,
                JoinSpan = 1
            },
            new JoinMetadata
            {
                Description = "Has lamps feedback",
                JoinCapabilities = eJoinCapabilities.ToSIMPL,
                JoinType = eJoinType.Digital
            });

        [JoinName("HasScreen")]
        public JoinDataComplete HasScreen = new JoinDataComplete(
            new JoinData
            {
                JoinNumber = 32,
                JoinSpan = 1
            },
            new JoinMetadata
            {
                Description = "Has screen feedback",
                JoinCapabilities = eJoinCapabilities.ToSIMPL,
                JoinType = eJoinType.Digital
            });

        [JoinName("HasLift")]
        public JoinDataComplete HasLift = new JoinDataComplete(
            new JoinData
            {
                JoinNumber = 33,
                JoinSpan = 1
            },
            new JoinMetadata
            {
                Description = "Has lift feedback",
                JoinCapabilities = eJoinCapabilities.ToSIMPL,
                JoinType = eJoinType.Digital
            });

        //[JoinName("ButtonVisibilityOffset")]
        //public JoinDataComplete ButtonVisibilityOffset = new JoinDataComplete(
        //    new JoinData
        //    {
        //        JoinNumber = 41,
        //        JoinSpan = 10
        //    },
        //    new JoinMetadata
        //    {
        //        Description = "Button Visibility Offset",
        //        JoinCapabilities = eJoinCapabilities.ToFromSIMPL,
        //        JoinType = eJoinType.DigitalSerial
        //    });

        //[JoinName("IsOnline")]
        //public JoinDataComplete IsOnline = new JoinDataComplete(
        //    new JoinData
        //    {
        //        JoinNumber = 50,
        //        JoinSpan = 1
        //    },
        //    new JoinMetadata
        //    {
        //        Description = "Is Online",
        //        JoinCapabilities = eJoinCapabilities.ToSIMPL,
        //        JoinType = eJoinType.Digital
        //    });

        #endregion


        #region Analogs

        //[JoinName("InputSelect")]
        //public JoinDataComplete InputSelect = new JoinDataComplete(
        //    new JoinData
        //    {
        //        JoinNumber = 11,
        //        JoinSpan = 1
        //    },
        //    new JoinMetadata
        //    {
        //        Description = "Input Select",
        //        JoinCapabilities = eJoinCapabilities.ToFromSIMPL,
        //        JoinType = eJoinType.Analog
        //    });

        #endregion



        #region Serials

        //[JoinName("Name")]
        //public JoinDataComplete Name = new JoinDataComplete(
        //    new JoinData
        //    {
        //        JoinNumber = 1,
        //        JoinSpan = 1
        //    },
        //    new JoinMetadata
        //    {
        //        Description = "Name",
        //        JoinCapabilities = eJoinCapabilities.ToSIMPL,
        //        JoinType = eJoinType.Serial
        //    });

        //[JoinName("InputNamesOffset")]
        //public JoinDataComplete InputNamesOffset = new JoinDataComplete(
        //    new JoinData
        //    {
        //        JoinNumber = 11,
        //        JoinSpan = 10
        //    },
        //    new JoinMetadata
        //    {
        //        Description = "Input Names Offset",
        //        JoinCapabilities = eJoinCapabilities.ToSIMPL,
        //        JoinType = eJoinType.Serial
        //    });

        #endregion



        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="joinStart"></param>
        public BarcoG60BridgeJoinMap(uint joinStart)
            : base(joinStart, typeof(BarcoG60BridgeJoinMap))
        {

        }
    }
}