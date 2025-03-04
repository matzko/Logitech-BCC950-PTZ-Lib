﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using DirectShowLib;

namespace PTZ
{
    public enum PTZType
    {
        Relative,
        Absolute
    }

    public class PTZDevice
    {
        static bool doLog = true;
        private readonly Guid PROPSETID_VIDCAP_CAMERACONTROL = new Guid(0xc6e13370, 0x30ac, 0x11d0, 0xa1, 0x8c, 0x00, 0xa0, 0xc9, 0x11, 0x89, 0x56);
        private DsDevice _device;
        private IAMCameraControl _camControl;
        private IKsPropertySet _ksPropertySet;

        public int ZoomMin { get; set; }
        public int ZoomMax { get; set; }
        public int ZoomStep { get; set; }
        public int ZoomDefault { get; set; }

        private PTZDevice(string name)
        {
            var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
            var device = devices.Where(d => d.Name == name).FirstOrDefault();

            _device = device;

            if (_device == null) throw new ApplicationException(String.Format("Couldn't find device named {0}!", name));

            IFilterGraph2 graphBuilder = new FilterGraph() as IFilterGraph2;
            IBaseFilter filter = null;
            IMoniker i = _device.Mon as IMoniker;

            graphBuilder.AddSourceFilterForMoniker(i, null, _device.Name, out filter);
            _camControl = filter as IAMCameraControl;
            _ksPropertySet = filter as IKsPropertySet;

            if (_camControl == null) throw new ApplicationException("Couldn't get ICamControl!");
            if (_ksPropertySet == null) throw new ApplicationException("Couldn't get IKsPropertySet!");

            //TODO: Add Absolute
            /*
            if (type == PTZType.Relative &&
                !(SupportFor(KSProperties.CameraControlFeature.KSPROPERTY_CAMERACONTROL_PAN_RELATIVE) &&
                SupportFor(KSProperties.CameraControlFeature.KSPROPERTY_CAMERACONTROL_TILT_RELATIVE)))
            {
                throw new NotSupportedException("This camera doesn't appear to support Relative Pan and Tilt");
            }
            /**/

            //TODO: Do I through NotSupported when methods are called or throw them now?

            //TODO: Do I check for Zoom or ignore if it's not there?
            InitZoomRanges();
        }

        private bool SupportFor(KSProperties.CameraControlFeature feature)
        {
            KSPropertySupport supported = new KSPropertySupport();
            _ksPropertySet.QuerySupported(PROPSETID_VIDCAP_CAMERACONTROL,(int)feature, out supported);

            return (supported.HasFlag(KSPropertySupport.Set) && supported.HasFlag(KSPropertySupport.Get));
        }

        public static DsDevice[] Devices()
        {
            return DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
        }

