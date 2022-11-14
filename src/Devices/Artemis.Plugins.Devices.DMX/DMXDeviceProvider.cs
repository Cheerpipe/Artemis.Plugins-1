﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Artemis.Core;
using Artemis.Core.DeviceProviders;
using Artemis.Core.Services;
using Artemis.Plugins.Devices.DMX.Settings;
using RGB.NET.Core;
using RGB.NET.Devices.DMX.E131;
using Serilog;
using RGBDeviceProvider = RGB.NET.Devices.DMX.DMXDeviceProvider;

namespace Artemis.Plugins.Devices.DMX
{
    // ReSharper disable once UnusedMember.Global
    [PluginFeature(Name = "DMX Device Provider")]
    public class DMXDeviceProvider : DeviceProvider
    {
        private readonly IRgbService _rgbService;
        private readonly PluginSettings _settings;

        public DMXDeviceProvider(IRgbService rgbService, PluginSettings settings) : base(RGBDeviceProvider.Instance)
        {
            _rgbService = rgbService;
            _settings = settings;
        }

        public override void Enable()
        {
            PluginSetting<List<DeviceDefinition>> definitions = _settings.GetSetting("DeviceDefinitions", new List<DeviceDefinition>());
            RGBDeviceProvider.Instance.DeviceDefinitions.Clear();
            foreach (DeviceDefinition deviceDefinition in definitions.Value)
            {
                E131DMXDeviceDefinition definition = new(deviceDefinition.Hostname)
                {
                    Port = deviceDefinition.Port,
                    Manufacturer = deviceDefinition.Manufacturer ?? "Artemis",
                    Model = deviceDefinition.Model ?? "DMX Device",
                    Universe = deviceDefinition.Universe
                };
                
                for (int i = 0; i < deviceDefinition.LedDefinitions.Count; i++)
                {
                    LedDefinition ledDefinition = deviceDefinition.LedDefinitions[i];
                    LedId ledId = LedId.LedStripe1 + i;
                    int rChannel = ledDefinition.R;
                    int gChannel = ledDefinition.G;
                    int bChannel = ledDefinition.B;
                    definition.AddLed(ledId, (rChannel, c => c.GetR()), (gChannel, c => c.GetG()), (bChannel, c => c.GetB()));
                }

                RGBDeviceProvider.Instance.AddDeviceDefinition(definition);
            }

            _rgbService.AddDeviceProvider(RgbDeviceProvider);
            _rgbService.Surface.RegisterUpdateTrigger(new TimerUpdateTrigger(new CustomUpdateData().FlushLeds().Render(false)) { UpdateFrequency = 1 });
        }

        public override void Disable()
        {
            if (_settings.GetSetting("TurnOffLedsOnShutdown", false).Value)
                TurnOffLeds();

            _rgbService.RemoveDeviceProvider(RgbDeviceProvider);
            RgbDeviceProvider.Dispose();
        }

        private void TurnOffLeds()
        {
            // Disable the LEDs on every device before we leave
            foreach (IRGBDevice rgbDevice in RgbDeviceProvider.Devices)
            {
                ListLedGroup _ = new(_rgbService.Surface, rgbDevice)
                {
                    Brush = new SolidColorBrush(new Color(0, 0, 0)),
                    ZIndex = 999
                };
            }

            // Don't wait for the next update, force one now and flush all LEDs for good measure
            _rgbService.Surface.Update(true);
            // Give the update queues time to process
            Thread.Sleep(200);

            _rgbService.RemoveDeviceProvider(RgbDeviceProvider);
        }
    }
}