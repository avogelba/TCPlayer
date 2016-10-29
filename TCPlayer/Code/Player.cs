﻿/*
    TC Plyer
    Total Commander Audio Player plugin & standalone player written in C#, based on bass.dll components
    Copyright (C) 2016 Webmaster442 aka. Ruzsinszki Gábor

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */
using ManagedBass;
using ManagedBass.Cd;
using ManagedBass.Mix;
using ManagedBass.Midi;
using System;
using System.Collections.Generic;
using System.IO;
using ManagedBass.Fx;
using System.Runtime.InteropServices;

namespace TCPlayer.Code
{
    internal class Player : IDisposable
    {
        public bool Is64Bit
        {
            get { return IntPtr.Size == 8; }
        }

        private bool _initialized;
        private int _source, _mixer;
        private float _lastvol;
        private bool _paused;
        private bool _isstream;
        private PeakEQParameters _parameters;
        private GCHandle _gch;
        private int _eqhandle;
        private float[] _eqvalues;

        public Player()
        {
            var enginedir = AppDomain.CurrentDomain.BaseDirectory;
            if (Is64Bit) enginedir = Path.Combine(enginedir, @"Engine\x64");
            else enginedir = Path.Combine(enginedir, @"Engine\x86");
            Bass.Load(enginedir);
            BassMix.Load(enginedir);
            BassCd.Load(enginedir);
            BassFx.Load(enginedir);
            Bass.PluginLoad(enginedir + "\\bass_aac.dll");
            Bass.PluginLoad(enginedir + "\\bass_ac3.dll");
            Bass.PluginLoad(enginedir + "\\bass_ape.dll");
            Bass.PluginLoad(enginedir + "\\bass_mpc.dll");
            Bass.PluginLoad(enginedir + "\\bass_ofr.dll");
            Bass.PluginLoad(enginedir + "\\bass_spx.dll");
            Bass.PluginLoad(enginedir + "\\bass_tta.dll");
            Bass.PluginLoad(enginedir + "\\bassalac.dll");
            Bass.PluginLoad(enginedir + "\\bassdsd.dll");
            Bass.PluginLoad(enginedir + "\\bassflac.dll");
            Bass.PluginLoad(enginedir + "\\bassopus.dll");
            Bass.PluginLoad(enginedir + "\\basswma.dll");
            Bass.PluginLoad(enginedir + "\\basswv.dll");
            Bass.PluginLoad(enginedir + "\\bassmidi.dll");
            BassMidi.DefaultFont = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Engine\Ct8mgm.sf2");
            _eqvalues = new float[10];
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_initialized) Bass.Free();
            BassCd.Unload();
            BassMix.Unload();
            Bass.PluginFree(0);
            Bass.Unload();
            _gch.Free();
            GC.SuppressFinalize(this);
        }

        public bool IsStream
        {
            get { return _isstream; }
        }

        /// <summary>
        /// Free used resources
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Display Error message
        /// </summary>
        /// <param name="message"></param>
        private void Error(string message)
        {
            var error = Bass.LastError;
            string text = string.Format("{0}\r\nBass Error code: {1}\r\nError Description: {2}", message, (int)error, error);
            throw new Exception(text);
        }

        /// <summary>
        /// Gets the channel length
        /// </summary>
        public double Length
        {
            get
            {
                var len = Bass.ChannelGetLength(_source);
                return Bass.ChannelBytes2Seconds(_source, len);
            }
        }

        public double Position
        {
            get
            {
                var pos = Bass.ChannelGetPosition(_source);
                return Bass.ChannelBytes2Seconds(_source, pos);
            }
            set
            {
                var pos = Bass.ChannelSeconds2Bytes(_source, value);
                Bass.ChannelSetPosition(_source, pos);
            }
        }

        /// <summary>
        /// Gets or sets the Channel volume
        /// </summary>
        public float Volume
        {
            get
            {

                float temp = 0.0f;
                Bass.ChannelGetAttribute(_mixer, ChannelAttribute.Volume, out temp);
                return temp;
            }
            set
            {
                Bass.ChannelSetAttribute(_mixer, ChannelAttribute.Volume, value);
                _lastvol = value;
            }
        }