        public IDictionary<string, bool> DeviceSupportedProperties()
        {
            var dictionary = new Dictionary<string, bool>();
            dictionary.Add("PAN", SupportFor(KSProperties.CameraControlFeature.KSPROPERTY_CAMERACONTROL_PAN));
            dictionary.Add("TILT", SupportFor(KSProperties.CameraControlFeature.KSPROPERTY_CAMERACONTROL_TILT));
            dictionary.Add("ROLL", SupportFor(KSProperties.CameraControlFeature.KSPROPERTY_CAMERACONTROL_ROLL));
            dictionary.Add("ZOOM", SupportFor(KSProperties.CameraControlFeature.KSPROPERTY_CAMERACONTROL_ZOOM));
            dictionary.Add("EXPOSURE", SupportFor(KSProperties.CameraControlFeature.KSPROPERTY_CAMERACONTROL_EXPOSURE));
            dictionary.Add("IRIS", SupportFor(KSProperties.CameraControlFeature.KSPROPERTY_CAMERACONTROL_IRIS));
            dictionary.Add("FOCUS", SupportFor(KSProperties.CameraControlFeature.KSPROPERTY_CAMERACONTROL_FOCUS));
            dictionary.Add("SCANMODE", SupportFor(KSProperties.CameraControlFeature.KSPROPERTY_CAMERACONTROL_SCANMODE));
            dictionary.Add("PRIVACY", SupportFor(KSProperties.CameraControlFeature.KSPROPERTY_CAMERACONTROL_PRIVACY));
            dictionary.Add("PANTILT", SupportFor(KSProperties.CameraControlFeature.KSPROPERTY_CAMERACONTROL_PANTILT));
            dictionary.Add("PAN_RELATIVE", SupportFor(KSProperties.CameraControlFeature.KSPROPERTY_CAMERACONTROL_PAN_RELATIVE));
            dictionary.Add("TILT_RELATIVE", SupportFor(KSProperties.CameraControlFeature.KSPROPERTY_CAMERACONTROL_TILT_RELATIVE));
            dictionary.Add("ROLL_RELATIVE", SupportFor(KSProperties.CameraControlFeature.KSPROPERTY_CAMERACONTROL_ROLL_RELATIVE));
            dictionary.Add("ZOOM_RELATIVE", SupportFor(KSProperties.CameraControlFeature.KSPROPERTY_CAMERACONTROL_ZOOM_RELATIVE));
            dictionary.Add("EXPOSURE_RELATIVE", SupportFor(KSProperties.CameraControlFeature.KSPROPERTY_CAMERACONTROL_EXPOSURE_RELATIVE));
            dictionary.Add("IRIS_RELATIVE", SupportFor(KSProperties.CameraControlFeature.KSPROPERTY_CAMERACONTROL_IRIS_RELATIVE));
            dictionary.Add("FOCUS_RELATIVE", SupportFor(KSProperties.CameraControlFeature.KSPROPERTY_CAMERACONTROL_FOCUS_RELATIVE));
            dictionary.Add("PANTILT_RELATIVE", SupportFor(KSProperties.CameraControlFeature.KSPROPERTY_CAMERACONTROL_PANTILT_RELATIVE));
            dictionary.Add("FOCAL_LENGTH", SupportFor(KSProperties.CameraControlFeature.KSPROPERTY_CAMERACONTROL_FOCAL_LENGTH));
            dictionary.Add("AUTO_EXPOSURE_PRIORITY", SupportFor(KSProperties.CameraControlFeature.KSPROPERTY_CAMERACONTROL_AUTO_EXPOSURE_PRIORITY));
            return dictionary;
        }

        public void Move(int x, int y) //TODO: Is this the best public API? Should work for Relative AND Absolute, right?
        {
            //TODO: Make it work for Absolute also...using the PTZEnum

            //first, tilt
            if (y != 0)
            {
                MoveLongTime(KSProperties.CameraControlFeature.KSPROPERTY_CAMERACONTROL_TILT_RELATIVE, y);
            }

            if (x != 0)
            {
                MoveLongTime(KSProperties.CameraControlFeature.KSPROPERTY_CAMERACONTROL_PAN_RELATIVE, x);
            }
        }

        public static int MakeWord(sbyte low, sbyte high)
        {
            return ((int)high << 8) | low;
        }

