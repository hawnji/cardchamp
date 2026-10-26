using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace CardVault.Core.Common
{
    /// <summary>Single JSON configuration shared by content loading and saves.</summary>
    public static class Json
    {
        public static readonly JsonSerializerSettings Settings = Create(Formatting.None);
        public static readonly JsonSerializerSettings PrettySettings = Create(Formatting.Indented);

        private static JsonSerializerSettings Create(Formatting formatting)
        {
            var settings = new JsonSerializerSettings
            {
                // Dictionary keys are IDs and must round-trip exactly, so only member names are camel-cased.
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false, OverrideSpecifiedNames = false },
                },
                Formatting = formatting,
                NullValueHandling = NullValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                ObjectCreationHandling = ObjectCreationHandling.Replace,
                DefaultValueHandling = DefaultValueHandling.Include,
                TypeNameHandling = TypeNameHandling.None,
            };
            settings.Converters.Add(new StringEnumConverter());
            return settings;
        }

        public static string Serialize(object value, bool pretty = false)
            => JsonConvert.SerializeObject(value, pretty ? PrettySettings : Settings);

        public static T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);

        public static JsonSerializer CreateSerializer() => JsonSerializer.Create(Settings);
    }
}
