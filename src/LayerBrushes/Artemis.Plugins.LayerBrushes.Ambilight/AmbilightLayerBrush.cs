﻿using System;
using System.Linq;
using Artemis.Core;
using Artemis.Core.LayerBrushes;
using Artemis.Plugins.LayerBrushes.Ambilight.PropertyGroups;
using Artemis.Plugins.LayerBrushes.Ambilight.UI;
using Artemis.UI.Shared.LayerBrushes;
using ScreenCapture;
using SkiaSharp;

namespace Artemis.Plugins.LayerBrushes.Ambilight
{
    public class AmbilightLayerBrush : LayerBrush<AmbilightPropertyGroup>
    {
        #region Properties & Fields

        private IScreenCaptureService _screenCaptureService => AmbilightBootstrapper.ScreenCaptureService;
        public bool PropertiesOpen { get; set; }

        private Display? _display;
        private CaptureZone _captureZone;
        private bool _creatingCaptureZone;

        #endregion

        #region Methods

        public override void Update(double deltaTime)
        {
            _captureZone?.RequestUpdate();
        }

        public override unsafe void Render(SKCanvas canvas, SKRect bounds, SKPaint paint)
        {
            if (_captureZone == null) return;

            AmbilightCaptureProperties properties = Properties.Capture;
            lock (_captureZone.Buffer)
            {
                ReadOnlySpan<byte> capture = _captureZone.Buffer;
                if (capture.IsEmpty) return;

                fixed (byte* ptr = capture)
                {
                    using SKImage image = SKImage.FromPixels(new SKImageInfo(_captureZone.Width, _captureZone.Height, SKColorType.Bgra8888, SKAlphaType.Opaque), new IntPtr(ptr),
                        _captureZone.BufferWidth * 4);

                    if (properties.BlackBarDetectionTop || properties.BlackBarDetectionBottom || properties.BlackBarDetectionLeft || properties.BlackBarDetectionRight)
                    {
                        canvas.DrawImage(image, new SKRect(properties.BlackBarDetectionLeft ? _captureZone.BlackBars.Left : 0,
                                properties.BlackBarDetectionTop ? _captureZone.BlackBars.Top : 0,
                                _captureZone.Width - (properties.BlackBarDetectionRight ? _captureZone.BlackBars.Right : 0),
                                _captureZone.Height - (properties.BlackBarDetectionBottom ? _captureZone.BlackBars.Bottom : 0)),
                            bounds, paint);
                    }
                    else
                        canvas.DrawImage(image, bounds, paint);
                }
            }
        }

        public override void EnableLayerBrush()
        {
            ConfigurationDialog = new LayerBrushConfigurationDialog<CapturePropertiesViewModel>(1280, 720);

            Properties.Capture.LayerPropertyOnCurrentValueSet += CaptureOnLayerPropertyOnCurrentValueSet;
            RecreateCaptureZone();
        }

        private void CaptureOnLayerPropertyOnCurrentValueSet(object sender, LayerPropertyEventArgs e) => RecreateCaptureZone();

        public void RecreateCaptureZone()
        {
            if (PropertiesOpen || _creatingCaptureZone)
                return;

            try
            {
                _creatingCaptureZone = true;
                RemoveCaptureZone();
                AmbilightCaptureProperties props = Properties.Capture;
                bool defaulting = props.GraphicsCardDeviceId == 0 || props.GraphicsCardVendorId == 0 || props.DisplayName.CurrentValue == null;

                GraphicsCard? graphicsCard = _screenCaptureService.GetGraphicsCards()
                    .Where(gg => defaulting || (gg.VendorId == props.GraphicsCardVendorId) && (gg.DeviceId == props.GraphicsCardDeviceId))
                    .Cast<GraphicsCard?>()
                    .FirstOrDefault();
                if (graphicsCard == null)
                    return;

                _display = _screenCaptureService.GetDisplays(graphicsCard.Value)
                    .Where(d => defaulting || d.DeviceName.Equals(props.DisplayName.CurrentValue, StringComparison.OrdinalIgnoreCase))
                    .Cast<Display?>()
                    .FirstOrDefault();

                if (_display != null)
                {
                    // Save the defaults we went with so the VM picks them up as a starting point as well
                    if (defaulting)
                        props.ApplyDisplay(_display.Value, true);

                    _captureZone = _screenCaptureService.GetScreenCapture(_display.Value).RegisterCaptureZone(
                        // Don't go beyond screen resolution
                        Math.Clamp(props.X, 0, _display.Value.Width),
                        Math.Clamp(props.Y, 0, _display.Value.Height),
                        Math.Clamp(props.Width, 0, _display.Value.Width - props.X),
                        Math.Clamp(props.Height, 0, _display.Value.Height - props.Y),
                        props.DownscaleLevel);

                    _captureZone.AutoUpdate = false; //TODO DarthAffe 09.04.2021: config?
                    _captureZone.BlackBars.Threshold = props.BlackBarDetectionThreshold;
                }
            }
            finally
            {
                _creatingCaptureZone = false;
            }
        }

        private void RemoveCaptureZone()
        {
            if ((_display != null) && (_captureZone != null))
                _screenCaptureService.GetScreenCapture(_display.Value).UnregisterCaptureZone(_captureZone);
            _captureZone = null;
            _display = null;
        }

        public override void DisableLayerBrush()
        {
            Properties.Capture.LayerPropertyOnCurrentValueSet -= CaptureOnLayerPropertyOnCurrentValueSet;
            RemoveCaptureZone();
        }

        #endregion
    }
}