        private void MoveDirection(
            KSProperties.CameraControlFeature cameraControlFeature,
            int durationMilliseconds,
            int value
            )
        {
            // Create and prepare data structures
            var control = new KSProperties.KSPROPERTY_CAMERACONTROL_S();

            IntPtr controlData = Marshal.AllocCoTaskMem(Marshal.SizeOf(control));
            IntPtr instData = Marshal.AllocCoTaskMem(Marshal.SizeOf(control.Instance));

            control.Instance.Value = value;

            //TODO: Fix for Absolute
            control.Instance.Flags = (int)CameraControlFlags.Relative;

            Marshal.StructureToPtr(control, controlData, true);
            Marshal.StructureToPtr(control.Instance, instData, true);

            _ksPropertySet.Set(
                PROPSETID_VIDCAP_CAMERACONTROL,
                (int) cameraControlFeature,
                instData,
                Marshal.SizeOf(control.Instance),
                controlData, Marshal.SizeOf(control)
            );

            // do stop after a while, to be safe
            Thread.Sleep(durationMilliseconds);

            control.Instance.Value = 0; //STOP!
            control.Instance.Flags = (int) CameraControlFlags.Relative;

            Marshal.StructureToPtr(control, controlData, true);
            Marshal.StructureToPtr(control.Instance, instData, true);

            _ksPropertySet.Set(
                PROPSETID_VIDCAP_CAMERACONTROL,
                (int) cameraControlFeature,
                instData,
                Marshal.SizeOf(control.Instance),
                controlData, Marshal.SizeOf(control)
            );

            if (controlData != IntPtr.Zero) { Marshal.FreeCoTaskMem(controlData); }
            if (instData != IntPtr.Zero) { Marshal.FreeCoTaskMem(instData); }
        }

        public void MoveUp(int durationMilliseconds)
        {
            MoveDirection(
                KSProperties.CameraControlFeature.KSPROPERTY_CAMERACONTROL_TILT_RELATIVE,
                durationMilliseconds,
                1
            );
        }

        public void MoveDown(int durationMilliseconds)
        {
            MoveDirection(
                KSProperties.CameraControlFeature.KSPROPERTY_CAMERACONTROL_TILT_RELATIVE,
                durationMilliseconds,
                -1
            );
        }

        public void MoveLeft(int durationMilliseconds)
        {
            MoveDirection(
                KSProperties.CameraControlFeature.KSPROPERTY_CAMERACONTROL_PAN_RELATIVE,
                durationMilliseconds,
                -1
            );
        }

        public void MoveRight(int durationMilliseconds)
        {
            MoveDirection(
                KSProperties.CameraControlFeature.KSPROPERTY_CAMERACONTROL_PAN_RELATIVE,
                durationMilliseconds,
                1
            );
        }

        public int ZoomIn(int durationMilliseconds)
        {
            MoveDirection(
                KSProperties.CameraControlFeature.KSPROPERTY_CAMERACONTROL_ZOOM,
                durationMilliseconds,
                10
            );
            int oldZoom = GetCurrentZoom();
            int newZoom = ZoomDefault;
            newZoom = oldZoom + 10; //10 is magic...could be anything?

            newZoom = Math.Max(ZoomMin, newZoom);
            newZoom = Math.Min(ZoomMax, newZoom);
            _camControl.Set(CameraControlProperty.Zoom, newZoom, CameraControlFlags.Manual);
            return newZoom;
        }

        public int ZoomOut(int durationMilliseconds)
        {
            int oldZoom = GetCurrentZoom();
            int newZoom = ZoomDefault;
            newZoom = oldZoom - 10;

            newZoom = Math.Max(ZoomMin, newZoom);
            newZoom = Math.Min(ZoomMax, newZoom);
            _camControl.Set(CameraControlProperty.Zoom, newZoom, CameraControlFlags.Manual);
            return newZoom;
        }

