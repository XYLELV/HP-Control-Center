// Quick switches: Wi-Fi / Bluetooth radios (Windows.Devices.Radios, the same switch as the Action Center),
// touchpad and camera (enable/disable the device with pnputil), microphone mute (Core Audio, in SystemInfo).
using System;
using System.Linq;
using System.Reflection;
using System.Management;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace HPControlPanel
{
    // ---------- Radios ----------
    // WinRT is reached through reflection so the build needs no Windows SDK metadata files.
    public static class Radios
    {
        static Type RadioT, StateT, AccessT;
        static MethodInfo AsTask;
        static bool? available;

        // Resolved lazily inside a try: if WinRT isn't reachable, the tiles just show "无"
        // instead of a type-initializer exception breaking every poll.
        static bool Init()
        {
            if (available.HasValue) return available.Value;
            try
            {
                RadioT = Type.GetType("Windows.Devices.Radios.Radio, Windows.Devices, ContentType=WindowsRuntime");
                StateT = Type.GetType("Windows.Devices.Radios.RadioState, Windows.Devices, ContentType=WindowsRuntime");
                AccessT = Type.GetType("Windows.Devices.Radios.RadioAccessStatus, Windows.Devices, ContentType=WindowsRuntime");
                var ext = Type.GetType("System.WindowsRuntimeSystemExtensions, System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
                AsTask = ext == null ? null : ext.GetMethods().FirstOrDefault(m => m.Name == "AsTask" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.Name == "IAsyncOperation`1");
                available = RadioT != null && StateT != null && AccessT != null && AsTask != null;
            }
            catch { available = false; }
            return available.Value;
        }

        public const int WiFi = 1, Bluetooth = 3;          // RadioKind
        const int On = 1, Off = 2;                          // RadioState

        static object Await(object op, Type result)
        {
            var task = (Task)AsTask.MakeGenericMethod(result).Invoke(null, new[] { op });
            // Reading Result of an unfinished task would block for good (and with it every later poll).
            if (!task.Wait(8000)) throw new TimeoutException("Windows.Devices.Radios did not answer");
            return task.GetType().GetProperty("Result").GetValue(task);
        }

        static object Find(int kind)
        {
            if (!Init()) return null;
            var list = (IEnumerable)Await(RadioT.GetMethod("GetRadiosAsync").Invoke(null, null), typeof(IReadOnlyList<>).MakeGenericType(RadioT));
            foreach (var r in list)
                if (Convert.ToInt32(RadioT.GetProperty("Kind").GetValue(r)) == kind) return r;
            return null;
        }

        // true = on, false = off, null = no such radio / unavailable.
        public static bool? Get(int kind)
        {
            try
            {
                var r = Find(kind);
                if (r == null) return null;
                int st = Convert.ToInt32(RadioT.GetProperty("State").GetValue(r));
                return st == On ? true : st == Off ? (bool?)false : null;
            }
            catch { return null; }
        }

        public static bool Set(int kind, bool on)
        {
            try
            {
                if (!Init()) return false;
                Await(RadioT.GetMethod("RequestAccessAsync").Invoke(null, null), AccessT);
                var r = Find(kind);
                if (r == null) return false;
                var op = RadioT.GetMethod("SetStateAsync").Invoke(r, new[] { Enum.ToObject(StateT, on ? On : Off) });
                return Convert.ToInt32(Await(op, AccessT)) == 1;   // RadioAccessStatus.Allowed
            }
            catch (Exception e) { App.Log(e); return false; }
        }
    }

    // ---------- Devices (touchpad, camera) ----------
    public static class Devices
    {
        public static string TouchpadId, CameraId;
        static volatile bool restartNeeded;

        // True once after a switch that Windows applies only at the next restart (pnputil exit code 3010).
        public static bool TakeRestartNeeded() { bool r = restartNeeded; restartNeeded = false; return r; }

        // How likely a Camera / Image class device is the normal webcam: -1 = never (Windows Hello IR sensor,
        // depth camera, scanner), higher = better. A "Camera" class device with a camera-like name wins.
        static int WebcamScore(string cls, string name)
        {
            if (Regex.IsMatch(name, @"\bIR\b|infrared|hello|depth|红外|紅外", RegexOptions.IgnoreCase)) return -1;
            bool camName = Regex.IsMatch(name, @"cam|video|摄像|攝影|相机|相機", RegexOptions.IgnoreCase);
            if (cls == "Image" && !camName) return -1;   // the Image class also holds scanners
            return (cls == "Camera" ? 2 : 0) + (camName ? 1 : 0);
        }

        // Looked up by class and name, so it works for ELAN / Synaptics touchpads and any webcam.
        public static void Detect()
        {
            try
            {
                int bestCamera = -1;
                using (var q = new ManagementObjectSearcher("SELECT DeviceID, Name, PNPClass FROM Win32_PnPEntity WHERE PNPClass='HIDClass' OR PNPClass='Mouse' OR PNPClass='Camera' OR PNPClass='Image'"))
                    foreach (ManagementObject mo in q.Get())
                    {
                        string id = Convert.ToString(mo["DeviceID"]), name = Convert.ToString(mo["Name"]) ?? "", cls = Convert.ToString(mo["PNPClass"]);
                        if (cls == "Camera" || cls == "Image")
                        {
                            int score = WebcamScore(cls, name);
                            if (score > bestCamera) { bestCamera = score; CameraId = id; }
                        }
                        if ((cls == "HIDClass" || cls == "Mouse") && Regex.IsMatch(name, @"precision ?touch ?pad|touch ?pad|触摸板|触控板", RegexOptions.IgnoreCase))
                        {
                            // Prefer the precision-touchpad collection over plain filter/HID entries.
                            if (TouchpadId == null || Regex.IsMatch(name, "precision", RegexOptions.IgnoreCase)) TouchpadId = id;
                        }
                    }
            }
            catch (Exception e) { App.Log(e); }
        }

        // true = enabled, false = disabled (Device Manager error 22), null = not found.
        public static bool? Enabled(string id)
        {
            if (id == null) return null;
            try
            {
                using (var q = new ManagementObjectSearcher("SELECT ConfigManagerErrorCode FROM Win32_PnPEntity WHERE DeviceID='" + id.Replace("\\", "\\\\").Replace("'", "\\'") + "'"))
                    foreach (ManagementObject mo in q.Get()) return Convert.ToInt32(mo["ConfigManagerErrorCode"]) != 22;
            }
            catch { }
            return null;
        }

        public static bool SetEnabled(string id, bool on)
        {
            if (id == null) return false;
            try
            {
                var psi = new ProcessStartInfo(SystemInfo.Sys32("pnputil.exe"), (on ? "/enable-device \"" : "/disable-device \"") + id + "\"") { CreateNoWindow = true, UseShellExecute = false };
                using (var p = Process.Start(psi))
                {
                    if (!p.WaitForExit(15000)) return false;
                    if (p.ExitCode == 3010) { restartNeeded = true; return true; }   // ERROR_SUCCESS_REBOOT_REQUIRED
                    return p.ExitCode == 0;
                }
            }
            catch (Exception e) { App.Log(e); return false; }
        }
    }
}
