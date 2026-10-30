using System.Collections.Generic;
using System.Dynamic;
using Newtonsoft.Json.Linq;
using Tbot.Common.Settings;
using Tbot.Services;
using Xunit;

namespace TBot.Tests {
    public class SimulatorSettingsReaderTests {
        [Fact]
        public void Reads_nested_settings_from_expando_case_insensitively() {
            IDictionary<string, object> external = new ExpandoObject();
            external["url"] = "http://127.0.0.1:3000";
            external["AutoStart"] = "true";
            IDictionary<string, object> root = new ExpandoObject();
            root["calcsource"] = "shadow";
            root["ExternalCalc"] = external;

            Assert.Equal("shadow", SwitchingSimulatorClient.ResolveSettingValue(root, "CalcSource"));
            Assert.Equal("http://127.0.0.1:3000", SwitchingSimulatorClient.ResolveSettingValue(root, "ExternalCalc.Url"));
            Assert.Equal("true", SwitchingSimulatorClient.ResolveSettingValue(root, "externalcalc.autostart"));
            Assert.Null(SwitchingSimulatorClient.ResolveSettingValue(root, "ExternalCalc.Missing"));
        }

        [Fact]
        public void Reads_nested_settings_from_config_object() {
            var root = new ConfigObject();
            var external = new ConfigObject();
            ((IDictionary<string, object>)root)["CalcSource"] = "external";
            ((IDictionary<string, object>)root)["ExternalCalc"] = external;
            ((IDictionary<string, object>)external)["Url"] = "http://localhost:3000";
            ((IDictionary<string, object>)external)["AutoStart"] = true;

            Assert.Equal("external", SwitchingSimulatorClient.ResolveSettingValue(root, "CalcSource"));
            Assert.Equal("http://localhost:3000", SwitchingSimulatorClient.ResolveSettingValue(root, "ExternalCalc.Url"));
            Assert.Equal(true, SwitchingSimulatorClient.ResolveSettingValue(root, "ExternalCalc.AutoStart"));
        }

        [Fact]
        public void Reads_jobject_settings_and_missing_values() {
            var root = JObject.Parse("{ 'CalcSource': 'shadow', 'ExternalCalc': { 'Url': 'http://127.0.0.1:3000', 'AutoStart': false } }");
            Assert.Equal("shadow", SwitchingSimulatorClient.ResolveSettingValue(root, "CalcSource"));
            Assert.Equal("http://127.0.0.1:3000", SwitchingSimulatorClient.ResolveSettingValue(root, "ExternalCalc.Url"));
            Assert.Equal(false, SwitchingSimulatorClient.ResolveSettingValue(root, "ExternalCalc.AutoStart"));
            Assert.Null(SwitchingSimulatorClient.ResolveSettingValue(root, "ExternalCalc.Missing"));
        }
    }
}