        private void MoveLongTime(KSProperties.CameraControlFeature axis, int value)
        {
            // Create and prepare data structures
            var control = new KSProperties.KSPROPERTY_CAMERACONTROL_S();

            IntPtr controlData = Marshal.AllocCoTaskMem(Marshal.SizeOf(control));
            IntPtr instData = Marshal.AllocCoTaskMem(Marshal.SizeOf(control.Instance));

            control.Instance.Value = value;

            //TODO: Fix for Absolute
            control.Instance.Flags = (int)CameraControlFlags.Relative;

            Marshal.StructureToPtr(control, controlData, true);
            Marshal.StructureToPtr(control.Instance, instData, true);
            var hr2 = _ksPropertySet.Set(PROPSETID_VIDCAP_CAMERACONTROL, (int)axis,
               instData, Marshal.SizeOf(control.Instance), controlData, Marshal.SizeOf(control));

            // do stop after a while, to be safe
            Thread.Sleep(2500);

            control.Instance.Value = 0; //STOP!
            control.Instance.Flags = (int)CameraControlFlags.Relative;

            Marshal.StructureToPtr(control, controlData, true);
            Marshal.StructureToPtr(control.Instance, instData, true);
            var hr3 = _ksPropertySet.Set(PROPSETID_VIDCAP_CAMERACONTROL, (int)axis,
               instData, Marshal.SizeOf(control.Instance), controlData, Marshal.SizeOf(control));

            if (controlData != IntPtr.Zero) { Marshal.FreeCoTaskMem(controlData); }
            if (instData != IntPtr.Zero) { Marshal.FreeCoTaskMem(instData); }
        }

        public void StopMove()
        {
            // Create and prepare data structures
            var control = new KSProperties.KSPROPERTY_CAMERACONTROL_S();

            IntPtr controlData = Marshal.AllocCoTaskMem(Marshal.SizeOf(control));
            IntPtr instData = Marshal.AllocCoTaskMem(Marshal.SizeOf(control.Instance));

            control.Instance.Flags = (int)CameraControlFlags.Relative;

            control.Instance.Value = 0; //STOP!
            control.Instance.Flags = (int)CameraControlFlags.Relative;

            Marshal.StructureToPtr(control, controlData, true);
            Marshal.StructureToPtr(control.Instance, instData, true);

            if (controlData != IntPtr.Zero) { Marshal.FreeCoTaskMem(controlData); }
            if (instData != IntPtr.Zero) { Marshal.FreeCoTaskMem(instData); }
        }

        private void MoveInternal(KSProperties.CameraControlFeature axis, int value)
        {
            // Create and prepare data structures
            var control = new KSProperties.KSPROPERTY_CAMERACONTROL_S();

            IntPtr controlData = Marshal.AllocCoTaskMem(Marshal.SizeOf(control));
            IntPtr instData = Marshal.AllocCoTaskMem(Marshal.SizeOf(control.Instance));

            control.Instance.Value = value;

            //TODO: Fix for Absolute
            control.Instance.Flags = (int)CameraControlFlags.Relative;

            Marshal.StructureToPtr(control, controlData, true);
            Marshal.StructureToPtr(control.Instance, instData, true);
            var hr2 = _ksPropertySet.Set(PROPSETID_VIDCAP_CAMERACONTROL, (int)axis,
               instData, Marshal.SizeOf(control.Instance), controlData, Marshal.SizeOf(control));

            //TODO: It's a DC motor, no better way?
            Thread.Sleep(300);

            control.Instance.Value = 0; //STOP!
            control.Instance.Flags = (int)CameraControlFlags.Relative;

            Marshal.StructureToPtr(control, controlData, true);
            Marshal.StructureToPtr(control.Instance, instData, true);
            var hr3 = _ksPropertySet.Set(PROPSETID_VIDCAP_CAMERACONTROL, (int)axis,
               instData, Marshal.SizeOf(control.Instance), controlData, Marshal.SizeOf(control));

            if (controlData != IntPtr.Zero) { Marshal.FreeCoTaskMem(controlData); }
            if (instData != IntPtr.Zero) { Marshal.FreeCoTaskMem(instData); }
        }

        private int GetCurrentZoom()
        {
            int oldZoom = 0;
            CameraControlFlags oldFlags = CameraControlFlags.Manual;
            var e = _camControl.Get(CameraControlProperty.Zoom, out oldZoom, out oldFlags);
            return oldZoom;
        }

        private void InitZoomRanges()
        {
            int iMin, iMax, iStep, iDefault;
            CameraControlFlags flag;
            _camControl.GetRange(CameraControlProperty.Zoom, out iMin, out iMax, out iStep, out iDefault, out flag);

            //Can't pass properties by refer, so some duplication...
            ZoomMin = iMin;
            ZoomMax = iMax;
            ZoomDefault = iDefault;
            ZoomStep = iStep;
        }