        /// <summary>
        /// Load a file for playback
        /// </summary>
        /// <param name="file">File to load</param>
        public void Load(string file)
        {
            _isstream = false;
            if (_eqhandle != 0)
            {
                Bass.ChannelRemoveFX(_mixer, _eqhandle);
                _eqhandle = 0;
            }
            if (_source != 0)
            {
                Bass.StreamFree(_source);
                _source = 0;
            }
            if (_mixer != 0)
            {
                Bass.StreamFree(_mixer);
                _mixer = 0;
            }
            var sourceflags = BassFlags.Decode | BassFlags.Loop | BassFlags.Float | BassFlags.Prescan;
            var mixerflags = BassFlags.MixerDownMix | BassFlags.MixerPositionEx | BassFlags.AutoFree;

            if (file.StartsWith("http://") || file.StartsWith("https://"))
            {
                _source = Bass.CreateStream(file, 0, sourceflags, null);
                _isstream = true;
            }
            else if (file.StartsWith("cd://"))
            {
                string[] info = file.Replace("cd://", "").Split('/');
                _source = BassCd.CreateStream(Convert.ToInt32(info[0]), Convert.ToInt32(info[1]), sourceflags);
            }
            else if (Helpers.IsTracker(file))
            {
                _source = Bass.MusicLoad(file, 0, 0, sourceflags);
            }
            else
            {
                _source = Bass.CreateStream(file, 0, 0, sourceflags);
            }
            if (_source == 0)
            {
                Error("Load failed");
                _isstream = false;
                return;
            }
            var ch = Bass.ChannelGetInfo(_source);
            _mixer = BassMix.CreateMixerStream(ch.Frequency, ch.Channels, mixerflags);
            if (_mixer == 0)
            {
                Error("Mixer stream create failed");
                return;
            }
            if (!BassMix.MixerAddChannel(_mixer, _source, BassFlags.MixerDownMix))
            {
                Error("Mixer chanel adding failed");
                return;
            }
            InitEQ();
            Bass.ChannelSetAttribute(_mixer, ChannelAttribute.Volume, _lastvol);
            _paused = false;
        }

        public void VolumeValues(out int left, out int right)
        {
            left = Bass.ChannelGetLevelLeft(_mixer);
            right = Bass.ChannelGetLevelRight(_mixer);
        }

        /// <summary>
        /// Player source handle
        /// </summary>
        public int SourceHandle
        {
            get { return _source; }
        }

        /// <summary>
        /// Returns mixer handle
        /// </summary>
        public int MixerHandle
        {
            get { return _mixer; }
        }

        /// <summary>
        /// Play
        /// </summary>
        public void Play()
        {
            _paused = false;
            Bass.ChannelPlay(_mixer, false);
        }

        /// <summary>
        /// Pause
        /// </summary>
        public void Pause()
        {
            _paused = true;
            Bass.ChannelPause(_mixer);
        }

        /// <summary>
        /// Play / Pause
        /// </summary>
        public void PlayPause()
        {
            if (_paused)
            {
                Bass.ChannelPlay(_mixer, false);
                _paused = false;
            }
            else
            {
                Bass.ChannelPause(_mixer);
                _paused = true;
            }
        }

        /// <summary>
        /// Stop
        /// </summary>
        public void Stop()
        {
            Bass.ChannelStop(_mixer);
            _paused = false;
        }

        /// <summary>
        /// IsPaused state
        /// </summary>
        public bool IsPaused
        {
            get
            {
                return _paused || (_mixer == 0);
            }
        }

        /// <summary>
        /// Gets the available output devices
        /// </summary>
        /// <returns>device names in an array</returns>
        public string[] GetDevices()
        {
            List<string> _devices = new List<string>(Bass.DeviceCount);
            for (int i = 1; i < Bass.DeviceCount; i++)
            {
                var device = Bass.GetDeviceInfo(i);
                if (device.IsEnabled) _devices.Add(device.Name);
            }
            return _devices.ToArray();
        }

