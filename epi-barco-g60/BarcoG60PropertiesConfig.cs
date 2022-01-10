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
	}
}