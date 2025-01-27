﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Artemis.Core;
using Artemis.Core.Modules;
using Artemis.Plugins.PhilipsHue.DataModels;
using Artemis.Plugins.PhilipsHue.Models;
using Artemis.Plugins.PhilipsHue.Services;
using Q42.HueApi;
using Q42.HueApi.Models;
using Q42.HueApi.Models.Groups;

namespace Artemis.Plugins.PhilipsHue
{
    public class HueModule : Module<HueDataModel>
    {
        private readonly IHueService _hueService;
        private readonly PluginSetting<int> _pollingRateSetting;
        private readonly PluginSetting<List<PhilipsHueBridge>> _storedBridgesSetting;
        private CancellationTokenSource _enableCancel;

        private TimedUpdateRegistration _groupsTimedUpdate;
        private TimedUpdateRegistration _hueTimedUpdate;

        public override List<IModuleActivationRequirement> ActivationRequirements => null;

        public HueModule(PluginSettings settings, IHueService hueService)
        {
            _hueService = hueService;
            _storedBridgesSetting = settings.GetSetting("Bridges", new List<PhilipsHueBridge>());
            _pollingRateSetting = settings.GetSetting("PollingRate", 2000);

            // Reset to default if the setting is below 100ms because the scale changed from seconds to milliseconds
            if (_pollingRateSetting.Value < 100)
                _pollingRateSetting.Value = 2000;
        }

        public override DataModelPropertyAttribute GetDataModelDescription()
        {
            return new()
            {
                Name = "Philips Hue",
                Description = "A data model containing all your Philips Hue bridges and their systems"
            };
        }

        public override void Enable()
        {
            _enableCancel = new CancellationTokenSource();
            Task.Run(EnablePluginAsync, _enableCancel.Token);

            _storedBridgesSetting.SettingSaved += StoredBridgesSettingOnSettingSaved;
            _pollingRateSetting.SettingSaved += PollingRateSettingOnSettingSaved;
        }

        public async Task EnablePluginAsync()
        {
            await _hueService.UpdateExistingBridges();
            await _hueService.ConnectToBridges();

            SetupTimedUpdate();
        }

        public override void Disable()
        {
            _enableCancel?.Cancel();
            _storedBridgesSetting.SettingSaved -= StoredBridgesSettingOnSettingSaved;
            _pollingRateSetting.SettingSaved -= PollingRateSettingOnSettingSaved;
        }

        public override void Update(double deltaTime)
        {
        }

        private void StoredBridgesSettingOnSettingSaved(object sender, EventArgs e)
        {
            DataModel.ClearDynamicChildren();
            _hueService.ConnectToBridges();
        }

        private void PollingRateSettingOnSettingSaved(object sender, EventArgs e)
        {
            SetupTimedUpdate();
        }

        private void SetupTimedUpdate()
        {
            _groupsTimedUpdate?.Stop();
            _groupsTimedUpdate = AddTimedUpdate(TimeSpan.FromMinutes(1), UpdateGroups);
            _hueTimedUpdate?.Stop();
            _hueTimedUpdate = AddTimedUpdate(TimeSpan.FromMilliseconds(_pollingRateSetting.Value), UpdateHue);
        }

        /// <summary>
        ///     Retrieves and updates the groups and zones of all connected Hue bridges
        /// </summary>
        private async Task UpdateGroups(double delta)
        {
            foreach (PhilipsHueBridge bridge in _hueService.Bridges.Where(b => b.Client != null))
            {
                // Add or update current groups
                List<Group> groups = (await bridge.Client.GetGroupsAsync()).Where(g => g.Type == GroupType.Room || g.Type == GroupType.Zone).ToList();
                DataModel.Rooms.Update(bridge, groups);
                DataModel.Zones.Update(bridge, groups);
            }
        }

        /// <summary>
        ///     Updates the contents of groups and zones of all connected Hue bridges
        /// </summary>
        private async Task UpdateHue(double delta)
        {
            if (!DataModel.Rooms.Groups.Any() && !DataModel.Zones.Groups.Any())
                await UpdateGroups(delta);

            foreach (PhilipsHueBridge bridge in _hueService.Bridges)
            {
                if (IsPropertyInUse(dm => dm.Rooms, true) || IsPropertyInUse(dm => dm.Zones, true))
                {
                    // Get flat lists of all lights and sensors
                    List<Light> lights = (await bridge.Client.GetLightsAsync()).ToList();
                    // Pass the lights to all rooms and zones, they'll pick what they own
                    DataModel.Rooms.UpdateContents(bridge, lights);
                    DataModel.Zones.UpdateContents(bridge, lights);
                }

                if (IsPropertyInUse(dm => dm.Accessories, true))
                {
                    // Get flat lists of all lights and sensors
                    List<Sensor> sensors = (await bridge.Client.GetSensorsAsync()).ToList();
                    // Let the accessories data model update all sensor data, it's not per room/zone
                    DataModel.Accessories.UpdateContents(bridge, sensors);
                }
            }
        }
    }
}