        public int Zoom(int direction)
        {
            int oldZoom = GetCurrentZoom();
            int newZoom = ZoomDefault;
            if (direction > 0)
                newZoom = oldZoom + 10; //10 is magic...could be anything?
            else if (direction < 0)
                newZoom = oldZoom - 10;

            newZoom = Math.Max(ZoomMin, newZoom);
            newZoom = Math.Min(ZoomMax, newZoom);
            _camControl.Set(CameraControlProperty.Zoom, newZoom, CameraControlFlags.Manual);
            return newZoom;
        }

        public static PTZDevice GetDevice(string name)
        {
            return new PTZDevice(name);
        }

        static void Log(String message)
        {
            if (doLog)
            {
                message = DateTime.Now.ToString("yyyy-MM-dd h:mm:ss tt") + ": " + message;
                // string filePath = System.AppDomain.CurrentDomain.BaseDirectory + "app.log";
                string filePath = "C:\\Windows\\Temp\\visuwell-fecc.log";
                using (StreamWriter streamWriter = new StreamWriter(filePath, append: true)) {
                    streamWriter.WriteLine(message);
                    streamWriter.Close();
                }
            }
        }
    }

    class KSProperties
    {
        public enum CameraControlFeature
        {
            KSPROPERTY_CAMERACONTROL_PAN,
            KSPROPERTY_CAMERACONTROL_TILT,
            KSPROPERTY_CAMERACONTROL_ROLL,
            KSPROPERTY_CAMERACONTROL_ZOOM,
            KSPROPERTY_CAMERACONTROL_EXPOSURE,
            KSPROPERTY_CAMERACONTROL_IRIS,
            KSPROPERTY_CAMERACONTROL_FOCUS,
            KSPROPERTY_CAMERACONTROL_SCANMODE,
            KSPROPERTY_CAMERACONTROL_PRIVACY,
            KSPROPERTY_CAMERACONTROL_PANTILT,
            KSPROPERTY_CAMERACONTROL_PAN_RELATIVE,
            KSPROPERTY_CAMERACONTROL_TILT_RELATIVE,
            KSPROPERTY_CAMERACONTROL_ROLL_RELATIVE,
            KSPROPERTY_CAMERACONTROL_ZOOM_RELATIVE,
            KSPROPERTY_CAMERACONTROL_EXPOSURE_RELATIVE,
            KSPROPERTY_CAMERACONTROL_IRIS_RELATIVE,
            KSPROPERTY_CAMERACONTROL_FOCUS_RELATIVE,
            KSPROPERTY_CAMERACONTROL_PANTILT_RELATIVE,
            KSPROPERTY_CAMERACONTROL_FOCAL_LENGTH,
            KSPROPERTY_CAMERACONTROL_AUTO_EXPOSURE_PRIORITY
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KSPROPERTY
        {
            // size Guid is long + 2 short + 8 byte = 4 longs
            Guid Set;
            [MarshalAs(UnmanagedType.U4)]
            int Id;
            [MarshalAs(UnmanagedType.U4)]
            int Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KSPROPERTY_CAMERACONTROL_S
        {
            /// <summary> Property Guid </summary>
            public KSPROPERTY Property;
            public KSPROPERTY_CAMERACONTROL Instance;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KSPROPERTY_CAMERACONTROL
        {
            [MarshalAs(UnmanagedType.I4)]
            public int Value;

            [MarshalAs(UnmanagedType.U4)]
            public int Flags;

            [MarshalAs(UnmanagedType.U4)]
            public int Capabilities;

            [MarshalAs(UnmanagedType.U4)]
            public int Dummy;
            // Dummy added to get a succesful return of the Get, Set function
        }
    }
}