﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Artemis.Core;
using Artemis.Core.Modules;
using Artemis.Core.Services;
using Artemis.Plugins.Modules.General.DataModels;
using Artemis.Plugins.Modules.General.DataModels.Windows;
using Artemis.Plugins.Modules.General.Utilities;

namespace Artemis.Plugins.Modules.General
{
    [PluginFeature(Name = "General", Icon = "Images/bow.svg", AlwaysEnabled = true)]
    public class GeneralModule : Module<GeneralDataModel>
    {
        private readonly PluginSetting<bool> _enableActiveWindow;
        private readonly IColorQuantizerService _quantizerService;
        private readonly IProcessMonitorService _processMonitorService;

        public override List<IModuleActivationRequirement> ActivationRequirements => null;

        public GeneralModule(IColorQuantizerService quantizerService, PluginSettings settings, IProcessMonitorService processMonitorService)
        {
            _quantizerService = quantizerService;
            _processMonitorService = processMonitorService;
            _enableActiveWindow = settings.GetSetting("EnableActiveWindow", true);

            if (settings.GetSetting("AutoDefaultProfilesCreation", true).Value)
            {
                AddDefaultProfile(DefaultCategoryName.General, "Profiles/rainbow.json");
                AddDefaultProfile(DefaultCategoryName.General, "Profiles/noise.json");
            }
        }

        #region Overrides of Module

        /// <inheritdoc />
        public override DataModelPropertyAttribute GetDataModelDescription()
        {
            return new() {Name = "General", Description = "Contains general system information"};
        }

        #endregion

        public override void Enable()
        {
            _enableActiveWindow.SettingChanged += EnableActiveWindowOnSettingChanged;

            AddTimedUpdate(TimeSpan.FromMilliseconds(250), _ => UpdateCurrentWindow(), "UpdateCurrentWindow");
            AddTimedUpdate(TimeSpan.FromSeconds(1.5), _ => UpdatePerformance(), "UpdatePerformance");
            AddTimedUpdate(TimeSpan.FromSeconds(1), _ => UpdateRunningProcesses(), "UpdateRunningProcesses");
            ApplyEnableActiveWindow();
        }

        public override void Disable()
        {
            _enableActiveWindow.SettingChanged -= EnableActiveWindowOnSettingChanged;
        }

        public override void Update(double deltaTime)
        {
            DataModel.TimeDataModel.CurrentTime = DateTimeOffset.Now;
            DataModel.TimeDataModel.TimeSinceMidnight = DateTimeOffset.Now - DateTimeOffset.Now.Date;
        }

        #region Open windows

        public void UpdateCurrentWindow()
        {
            if (!_enableActiveWindow.Value)
                return;

            int processId = WindowUtilities.GetActiveProcessId();
            if (DataModel.ActiveWindow == null || DataModel.ActiveWindow.Process.Id != processId)
                DataModel.ActiveWindow = new WindowDataModel(Process.GetProcessById(processId), _quantizerService);

            DataModel.ActiveWindow?.UpdateWindowTitle();
        }

        #endregion

        private void UpdatePerformance()
        {
            // Performance counters are slow, only update them if necessary
            if (IsPropertyInUse("PerformanceDataModel.CpuUsage", false))
                DataModel.PerformanceDataModel.CpuUsage = Performance.GetCpuUsage();
            if (IsPropertyInUse("PerformanceDataModel.AvailableRam", false))
                DataModel.PerformanceDataModel.AvailableRam = Performance.GetPhysicalAvailableMemoryInMiB();
            if (IsPropertyInUse("PerformanceDataModel.TotalRam", false))
                DataModel.PerformanceDataModel.TotalRam = Performance.GetTotalMemoryInMiB();
        }

        private void UpdateRunningProcesses()
        {
            DataModel.RunningProcesses = _processMonitorService.GetRunningProcesses().Select(p => p.ProcessName).Except(Constants.IgnoredProcessList).ToList();
        }

        private void EnableActiveWindowOnSettingChanged(object sender, EventArgs e)
        {
            ApplyEnableActiveWindow();
        }

        private void ApplyEnableActiveWindow()
        {
            if (_enableActiveWindow.Value)
                ShowProperty(d => d.ActiveWindow);
            else
                HideProperty(d => d.ActiveWindow);
        }
    }
}