        /// <summary>
        /// Currently used device index. Used for setting saving
        /// </summary>
        public int CurrentDeviceID
        {
            get;
            private set;
        }

        /// <summary>
        /// Change output device
        /// </summary>
        /// <param name="name">string device</param>
        public void ChangeDevice(string name = null)
        {
            if (name == null)
            {
                CurrentDeviceID = -1;
                if (Properties.Settings.Default.SaveDevice)
                    CurrentDeviceID = Properties.Settings.Default.DeviceID;

                _initialized = Bass.Init(CurrentDeviceID, 48000, DeviceInitFlags.Frequency, IntPtr.Zero);
                if (!_initialized)
                {
                    Error("Bass.dll init failed");
                    return;
                }
                Bass.Start();
            }
            for (int i = 0; i < Bass.DeviceCount; i++)
            {
                var device = Bass.GetDeviceInfo(i);
                if (device.Name == name)
                {
                    if (_initialized)
                    {
                        Bass.Free();
                        _initialized = false;
                    }

                    _initialized = Bass.Init(i, 48000, DeviceInitFlags.Frequency, IntPtr.Zero);
                    CurrentDeviceID = i;
                    if (!_initialized)
                    {
                        Error("Bass.dll init failed");
                        return;
                    }
                    Bass.Start();
                }
            }
        }

        private void InitEQ()
        {
            _eqhandle = Bass.ChannelSetFX(_mixer, EffectType.PeakEQ, 0);
            _parameters = new PeakEQParameters
            {
                lBand = -1,
                fBandwidth = 2.5f,
                fQ = 0
            };
            _gch = GCHandle.Alloc(_parameters, GCHandleType.Pinned);
            var center = 16.0f;
            for (int i = 0; i < 10; i++)
            {
                ++_parameters.lBand;
                _parameters.fCenter = center;
                _parameters.fGain = _eqvalues[i];
                Bass.FXSetParameters(_mixer, _gch.AddrOfPinnedObject());
                center *= 2;
                if (center == 128) center = 125;
            }
        }

        public void SetEQBand(int band, float value)
        {
            _eqvalues[band] = value;
            var cur = _parameters.lBand;
            _parameters.lBand = band;
            Bass.FXGetParameters(_eqhandle, _gch.AddrOfPinnedObject());
            _parameters.fGain = value;
            Bass.FXSetParameters(_eqhandle, _gch.AddrOfPinnedObject());
            _parameters.lBand = cur;
        }

        /// <summary>
        /// List tracks on a CD drive
        /// </summary>
        /// <param name="drive">CD drive path</param>
        /// <returns>An array of playlist entry's</returns>
        public static string[] GetCdInfo(string drive)
        {
            var list = new List<string>();

            int drivecount = BassCd.DriveCount;
            int driveindex = 0;
            for (int i = 0; i < drivecount; i++)
            {

                var info = BassCd.GetInfo(i);
                if (info.DriveLetter == drive[0])
                {
                    driveindex = i;
                    break;
                }
            }

            if (BassCd.IsReady(driveindex))
            {
                var numtracks = BassCd.GetTracks(driveindex);
                var discid = BassCd.GetID(0, CDID.CDDB); //cddb connect
                if (App._discid != discid)
                {
                    var datas = BassCd.GetIDText(driveindex);
                    App._discid = discid;
                    App._cddata.Clear();
                    foreach (var data in datas)
                    {
                        var item = data.Split('=');
                        App._cddata.Add(item[0], item[1]);
                    }
                }
                for (int i = 0; i < numtracks; i++)
                {
                    var entry = string.Format("cd://{0}/{1}", driveindex, i);
                    list.Add(entry);
                }
            }
            BassCd.Release(driveindex);
            return list.ToArray();
        }

    }
}
