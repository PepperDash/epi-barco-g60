using Newtonsoft.Json;

namespace Plugin.BarcoG60
{
	public class BarcoG60PropertiesConfig
	{		
		/// <summary>
		/// Poll interval in miliseconds, defaults 30,000ms (30-seconds)
		/// </summary>
        [JsonProperty("pollIntervalMs")]
        public long PollIntervalMs { get; set; }

		/// <summary>
		/// Device cooling time, defaults to 15,000ms (15-seconds)
		/// </summary>
        [JsonProperty("coolingTimeMs")]
        public uint CoolingTimeMs { get; set; }

		/// <summary>
		/// Device warming time, defaults to 15,000ms (15-seconds)
		/// </summary>
        [JsonProperty("warmingTimeMs")]
        public uint WarmingTimeMs { get; set; }

        /// <summary>
        /// Device ID
        /// </summary>
        [JsonProperty("id")]
        public uint Id { get; set; }

        /// <summary>
        /// Has lamp feedback
        /// </summary>
        [JsonProperty("hasLamps")]
        public bool HasLamps { get; set; }

        /// <summary>
        /// Has screen feedback
        /// </summary>
        [JsonProperty("hasScreen")]
        public bool HasScreen { get; set; }

        /// <summary>
        /// Has lift feedback
        /// </summary>
        [JsonProperty("hasLift")]
        public bool HasLift { get; set; }
	}
}