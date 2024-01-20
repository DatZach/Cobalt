using System.Reflection;
using System.Text.Json;

namespace Emulator
{
    internal sealed class RuntimeConfig
    {
        public string? MicrocodePath { get; private set; }

        public string? BootAsmPath { get; private set; }

        public bool ExportArtifacts { get; private set; }

        public bool DebugOutput { get; private set; }

        public bool ShutdownWhenHalted { get; private set; }

        public string? TestAsm { get; private set; }

        public IReadOnlyList<DeviceConfigBase> Devices { get; private set; }

        private RuntimeConfig()
        {
            // NOTE Private ctor to enforce factory pattern
        }

        public static RuntimeConfig? FromCommandLine(string[] args)
        {
            if (args.Length == 0)
                return null;
            
            var configPath = OptionalArgument<string>("--config");
            var dict = string.IsNullOrEmpty(configPath)
                     ? ParseCommandLineConfig(args)
                     : ParseJsonConfig(configPath);

            var deviceTypes = DeviceManager.GetDeviceTypes();
            var config = new RuntimeConfig();
            var devices = new List<DeviceConfigBase>();
            foreach (var kvp in dict)
            {
                if (kvp.Key == nameof(MicrocodePath))
                    config.MicrocodePath = kvp.Value as string;
                else if (kvp.Key == nameof(BootAsmPath))
                    config.BootAsmPath = kvp.Value as string;
                else if (kvp.Key == nameof(ExportArtifacts))
                    config.ExportArtifacts = (kvp.Value as bool?).GetValueOrDefault(false);
                else if (kvp.Key == nameof(DebugOutput))
                    config.DebugOutput = (kvp.Value as bool?).GetValueOrDefault(false);
                else if (kvp.Key == nameof(ShutdownWhenHalted))
                    config.ShutdownWhenHalted = (kvp.Value as bool?).GetValueOrDefault(false);
                else if (kvp.Key == nameof(TestAsm))
                    config.TestAsm = kvp.Value as string;
                else
                {
                    // TODO Do not string compare, use the real types here
                    var deviceType = deviceTypes.FirstOrDefault(x => x.Config.FullName.Contains(kvp.Key));
                    var valueDict = kvp.Value as Dictionary<string, object>;
                    if (deviceType == null || valueDict == null)
                        continue;

                    if (ParseDict(valueDict, deviceType.Config) is DeviceConfigBase subConfig)
                        devices.Add(subConfig);
                }
            }

            config.Devices = devices;

            return config;

            object ParseDict(Dictionary<string, object> dict, Type type)
            {
                var result = Activator.CreateInstance(type);
                var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                foreach (var property in properties)
                {
                    if (dict.TryGetValue(property.Name, out var value))
                        property.SetValue(result, value);
                }

                return result;
            }

            T? OptionalArgument<T>(string key, T? fallback = default)
            {
                var value = args.FirstOrDefault(x => x.StartsWith(key))?.Split('=').ElementAtOrDefault(1);
                if (value == null)
                    return fallback;

                return (T)Convert.ChangeType(value, typeof(T));
            }
        }

        public static void PrintHelp()
        {
            Console.WriteLine("Emulator [--config=<Config.json>] | [--<device>.<key>=<value>]*");
        }

        private static Dictionary<string, object> ParseJsonConfig(string path)
        {
            using var stream = File.OpenRead(path);
            var config = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(stream);
            if (config == null)
                throw new ArgumentException($"JSON Config '{path}' cannot be found");

            var result = new Dictionary<string, object>();
            ConvertDict(config, result);
            
            return result;

            static void ConvertDict(Dictionary<string, JsonElement> src, Dictionary<string, object> dst)
            {
                foreach (var kvp in src)
                {
                    var srcValue = kvp.Value;
                    object? dstValue;
                    switch (srcValue.ValueKind)
                    {
                        case JsonValueKind.Undefined:
                            dstValue = null;
                            break;
                        case JsonValueKind.Object:
                        {
                            var srcDict = srcValue.Deserialize<Dictionary<string, JsonElement>>();
                            var dstDict = new Dictionary<string, object>();
                            ConvertDict(srcDict, dstDict);
                            dstValue = dstDict;
                            break;
                        }
                        case JsonValueKind.Array:
                            dstValue = srcValue.Deserialize<IReadOnlyList<JsonElement>>();
                            break;
                        case JsonValueKind.String:
                            dstValue = srcValue.GetString();
                            break;
                        case JsonValueKind.Number:
                            dstValue = srcValue.GetInt32();
                            break;
                        case JsonValueKind.True:
                            dstValue = true;
                            break;
                        case JsonValueKind.False:
                            dstValue = false;
                            break;
                        case JsonValueKind.Null:
                            dstValue = null;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    dst.Add(kvp.Key, dstValue);
                }
            }
        }

        private static Dictionary<string, object> ParseCommandLineConfig(string[] args)
        {
            var config = new Dictionary<string, object>();
            foreach (var arg in args)
            {
                var kvp = arg.Split('=');
                if (kvp.Length != 2)
                    continue;

                string? ns = null;
                var key = kvp[0];
                var value = kvp[1];

                var keyParts = key.Split('.');
                if (keyParts.Length == 2)
                {
                    ns = keyParts[0];
                    key = keyParts[1];
                }

                var dict = config;
                if (ns != null)
                {
                    if (!dict.TryGetValue(ns, out var nsValue))
                    {
                        nsValue = new Dictionary<string, object>();
                        dict.Add(ns, nsValue);
                    }

                    dict = (Dictionary<string, object>)nsValue;
                }

                dict[key] = value;
            }

            return config;
        }
    }
}
