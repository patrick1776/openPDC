﻿//******************************************************************************************************
//  PhasorMeasurementMapper.cs - Gbtc
//
//  Copyright © 2010, Grid Protection Alliance.  All Rights Reserved.
//
//  Licensed to the Grid Protection Alliance (GPA) under one or more contributor license agreements. See
//  the NOTICE file distributed with this work for additional information regarding copyright ownership.
//  The GPA licenses this file to you under the Eclipse Public License -v 1.0 (the "License"); you may
//  not use this file except in compliance with the License. You may obtain a copy of the License at:
//
//      http://www.opensource.org/licenses/eclipse-1.0.php
//
//  Unless agreed to in writing, the subject software distributed under the License is distributed on an
//  "AS-IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. Refer to the
//  License for the specific language governing permissions and limitations.
//
//  Code Modification History:
//  ----------------------------------------------------------------------------------------------------
//  05/18/2009 - J. Ritchie Carroll
//       Generated original version of source code.
//  03/21/2010 - J. Ritchie Carroll
//       Added new connection string settings to accomodate new MultiProtocolFrameParser properties.
//
//******************************************************************************************************

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading;
using TimeSeriesFramework;
using TimeSeriesFramework.Adapters;
using TVA.Communication;
using TVA.IO;
using TVA.PhasorProtocols.Anonymous;
using TVA.Units;

namespace TVA.PhasorProtocols
{
    /// <summary>
    /// Represents an <see cref="IInputAdapter"/> used to map measured values from a connection
    /// to a phasor measurement device to new <see cref="IMeasurement"/> values.
    /// </summary>
    public class PhasorMeasurementMapper : InputAdapterBase
    {
        #region [ Members ]

        // Fields
        private MultiProtocolFrameParser m_frameParser;
        private Dictionary<string, IMeasurement> m_definedMeasurements;
        private Dictionary<ushort, ConfigurationCell> m_definedDevices;
        private Dictionary<string, ConfigurationCell> m_labelDefinedDevices;
        private Dictionary<string, long> m_undefinedDevices;
        private Dictionary<SignalKind, string[]> m_generatedSignalReferenceCache;
        private System.Timers.Timer m_dataStreamMonitor;
        private bool m_allowUseOfCachedConfiguration;
        private bool m_cachedConfigLoadAttempted;
        private TimeZoneInfo m_timezone;
        private Ticks m_timeAdjustmentTicks;
        private Ticks m_lastReportTime;
        private long m_outOfOrderFrames;
        private long m_totalLatency;
        private long m_minimumLatency;
        private long m_maximumLatency;
        private long m_latencyMeasurements;
        private long m_connectionAttempts;
        private long m_configurationChanges;
        private long m_totalDataFrames;
        private long m_totalConfigurationFrames;
        private long m_totalHeaderFrames;
        private string m_sharedMapping;
        private uint m_sharedMappingID;
        private ushort m_accessID;
        private bool m_isConcentrator;
        private bool m_receivedConfigFrame;
        private long m_bytesReceived;
        private int m_hashCode;
        private bool m_disposed;

        #endregion

        #region [ Constructors ]

        /// <summary>
        /// Creates a new <see cref="PhasorMeasurementMapper"/>.
        /// </summary>
        public PhasorMeasurementMapper()
        {
            // Create a cached signal reference dictionary for generated signal references
            m_generatedSignalReferenceCache = new Dictionary<SignalKind, string[]>();

            // Create data stream monitoring timer
            m_dataStreamMonitor = new System.Timers.Timer();
            m_dataStreamMonitor.Elapsed += m_dataStreamMonitor_Elapsed;
            m_dataStreamMonitor.AutoReset = true;
            m_dataStreamMonitor.Enabled = false;

            m_undefinedDevices = new Dictionary<string, long>();
        }

        #endregion

        #region [ Properties ]

        /// <summary>
        /// Gets flag that determines if device being mapped is a concentrator (i.e., data from multiple
        /// devices combined together from the connected device).
        /// </summary>
        public bool IsConcentrator
        {
            get
            {
                return m_isConcentrator;
            }
        }

        /// <summary>
        /// Gets or sets access ID (or ID code) for this device connection which is often necessary in order to make a connection to some phasor protocols.
        /// </summary>
        public ushort AccessID
        {
            get
            {
                return m_accessID;
            }
            set
            {
                m_accessID = value;
            }
        }

        /// <summary>
        /// Gets an enumeration of all defined system devices (regardless of ID or label based definition)
        /// </summary>
        public IEnumerable<ConfigurationCell> DefinedDevices
        {
            get
            {
                if (m_labelDefinedDevices != null)
                    return m_definedDevices.Values.Concat(m_labelDefinedDevices.Values);

                return m_definedDevices.Values;
            }
        }

        /// <summary>
        /// Gets or sets flag that determines if use of cached configuration during initial connection is allowed when a configuration has not been received within the data loss interval.
        /// </summary>
        public bool AllowUseOfCachedConfiguration
        {
            get
            {
                return m_allowUseOfCachedConfiguration;
            }
            set
            {
                m_allowUseOfCachedConfiguration = value;
            }
        }

        /// <summary>
        /// Gets the configuration cache file name, with path.
        /// </summary>
        public string ConfigurationCacheFileName
        {
            get
            {
                return ConfigurationFrame.GetConfigurationCacheFileName(Name);
            }
        }

        /// <summary>
        /// Gets or sets time zone of this <see cref="PhasorMeasurementMapper"/>.
        /// </summary>
        /// <remarks>
        /// If time zone of clock of connected device is not set to UTC, assigning this property
        /// with proper time zone will allow proper adjustment.
        /// </remarks>
        public TimeZoneInfo TimeZone
        {
            get
            {
                return m_timezone;
            }
            set
            {
                m_timezone = value;
            }
        }

        /// <summary>
        /// Gets or sets ticks used to manually adjust time of this <see cref="PhasorMeasurementMapper"/>.
        /// </summary>
        /// <remarks>
        /// This property will allow for precise time adjustments of connected devices should
        /// this be needed.
        /// </remarks>
        public Ticks TimeAdjustmentTicks
        {
            get
            {
                return m_timeAdjustmentTicks;
            }
            set
            {
                m_timeAdjustmentTicks = value;
            }
        }

        /// <summary>
        /// Gets the the total number of frames that have been received by the current mapper connection.
        /// </summary>
        public long TotalFrames
        {
            get
            {
                if (m_frameParser != null)
                    return m_frameParser.TotalFramesReceived;

                return 0;
            }
        }

        /// <summary>
        /// Gets or set last report time for current mapper connection.
        /// </summary>
        public Ticks LastReportTime
        {
            get
            {
                return m_lastReportTime;
            }
            set
            {
                m_lastReportTime = value;
            }
        }

        /// <summary>
        /// Gets the total number of frames that have been missed by the current mapper connection.
        /// </summary>
        public long MissingFrames
        {
            get
            {
                if (m_frameParser != null)
                    return m_frameParser.TotalMissingFrames;

                return 0;
            }
        }

        /// <summary>
        /// Gets the total number of CRC errors that have been encountered by the the current mapper connection.
        /// </summary>
        public long CRCErrors
        {
            get
            {
                if (m_frameParser != null)
                    return m_frameParser.TotalCrcExceptions;

                return 0;
            }
        }

        /// <summary>
        /// Gets the total number frames that came in out of order from the current mapper connection.
        /// </summary>
        public long OutOfOrderFrames
        {
            get
            {
                return m_outOfOrderFrames;
            }
        }

        /// <summary>
        /// Gets the minimum latency in milliseconds over the last test interval.
        /// </summary>
        public int MinimumLatency
        {
            get
            {
                return (int)Ticks.ToMilliseconds(m_minimumLatency);
            }
        }

        /// <summary>
        /// Gets the maximum latency in milliseconds over the last test interval.
        /// </summary>
        public int MaximumLatency
        {
            get
            {
                return (int)Ticks.ToMilliseconds(m_maximumLatency);
            }
        }

        /// <summary>
        /// Gets the average latency in milliseconds over the last test interval.
        /// </summary>
        public int AverageLatency
        {
            get
            {
                if (m_latencyMeasurements == 0)
                    return -1;

                return (int)Ticks.ToMilliseconds(m_totalLatency / m_latencyMeasurements);
            }
        }

        /// <summary>
        /// Gets the total number of connection attempts.
        /// </summary>
        public long ConnectionAttempts
        {
            get
            {
                return m_connectionAttempts;
            }
        }

        /// <summary>
        /// Gets the total number of received configurations.
        /// </summary>
        public long ConfigurationChanges
        {
            get
            {
                return m_configurationChanges;
            }
        }

        /// <summary>
        /// Gets the total number of received data frames.
        /// </summary>
        public long TotalDataFrames
        {
            get
            {
                return m_totalDataFrames;
            }
        }

        /// <summary>
        /// Gets the total number of received configuration frames.
        /// </summary>
        public long TotalConfigurationFrames
        {
            get
            {
                return m_totalConfigurationFrames;
            }
        }

        /// <summary>
        /// Gets the total number of received header frames.
        /// </summary>
        public long TotalHeaderFrames
        {
            get
            {
                return m_totalHeaderFrames;
            }
        }

        /// <summary>
        /// Gets the defined frame rate.
        /// </summary>
        public int DefinedFrameRate
        {
            get
            {
                if (m_frameParser != null)
                    return m_frameParser.ConfiguredFrameRate;

                return 0;
            }
        }

        /// <summary>
        /// Gets the actual frame rate.
        /// </summary>
        public double ActualFrameRate
        {
            get
            {
                if (m_frameParser != null)
                    return m_frameParser.CalculatedFrameRate;

                return 0.0D;
            }
        }

        /// <summary>
        /// Gets the actual data rate.
        /// </summary>
        public double ActualDataRate
        {
            get
            {
                if (m_frameParser != null)
                    return m_frameParser.ByteRate;

                return 0.0D;
            }
        }

        /// <summary>
        /// Gets or sets acronym of other device for which to assume a shared mapping.
        /// </summary>
        /// <remarks>
        /// Assigning acronym to this property automatically looks up ID of associated device.
        /// </remarks>
        public string SharedMapping
        {
            get
            {
                return m_sharedMapping;
            }
            set
            {
                m_sharedMapping = value;
                m_sharedMappingID = 0;

                if (!string.IsNullOrWhiteSpace(m_sharedMapping))
                {
                    try
                    {
                        DataRow[] filteredRows = DataSource.Tables["InputAdapters"].Select(string.Format("AdapterName = '{0}'", m_sharedMapping));

                        if (filteredRows.Length > 0)
                        {
                            m_sharedMappingID = uint.Parse(filteredRows[0]["ID"].ToString());
                        }
                        else
                        {
                            OnProcessException(new InvalidOperationException(string.Format("Failed to find input adapter ID for shared mapping \"{0}\", mapping was not assigned.", m_sharedMapping)));
                            m_sharedMapping = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        OnProcessException(new InvalidOperationException(string.Format("Failed to find input adapter ID for shared mapping \"{0}\" due to exception: {1} Mapping was not assigned.", m_sharedMapping, ex.Message), ex));
                        m_sharedMapping = null;
                    }
                }
            }
        }

        /// <summary>
        /// Returns ID of associated device with shared mapping or <see cref="AdapterBase.ID"/> of this <see cref="PhasorMeasurementMapper"/> if no shared mapping is defined.
        /// </summary>
        public uint SharedMappingID
        {
            get
            {
                if (m_sharedMappingID == 0)
                    return ID;

                return m_sharedMappingID;
            }
        }

        /// <summary>
        /// Gets flag that determines if the data input connects asynchronously.
        /// </summary>
        protected override bool UseAsyncConnect
        {
            get
            {
                // We use asynchronous connection on devices
                return true;
            }
        }

        /// <summary>
        /// Gets the flag indicating if this adapter supports temporal processing.
        /// </summary>
        /// <remarks>
        /// Since the phasor measurement mapper is designed to open sockets and connect to data streams,
        /// it is expected that this would not be desired in a temporal data streaming session.
        /// </remarks>
        public override bool SupportsTemporalProcessing
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Returns the detailed status of the data input source.
        /// </summary>
        public override string Status
        {
            get
            {
                StringBuilder status = new StringBuilder();

                status.Append(base.Status);
                status.AppendFormat("    Source is concentrator: {0}", m_isConcentrator);
                status.AppendLine();
                if (!string.IsNullOrWhiteSpace(SharedMapping))
                {
                    status.AppendFormat("     Shared mapping source: {0}", SharedMapping);
                    status.AppendLine();
                }
                status.AppendFormat("   Source device time zone: {0}", m_timezone.Id);
                status.AppendLine();
                status.AppendFormat("    Manual time adjustment: {0} seconds", m_timeAdjustmentTicks.ToSeconds().ToString("0.000"));
                status.AppendLine();
                status.AppendFormat("Allow use of cached config: {0}", m_allowUseOfCachedConfiguration);
                status.AppendLine();
                status.AppendFormat("No data reconnect interval: {0} seconds", Ticks.FromMilliseconds(m_dataStreamMonitor.Interval).ToSeconds().ToString("0.000"));
                status.AppendLine();

                if (m_allowUseOfCachedConfiguration)
                {
                    //                   123456789012345678901234567890
                    status.AppendFormat("   Cached config file name: {0}", FilePath.TrimFileName(ConfigurationCacheFileName, 51));
                    status.AppendLine();
                }

                status.AppendFormat("       Out of order frames: {0}", m_outOfOrderFrames);
                status.AppendLine();
                status.AppendFormat("           Minimum latency: {0}ms over {1} tests", MinimumLatency, m_latencyMeasurements);
                status.AppendLine();
                status.AppendFormat("           Maximum latency: {0}ms over {1} tests", MaximumLatency, m_latencyMeasurements);
                status.AppendLine();
                status.AppendFormat("           Average latency: {0}ms over {1} tests", AverageLatency, m_latencyMeasurements);
                status.AppendLine();

                if (m_frameParser != null)
                    status.Append(m_frameParser.Status);

                status.AppendLine();
                status.Append("Parsed Frame Quality Statistics".CenterText(78));
                status.AppendLine();
                status.AppendLine();
                //                      1         2         3         4         5         6         7
                //             123456789012345678901234567890123456789012345678901234567890123456789012345678
                status.Append("Device                  Bad Data   Bad Time    Frame      Total    Last Report");
                status.AppendLine();
                status.Append(" Name                    Frames     Frames     Errors     Frames      Time");
                status.AppendLine();
                //                      1         2            1          1          1          1          1
                //             1234567890123456789012 1234567890 1234567890 1234567890 1234567890 123456789012
                status.Append("---------------------- ---------- ---------- ---------- ---------- ------------");
                status.AppendLine();

                IConfigurationCell parsedDevice;
                string stationName;

                foreach (ConfigurationCell definedDevice in DefinedDevices)
                {
                    stationName = null;

                    // Attempt to lookup station name in configuration frame of connected device
                    if (m_frameParser != null && m_frameParser.ConfigurationFrame != null)
                    {
                        // Attempt to lookup by label (if defined), then by ID code
                        if ((m_labelDefinedDevices != null && definedDevice.StationName != null &&
                            m_frameParser.ConfigurationFrame.Cells.TryGetByStationName(definedDevice.StationName, out parsedDevice)) ||
                            m_frameParser.ConfigurationFrame.Cells.TryGetByIDCode(definedDevice.IDCode, out parsedDevice))
                            stationName = parsedDevice.StationName;
                    }

                    // We will default to defined name if parsed name is unavailable
                    if (string.IsNullOrWhiteSpace(stationName))
                        stationName = "[" + definedDevice.StationName.NotEmpty(definedDevice.IDLabel.NotEmpty("UNDEF") + ":" + definedDevice.IDCode) + "]";

                    status.Append(stationName.TruncateRight(22).PadRight(22));
                    status.Append(' ');
                    status.Append(definedDevice.DataQualityErrors.ToString().CenterText(10));
                    status.Append(' ');
                    status.Append(definedDevice.TimeQualityErrors.ToString().CenterText(10));
                    status.Append(' ');
                    status.Append(definedDevice.DeviceErrors.ToString().CenterText(10));
                    status.Append(' ');
                    status.Append(definedDevice.TotalFrames.ToString().CenterText(10));
                    status.Append(' ');
                    status.Append(((DateTime)definedDevice.LastReportTime).ToString("HH:mm:ss.fff"));
                    status.AppendLine();
                }

                status.AppendLine();
                status.AppendFormat("Undefined devices encountered: {0}", m_undefinedDevices.Count);
                status.AppendLine();

                lock (m_undefinedDevices)
                {
                    foreach (KeyValuePair<string, long> item in m_undefinedDevices)
                    {
                        status.Append("    Device \"");
                        status.Append(item.Key);
                        status.Append("\" encountered ");
                        status.Append(item.Value);
                        status.Append(" times");
                        status.AppendLine();
                    }
                }

                return status.ToString();
            }
        }

        /// <summary>
        /// Gets or sets reference to <see cref="MultiProtocolFrameParser"/>, attaching and/or detaching to events as needed.
        /// </summary>
        protected MultiProtocolFrameParser FrameParser
        {
            get
            {
                return m_frameParser;
            }
            set
            {
                if (m_frameParser != null)
                {
                    // Detach from events on existing frame parser reference
                    m_frameParser.ConfigurationChanged -= m_frameParser_ConfigurationChanged;
                    m_frameParser.ConnectionAttempt -= m_frameParser_ConnectionAttempt;
                    m_frameParser.ConnectionEstablished -= m_frameParser_ConnectionEstablished;
                    m_frameParser.ConnectionException -= m_frameParser_ConnectionException;
                    m_frameParser.ConnectionTerminated -= m_frameParser_ConnectionTerminated;
                    m_frameParser.ExceededParsingExceptionThreshold -= m_frameParser_ExceededParsingExceptionThreshold;
                    m_frameParser.ParsingException -= m_frameParser_ParsingException;
                    m_frameParser.ReceivedConfigurationFrame -= m_frameParser_ReceivedConfigurationFrame;
                    m_frameParser.ReceivedDataFrame -= m_frameParser_ReceivedDataFrame;
                    m_frameParser.ReceivedHeaderFrame -= m_frameParser_ReceivedHeaderFrame;
                    m_frameParser.ReceivedFrameBufferImage -= m_frameParser_ReceivedFrameBufferImage;
                    m_frameParser.Dispose();
                }

                // Assign new frame parser reference
                m_frameParser = value;

                if (m_frameParser != null)
                {
                    // Attach to events on new frame parser reference
                    m_frameParser.ConfigurationChanged += m_frameParser_ConfigurationChanged;
                    m_frameParser.ConnectionAttempt += m_frameParser_ConnectionAttempt;
                    m_frameParser.ConnectionEstablished += m_frameParser_ConnectionEstablished;
                    m_frameParser.ConnectionException += m_frameParser_ConnectionException;
                    m_frameParser.ConnectionTerminated += m_frameParser_ConnectionTerminated;
                    m_frameParser.ExceededParsingExceptionThreshold += m_frameParser_ExceededParsingExceptionThreshold;
                    m_frameParser.ParsingException += m_frameParser_ParsingException;
                    m_frameParser.ReceivedConfigurationFrame += m_frameParser_ReceivedConfigurationFrame;
                    m_frameParser.ReceivedDataFrame += m_frameParser_ReceivedDataFrame;
                    m_frameParser.ReceivedHeaderFrame += m_frameParser_ReceivedHeaderFrame;
                    m_frameParser.ReceivedFrameBufferImage += m_frameParser_ReceivedFrameBufferImage;
                }
            }
        }

        #endregion

        #region [ Methods ]

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="PhasorMeasurementMapper"/> object and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            if (!m_disposed)
            {
                try
                {
                    if (disposing)
                    {
                        // Detach from frame parser events and set reference to null
                        this.FrameParser = null;

                        if (m_dataStreamMonitor != null)
                        {
                            m_dataStreamMonitor.Elapsed -= m_dataStreamMonitor_Elapsed;
                            m_dataStreamMonitor.Dispose();
                        }
                        m_dataStreamMonitor = null;
                    }
                }
                finally
                {
                    m_disposed = true;          // Prevent duplicate dispose.
                    base.Dispose(disposing);    // Call base class Dispose().
                }
            }
        }

        /// <summary>
        /// Intializes <see cref="PhasorMeasurementMapper"/>.
        /// </summary>
        public override void Initialize()
        {
            base.Initialize();

            Dictionary<string, string> settings = Settings;
            string setting;

            // Load optional mapper specific connection parameters
            if (settings.TryGetValue("isConcentrator", out setting))
                m_isConcentrator = setting.ParseBoolean();
            else
                m_isConcentrator = false;

            if (settings.TryGetValue("accessID", out setting))
                m_accessID = ushort.Parse(setting);
            else
                m_accessID = 1;

            if (settings.TryGetValue("sharedMapping", out setting))
                SharedMapping = setting.Trim();
            else
                SharedMapping = null;

            if (settings.TryGetValue("timeZone", out setting) && !string.IsNullOrWhiteSpace(setting) && string.Compare(setting.Trim(), "UTC", true) != 0 && string.Compare(setting.Trim(), "Coordinated Universal Time", true) != 0)
            {
                try
                {
                    m_timezone = TimeZoneInfo.FindSystemTimeZoneById(setting);
                }
                catch (Exception ex)
                {
                    OnProcessException(new InvalidOperationException(string.Format("Defaulting to UTC. Failed to find system time zone for ID \"{0}\": {1}", setting, ex.Message), ex));
                    m_timezone = TimeZoneInfo.Utc;
                }
            }
            else
                m_timezone = TimeZoneInfo.Utc;

            if (settings.TryGetValue("timeAdjustmentTicks", out setting))
                m_timeAdjustmentTicks = long.Parse(setting);
            else
                m_timeAdjustmentTicks = 0;

            if (settings.TryGetValue("dataLossInterval", out setting))
                m_dataStreamMonitor.Interval = double.Parse(setting) * 1000.0D;
            else
                m_dataStreamMonitor.Interval = 5000.0D;

            if (settings.TryGetValue("delayedConnectionInterval", out setting))
            {
                double interval = double.Parse(setting) * 1000.0D;

                // Minimum delay is one millisecond
                if (interval < 1.0D)
                    interval = 1.0D;

                ConnectionAttemptInterval = interval;
            }
            else
                ConnectionAttemptInterval = 1500.0D;

            if (settings.TryGetValue("allowUseOfCachedConfiguration", out setting))
                m_allowUseOfCachedConfiguration = setting.ParseBoolean();
            else
                m_allowUseOfCachedConfiguration = true;

            // Create a new phasor protocol frame parser for non-virtual connections
            MultiProtocolFrameParser frameParser = new MultiProtocolFrameParser();

            // Most of the parameters in the connection string will be for the data source in the frame parser
            // so we provide all of them, other parameters will simply be ignored
            frameParser.ConnectionString = ConnectionString;

            // Since input adapter will automatically reconnect on connection exceptions, we need only to specify
            // that the frame parser try to connect once per connection attempt
            frameParser.MaximumConnectionAttempts = 1;

            // For captured data simulations we will inject a simulated timestamp and auto-repeat file stream...
            if (frameParser.TransportProtocol == TransportProtocol.File)
            {
                if (settings.TryGetValue("definedFrameRate", out setting))
                    frameParser.DefinedFrameRate = int.Parse(setting);
                else
                    frameParser.DefinedFrameRate = 30;

                if (settings.TryGetValue("autoRepeatFile", out setting))
                    frameParser.AutoRepeatCapturedPlayback = setting.ParseBoolean();
                else
                    frameParser.AutoRepeatCapturedPlayback = true;

                if (settings.TryGetValue("useHighResolutionInputTimer", out setting))
                    frameParser.UseHighResolutionInputTimer = setting.ParseBoolean();
                else
                    frameParser.UseHighResolutionInputTimer = false;
            }

            // Apply other settings as needed
            if (settings.TryGetValue("simulateTimestamp", out setting))
                frameParser.InjectSimulatedTimestamp = setting.ParseBoolean();
            else
                frameParser.InjectSimulatedTimestamp = (frameParser.TransportProtocol == TransportProtocol.File);

            if (settings.TryGetValue("allowedParsingExceptions", out setting))
                frameParser.AllowedParsingExceptions = int.Parse(setting);

            if (settings.TryGetValue("parsingExceptionWindow", out setting))
                frameParser.ParsingExceptionWindow = Ticks.FromSeconds(double.Parse(setting));

            if (settings.TryGetValue("autoStartDataParsingSequence", out setting))
                frameParser.AutoStartDataParsingSequence = setting.ParseBoolean();

            if (settings.TryGetValue("skipDisableRealTimeData", out setting))
                frameParser.SkipDisableRealTimeData = setting.ParseBoolean();

            if (settings.TryGetValue("executeParseOnSeparateThread", out setting))
                frameParser.ExecuteParseOnSeparateThread = setting.ParseBoolean();
            //else
            //    frameParser.ExecuteParseOnSeparateThread = true;

            if (settings.TryGetValue("configurationFile", out setting))
                LoadConfiguration(setting);

            // Provide access ID to frame parser as this may be necessary to make a phasor connection
            frameParser.DeviceID = m_accessID;
            frameParser.SourceName = Name;

            // Assign reference to frame parser for this connection and attach to needed events
            this.FrameParser = frameParser;

            // Load input devices associated with this connection
            LoadInputDevices();

            // Load active device measurements associated with this connection
            LoadDeviceMeasurements();
        }

        // Load device list for this mapper connection
        private void LoadInputDevices()
        {
            ConfigurationCell definedDevice;
            string deviceName;

            m_definedDevices = new Dictionary<ushort, ConfigurationCell>();

            if (m_isConcentrator)
            {
                StringBuilder deviceStatus = new StringBuilder();
                bool devicedAdded;
                int index = 0;

                deviceStatus.AppendLine();
                deviceStatus.AppendLine();
                deviceStatus.Append("Loading expected concentrator device list...");
                deviceStatus.AppendLine();
                deviceStatus.AppendLine();

                // Making a connection to a concentrator that can support multiple devices
                foreach (DataRow row in DataSource.Tables["InputStreamDevices"].Select(string.Format("ParentID={0}", SharedMappingID)))
                {
                    // Create new configuration cell parsing needed ID code and label from input stream configuration
                    definedDevice = new ConfigurationCell(ushort.Parse(row["AccessID"].ToString()));
                    deviceName = row["Acronym"].ToNonNullString("[undefined]").Trim();
                    definedDevice.StationName = row["Name"].ToNonNullString(deviceName).Trim().TruncateRight(definedDevice.MaximumStationNameLength);
                    definedDevice.IDLabel = deviceName.TruncateRight(definedDevice.IDLabelLength);
                    definedDevice.Tag = uint.Parse(row["ID"].ToString());
                    definedDevice.Source = this;
                    devicedAdded = false;

                    // See if key already exists in this collection
                    if (m_definedDevices.ContainsKey(definedDevice.IDCode))
                    {
                        // For devices that do not have unique ID codes, we fall back on its label for unique lookup
                        if (m_labelDefinedDevices == null)
                            m_labelDefinedDevices = new Dictionary<string, ConfigurationCell>(StringComparer.OrdinalIgnoreCase);

                        if (m_labelDefinedDevices.ContainsKey(definedDevice.StationName))
                        {
                            OnProcessException(new InvalidOperationException(string.Format("ERROR: Device ID \"{0}\", labeled \"{1}\", was not unique in the {2} input stream. Data from devices that are not distinctly defined by ID code or label will not be correctly parsed until uniquely identified.", definedDevice.IDCode, definedDevice.StationName, Name)));
                            definedDevice.Dispose();
                        }
                        else
                        {
                            m_labelDefinedDevices.Add(definedDevice.StationName, definedDevice);
                            devicedAdded = true;
                        }
                    }
                    else
                    {
                        m_definedDevices.Add(definedDevice.IDCode, definedDevice);
                        devicedAdded = true;
                    }

                    if (devicedAdded)
                    {
                        // Create status display string for expected device
                        deviceStatus.Append("   Device ");
                        deviceStatus.Append((index++).ToString("00"));
                        deviceStatus.Append(": ");
                        deviceStatus.Append(definedDevice.StationName);
                        deviceStatus.Append(" (");
                        deviceStatus.Append(definedDevice.IDCode);
                        deviceStatus.Append(')');
                        deviceStatus.AppendLine();
                    }
                }

                OnStatusMessage(deviceStatus.ToString());

                if (m_labelDefinedDevices != null)
                    OnStatusMessage("WARNING: {0} has {1} defined input devices that do not have unique ID codes (i.e., the AccessID), as a result system will use the device label for identification. This is not the optimal configuration.", Name, m_labelDefinedDevices.Count);
            }
            else
            {
                // Making a connection to a single device
                definedDevice = new ConfigurationCell(m_accessID);

                // Used shared mapping name for single device connection if defined - this causes measurement mappings to be associated
                // with alternate device by caching signal references associated with shared mapping acronym
                if (string.IsNullOrWhiteSpace(SharedMapping))
                    deviceName = Name.ToNonNullString("[undefined]").Trim();
                else
                    deviceName = SharedMapping;

                definedDevice.StationName = deviceName.TruncateRight(definedDevice.MaximumStationNameLength);
                definedDevice.IDLabel = deviceName.TruncateRight(definedDevice.IDLabelLength);
                definedDevice.Tag = ID;
                definedDevice.Source = this;
                m_definedDevices.Add(definedDevice.IDCode, definedDevice);
            }
        }

        // Load active device measurements for this mapper connection
        private void LoadDeviceMeasurements()
        {
            Measurement definedMeasurement;
            Guid signalID;
            string signalReference;

            m_definedMeasurements = new Dictionary<string, IMeasurement>();

            foreach (DataRow row in DataSource.Tables["ActiveMeasurements"].Select(string.Format("DeviceID={0}", SharedMappingID)))
            {
                signalReference = row["SignalReference"].ToString();

                if (!string.IsNullOrWhiteSpace(signalReference))
                {
                    try
                    {
                        // Get measurement's signal ID
                        signalID = new Guid(row["SignalID"].ToNonNullString(Guid.NewGuid().ToString()));

                        // Create a measurement with a reference associated with this adapter
                        definedMeasurement = new Measurement()
                        {
                            ID = signalID,
                            Key = MeasurementKey.Parse(row["ID"].ToString(), signalID),
                            TagName = signalReference,
                            Adder = double.Parse(row["Adder"].ToNonNullString("0.0")),
                            Multiplier = double.Parse(row["Multiplier"].ToNonNullString("1.0"))
                        };

                        // Add measurement to definition list keyed by signal reference
                        m_definedMeasurements.Add(signalReference, definedMeasurement);
                    }
                    catch (Exception ex)
                    {
                        OnProcessException(new InvalidOperationException(string.Format("Failed to load signal reference \"{0}\" due to exception: {1}", signalReference, ex.Message), ex));
                    }
                }
            }

            OnStatusMessage("Loaded {0} active device measurements...", m_definedMeasurements.Count);
        }

        /// <summary>
        /// Sends the specified <see cref="DeviceCommand"/> to the current device connection.
        /// </summary>
        /// <param name="command"><see cref="DeviceCommand"/> to send to connected device.</param>
        [AdapterCommand("Sends the specified command to connected phasor device.")]
        public void SendCommand(DeviceCommand command)
        {
            if (m_frameParser != null)
            {
                if (m_frameParser.SendDeviceCommand(command) != null)
                    OnStatusMessage("Sent device command \"{0}\"...", command);
            }
            else
                OnStatusMessage("Failed to send device command \"{0}\", no frame parser is defined.", command);
        }

        /// <summary>
        /// Resets the statistics of all devices associated with this connection.
        /// </summary>
        [AdapterCommand("Resets the statistics of all devices associated with this connection.")]
        public void ResetStatistics()
        {
            if (m_definedDevices != null)
            {
                foreach (ConfigurationCell definedDevice in DefinedDevices)
                {
                    definedDevice.DataQualityErrors = 0;
                    definedDevice.DeviceErrors = 0;
                    definedDevice.TotalFrames = 0;
                    definedDevice.TimeQualityErrors = 0;
                }

                m_outOfOrderFrames = 0;

                OnStatusMessage("Statistics reset for all devices associated with this connection.");
            }
            else
                OnStatusMessage("Failed to reset statistics, no devices are defined.");
        }

        /// <summary>
        /// Resets the statistics of the specified device associated with this connection.
        /// </summary>
        /// <param name="idCode">Integer ID code of device on which to reset statistics.</param>
        [AdapterCommand("Resets the statistics of the device with the specified ID code.")]
        public void ResetDeviceStatistics(ushort idCode)
        {
            if (m_definedDevices != null)
            {
                ConfigurationCell definedDevice;

                if (m_definedDevices.TryGetValue(idCode, out definedDevice))
                {
                    definedDevice.DataQualityErrors = 0;
                    definedDevice.DeviceErrors = 0;
                    definedDevice.TotalFrames = 0;
                    definedDevice.TimeQualityErrors = 0;

                    OnStatusMessage("Statistics reset for device with ID code \"{0}\" associated with this connection.", idCode);
                }
                else
                    OnStatusMessage("WARNING: Failed to find device with ID code \"{0}\" associated with this connection.", idCode);
            }
            else
                OnStatusMessage("Failed to reset statistics, no devices are defined.");
        }

        /// <summary>
        /// Resets counters related to latency calculations.
        /// </summary>
        public void ResetLatencyCounters()
        {
            m_minimumLatency = 0;
            m_maximumLatency = 0;
            m_totalLatency = 0;
            m_latencyMeasurements = 0;
        }

        /// <summary>
        /// Attempts to load the last known good configuration.
        /// </summary>
        [AdapterCommand("Attempts to load the last known good configuration.")]
        public void LoadCachedConfiguration()
        {
            try
            {
                IConfigurationFrame configFrame = ConfigurationFrame.GetCachedConfiguration(Name, true);

                // As soon as a configuration frame is made available to the frame parser, regardless of source,
                // full parsing of data frames can begin...
                if (configFrame != null)
                {
                    m_frameParser.ConfigurationFrame = configFrame;
                    m_receivedConfigFrame = true;
                    m_configurationChanges++;
                }
                else
                    OnStatusMessage("NOTICE: Cannot load cached configuration, file \"{0}\" does not exist.", ConfigurationCacheFileName);
            }
            catch (Exception ex)
            {
                OnProcessException(new InvalidOperationException(string.Format("Failed to load cached configuration \"{0}\": {1}", ConfigurationCacheFileName, ex.Message), ex));
            }
        }

        /// <summary>
        /// Attempts to load the specified configuration.
        /// </summary>
        /// <param name="configurationFileName">Path and file name containing serialized configuration.</param>
        [AdapterCommand("Attempts to load the specified configuration.")]
        public void LoadConfiguration(string configurationFileName)
        {
            try
            {
                IConfigurationFrame configFrame = ConfigurationFrame.GetCachedConfiguration(configurationFileName, false);

                // As soon as a configuration frame is made available to the frame parser, regardless of source,
                // full parsing of data frames can begin...
                if (configFrame != null)
                {
                    m_frameParser.ConfigurationFrame = configFrame;

                    // Cache this configuration frame since its being loaded as the new last known good configuration
                    ThreadPool.QueueUserWorkItem(ConfigurationFrame.Cache,
                        new EventArgs<IConfigurationFrame, Action<Exception>, string>(configFrame, OnProcessException, Name));

                    m_receivedConfigFrame = true;
                    m_configurationChanges++;
                }
                else
                    OnStatusMessage("NOTICE: Cannot load configuration, file \"{0}\" does not exist.", configurationFileName);
            }
            catch (Exception ex)
            {
                OnProcessException(new InvalidOperationException(string.Format("Failed to load configuration \"{0}\": {1}", configurationFileName, ex.Message), ex));
            }
        }

        /// <summary>
        /// Gets a short one-line status of this <see cref="PhasorMeasurementMapper"/>.
        /// </summary>
        /// <param name="maxLength">Maximum number of available characters for display.</param>
        /// <returns>A short one-line summary of the current status of this <see cref="PhasorMeasurementMapper"/>.</returns>
        public override string GetShortStatus(int maxLength)
        {
            StringBuilder status = new StringBuilder();

            if (m_frameParser != null && m_frameParser.IsConnected)
            {
                if (m_lastReportTime > 0)
                {
                    // Calculate total bad frames
                    long totalDataErrors = 0;

                    foreach (ConfigurationCell definedDevice in DefinedDevices)
                    {
                        totalDataErrors += definedDevice.DataQualityErrors;
                    }

                    // Generate a short connect time
                    Time connectionTime = m_frameParser.ConnectionTime;
                    string uptime;

                    if (connectionTime.ToDays() < 1.0D)
                    {
                        if (connectionTime.ToHours() < 1.0D)
                        {
                            if (connectionTime.ToMinutes() < 1.0D)
                                uptime = (int)connectionTime + " seconds";
                            else
                                uptime = connectionTime.ToMinutes().ToString("0.0") + " minutes";
                        }
                        else
                            uptime = connectionTime.ToHours().ToString("0.00") + " hours";
                    }
                    else
                        uptime = connectionTime.ToDays().ToString("0.00") + " days";

                    string uptimeStats = string.Format("Up for {0}, {1} errors",
                        uptime, totalDataErrors);

                    string runtimeStats = string.Format(" {0} {1} fps",
                        ((DateTime)m_lastReportTime).ToString("MM/dd/yyyy HH:mm:ss.fff"),
                        m_frameParser.CalculatedFrameRate.ToString("0.00"));

                    uptimeStats = uptimeStats.TruncateRight(maxLength - runtimeStats.Length).PadLeft(maxLength - runtimeStats.Length, '\xA0');

                    status.Append(uptimeStats);
                    status.Append(runtimeStats);
                }
                else if (m_frameParser.ConfigurationFrame == null)
                {
                    status.AppendFormat("  >> Awaiting configuration frame - {0} bytes received", m_frameParser.TotalBytesReceived);
                }
                else
                {
                    status.AppendFormat("  ** No data mapped, check configuration - {0} bytes received", m_frameParser.TotalBytesReceived);
                }
            }
            else
                status.Append("  ** Not connected");

            return status.ToString();
        }

        /// <summary>
        /// Attempts to connect to data input source.
        /// </summary>
        protected override void AttemptConnection()
        {
            m_lastReportTime = 0;
            m_bytesReceived = 0;
            m_outOfOrderFrames = 0;
            m_receivedConfigFrame = false;
            m_cachedConfigLoadAttempted = false;

            // Start frame parser
            if (m_frameParser != null)
                m_frameParser.Start();
        }

        /// <summary>
        /// Attempts to disconnect from data input source.
        /// </summary>
        protected override void AttemptDisconnection()
        {
            // Stop data stream monitor
            m_dataStreamMonitor.Enabled = false;

            // Stop frame parser
            if (m_frameParser != null)
                m_frameParser.Stop();
        }

        /// <summary>
        /// Map parsed measurement value to defined measurement attributes (i.e., assign meta-data to parsed measured value).
        /// </summary>
        /// <param name="mappedMeasurements">Destination collection for the mapped measurement values.</param>
        /// <param name="signalReference">Derived <see cref="SignalReference"/> string for the parsed measurement value.</param>
        /// <param name="parsedMeasurement">The parsed <see cref="IMeasurement"/> value.</param>
        /// <remarks>
        /// This procedure is used to identify a parsed measurement value by its derived signal reference and apply the
        /// additional needed measurement meta-data attributes (i.e., ID, Source, Adder and Multiplier).
        /// </remarks>
        protected void MapMeasurementAttributes(ICollection<IMeasurement> mappedMeasurements, string signalReference, IMeasurement parsedMeasurement)
        {
            // Coming into this function the parsed measurement value will only have a "value" and a "timestamp";
            // the measurement will not yet be associated with an actual historian measurement ID as the measurement
            // will have come directly out of the parsed phasor protocol data frame.  We take the generated signal
            // reference and use that to lookup the actual historian measurement ID, source, adder and multipler.
            IMeasurement definedMeasurement;

            // Lookup signal reference in defined measurement list
            if (m_definedMeasurements.TryGetValue(signalReference, out definedMeasurement))
            {
                // Assign ID and other relevant attributes to the parsed measurement value
                parsedMeasurement.ID = definedMeasurement.ID;
                parsedMeasurement.Key = definedMeasurement.Key;
                parsedMeasurement.Adder = definedMeasurement.Adder;              // Allows for run-time additive measurement value adjustments
                parsedMeasurement.Multiplier = definedMeasurement.Multiplier;    // Allows for run-time mulplicative measurement value adjustments

                // Add the updated measurement value to the destination measurement collection
                mappedMeasurements.Add(parsedMeasurement);
            }
        }

        /// <summary>
        /// Extract frame measurements and add expose them via the <see cref="IInputAdapter.NewMeasurements"/> event.
        /// </summary>
        /// <param name="frame">Phasor data frame to extract measurements from.</param>
        protected void ExtractFrameMeasurements(IDataFrame frame)
        {
            const int AngleIndex = (int)CompositePhasorValue.Angle;
            const int MagnitudeIndex = (int)CompositePhasorValue.Magnitude;
            const int FrequencyIndex = (int)CompositeFrequencyValue.Frequency;
            const int DfDtIndex = (int)CompositeFrequencyValue.DfDt;

            ICollection<IMeasurement> mappedMeasurements = new List<IMeasurement>();
            ConfigurationCell definedDevice;
            PhasorValueCollection phasors;
            AnalogValueCollection analogs;
            DigitalValueCollection digitals;
            IMeasurement[] measurements;
            Ticks timestamp;
            int x, count;

            // Adjust time to UTC based on source time zone
            if (m_timezone != TimeZoneInfo.Utc)
                frame.Timestamp = TimeZoneInfo.ConvertTimeToUtc(frame.Timestamp, m_timezone);

            // We also allow "fine tuning" of time for fickle GPS clocks...
            if (m_timeAdjustmentTicks != 0)
                frame.Timestamp += m_timeAdjustmentTicks;

            // Get adjusted timestamp of this frame
            timestamp = frame.Timestamp;

            // Track latest reporting time for mapper
            if (timestamp > m_lastReportTime)
                m_lastReportTime = timestamp;
            else
                m_outOfOrderFrames++;

            // Track latency statistics against system time - in order for these statistics
            // to be useful, the local clock must be fairly accurate
            long latency = frame.ReceivedTimestamp - (long)timestamp;

            if (m_minimumLatency > latency || m_minimumLatency == 0)
                m_minimumLatency = latency;

            if (m_maximumLatency < latency || m_maximumLatency == 0)
                m_maximumLatency = latency;

            m_totalLatency += latency;
            m_latencyMeasurements++;

            // Loop through each parsed device in the data frame
            foreach (IDataCell parsedDevice in frame.Cells)
            {
                try
                {
                    // Lookup device by its label (if needed), then by its ID code
                    if ((m_labelDefinedDevices != null &&
                        m_labelDefinedDevices.TryGetValue(parsedDevice.StationName.ToNonNullString(), out definedDevice)) ||
                        m_definedDevices.TryGetValue(parsedDevice.IDCode, out definedDevice))
                    {
                        // Track latest reporting time for this device
                        if (timestamp > definedDevice.LastReportTime)
                            definedDevice.LastReportTime = timestamp;

                        // Track quality statistics for this device
                        definedDevice.TotalFrames++;

                        if (!parsedDevice.DataIsValid)
                            definedDevice.DataQualityErrors++;

                        if (!parsedDevice.SynchronizationIsValid)
                            definedDevice.TimeQualityErrors++;

                        if (parsedDevice.DeviceError)
                            definedDevice.DeviceErrors++;

                        // Map status flags (SF) from device data cell itself (IDataCell implements IMeasurement
                        // and exposes the status flags as its value)
                        MapMeasurementAttributes(mappedMeasurements, definedDevice.GetSignalReference(SignalKind.Status), parsedDevice);

                        // Map phase angles (PAn) and magnitudes (PMn)
                        phasors = parsedDevice.PhasorValues;
                        count = phasors.Count;

                        for (x = 0; x < count; x++)
                        {
                            // Get composite phasor measurements
                            measurements = phasors[x].Measurements;

                            // Map angle
                            MapMeasurementAttributes(mappedMeasurements, definedDevice.GetSignalReference(SignalKind.Angle, x, count), measurements[AngleIndex]);

                            // Map magnitude
                            MapMeasurementAttributes(mappedMeasurements, definedDevice.GetSignalReference(SignalKind.Magnitude, x, count), measurements[MagnitudeIndex]);
                        }

                        // Map frequency (FQ) and dF/dt (DF)
                        measurements = parsedDevice.FrequencyValue.Measurements;

                        // Map frequency
                        MapMeasurementAttributes(mappedMeasurements, definedDevice.GetSignalReference(SignalKind.Frequency), measurements[FrequencyIndex]);

                        // Map dF/dt
                        MapMeasurementAttributes(mappedMeasurements, definedDevice.GetSignalReference(SignalKind.DfDt), measurements[DfDtIndex]);

                        // Map analog values (AVn)
                        analogs = parsedDevice.AnalogValues;
                        count = analogs.Count;

                        for (x = 0; x < count; x++)
                        {
                            // Map analog value
                            MapMeasurementAttributes(mappedMeasurements, definedDevice.GetSignalReference(SignalKind.Analog, x, count), analogs[x].Measurements[0]);
                        }

                        // Map digital values (DVn)
                        digitals = parsedDevice.DigitalValues;
                        count = digitals.Count;

                        for (x = 0; x < count; x++)
                        {
                            // Map digital value
                            MapMeasurementAttributes(mappedMeasurements, definedDevice.GetSignalReference(SignalKind.Digital, x, count), digitals[x].Measurements[0]);
                        }
                    }
                    else
                    {
                        // Encountered an undefined device, track frame counts
                        lock (m_undefinedDevices)
                        {
                            long frameCount;

                            if (m_undefinedDevices.TryGetValue(parsedDevice.StationName, out frameCount))
                            {
                                frameCount++;
                                m_undefinedDevices[parsedDevice.StationName] = frameCount;
                            }
                            else
                            {
                                m_undefinedDevices.Add(parsedDevice.StationName, 1);
                                OnStatusMessage("WARNING: Encountered an undefined device \"{0}\"...", parsedDevice.StationName);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    OnProcessException(new InvalidOperationException(string.Format("Exception encountered while mapping \"{0}\" data frame cell \"{1}\" elements to measurements: {2}", Name, parsedDevice.StationName.ToNonNullString("[undefined]"), ex.Message), ex));
                }
            }

            // Provide real-time measurements where needed
            OnNewMeasurements(mappedMeasurements);
        }

        /// <summary>
        /// Get signal reference for specified <see cref="SignalKind"/>.
        /// </summary>
        /// <param name="type"><see cref="SignalKind"/> to request signal reference for.</param>
        /// <returns>Signal reference of given <see cref="SignalKind"/>.</returns>
        public string GetSignalReference(SignalKind type)
        {
            // We cache non-indexed signal reference strings so they don't need to be generated at each mapping call.
            string[] references;

            // Look up synonym in dictionary based on signal type, if found return single element
            if (m_generatedSignalReferenceCache.TryGetValue(type, out references))
                return references[0];

            // Create a new signal reference array (for single element)
            references = new string[1];

            // Create and cache new non-indexed signal reference
            references[0] = SignalReference.ToString(Name + "!IS", type);

            // Cache generated signal synonym
            m_generatedSignalReferenceCache.Add(type, references);

            return references[0];
        }

        /// <summary>
        /// Get signal reference for specified <see cref="SignalKind"/> and <paramref name="index"/>.
        /// </summary>
        /// <param name="type"><see cref="SignalKind"/> to request signal reference for.</param>
        /// <param name="index">Index <see cref="SignalKind"/> to request signal reference for.</param>
        /// <param name="count">Number of signals defined for this <see cref="SignalKind"/>.</param>
        /// <returns>Signal reference of given <see cref="SignalKind"/> and <paramref name="index"/>.</returns>
        public string GetSignalReference(SignalKind type, int index, int count)
        {
            // We cache indexed signal reference strings so they don't need to be generated at each mapping call.
            // For speed purposes we intentionally do not validate that signalIndex falls within signalCount, be
            // sure calling procedures are very careful with parameters...
            string[] references;

            // Look up synonym in dictionary based on signal type
            if (m_generatedSignalReferenceCache.TryGetValue(type, out references))
            {
                // Verify signal count has not changed (we may have received new configuration from device)
                if (count == references.Length)
                {
                    // Create and cache new signal reference if it doesn't exist
                    if (references[index] == null)
                        references[index] = SignalReference.ToString(Name + "!IS", type, index + 1);

                    return references[index];
                }
            }

            // Create a new indexed signal reference array
            references = new string[count];

            // Create and cache new signal reference
            references[index] = SignalReference.ToString(Name + "!IS", type, index + 1);

            // Cache generated signal synonym array
            m_generatedSignalReferenceCache.Add(type, references);

            return references[index];
        }

        /// <summary>
        /// Returns the hash code for this instance.
        /// </summary>
        /// <returns>A 32-bit signed integer hash code.</returns>
        public override int GetHashCode()
        {
            if (m_hashCode == 0)
                m_hashCode = Guid.NewGuid().GetHashCode();

            return m_hashCode;
        }

        private void m_frameParser_ReceivedDataFrame(object sender, EventArgs<IDataFrame> e)
        {
            ExtractFrameMeasurements(e.Argument);
            m_totalDataFrames++;
        }

        private void m_frameParser_ReceivedConfigurationFrame(object sender, EventArgs<IConfigurationFrame> e)
        {
            if (!m_receivedConfigFrame)
            {
                OnStatusMessage("Received configuration frame at {0}", DateTime.UtcNow);

                // Cache configuration on an independent thread in case this takes some time
                ThreadPool.QueueUserWorkItem(ConfigurationFrame.Cache,
                    new EventArgs<IConfigurationFrame, Action<Exception>, string>(e.Argument, OnProcessException, Name));

                m_receivedConfigFrame = true;
            }

            m_configurationChanges++;
            m_totalConfigurationFrames++;
        }

        private void m_frameParser_ReceivedHeaderFrame(object sender, EventArgs<IHeaderFrame> e)
        {
            m_totalHeaderFrames++;
        }

        private void m_frameParser_ReceivedFrameBufferImage(object sender, EventArgs<FundamentalFrameType, byte[], int, int> e)
        {
            // We track bytes received so that connection can be restarted if data is not flowing
            m_bytesReceived += e.Argument4;
        }

        private void m_frameParser_ConnectionTerminated(object sender, EventArgs e)
        {
            OnDisconnected();

            if (m_frameParser.Enabled)
            {
                // Communications layer closed connection (close not initiated by system) - so we restart connection cycle...
                OnStatusMessage("WARNING: Connection closed by remote device, attempting reconnection...");
                Start();
            }
        }

        private void m_frameParser_ConnectionEstablished(object sender, EventArgs e)
        {
            OnConnected();

            ResetStatistics();

            // Enable data stream monitor for connections that support commands
            m_dataStreamMonitor.Enabled = m_frameParser.DeviceSupportsCommands || m_allowUseOfCachedConfiguration;
        }

        private void m_frameParser_ConnectionException(object sender, EventArgs<Exception, int> e)
        {
            Exception ex = e.Argument1;
            OnProcessException(new InvalidOperationException(string.Format("Connection attempt failed: {0}", ex.Message), ex));

            // So long as user hasn't requested to stop, keep trying connection
            if (Enabled)
                Start();
        }

        private void m_frameParser_ParsingException(object sender, EventArgs<Exception> e)
        {
            OnProcessException(e.Argument);
        }

        private void m_frameParser_ExceededParsingExceptionThreshold(object sender, EventArgs e)
        {
            OnStatusMessage("\r\nConnection is being reset due to an excessive number of exceptions...\r\n");

            // So long as user hasn't already requested to stop, we restart connection
            if (Enabled)
                Start();
        }

        private void m_frameParser_ConnectionAttempt(object sender, EventArgs e)
        {
            OnStatusMessage("Initiating {0} {1} based connection...", m_frameParser.PhasorProtocol.GetFormattedProtocolName(), m_frameParser.TransportProtocol.ToString().ToUpper());
            m_connectionAttempts++;
        }

        private void m_frameParser_ConfigurationChanged(object sender, EventArgs e)
        {
            OnStatusMessage("NOTICE: Configuration has changed, requesting new configuration frame...");

            // Reset data stream monitor to allow time for non-cached reception of new configuration frame...
            if (m_dataStreamMonitor.Enabled)
            {
                m_dataStreamMonitor.Stop();
                m_dataStreamMonitor.Start();
            }

            m_receivedConfigFrame = false;
            SendCommand(DeviceCommand.SendConfigurationFrame2);
        }

        private void m_dataStreamMonitor_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (m_bytesReceived == 0 && m_frameParser.DeviceSupportsCommands)
            {
                // If we've received no data in the last timespan, we restart connect cycle...
                m_dataStreamMonitor.Enabled = false;
                OnStatusMessage("\r\nNo data received in {0} seconds, restarting connect cycle...\r\n", (m_dataStreamMonitor.Interval / 1000.0D).ToString("0.0"));
                Start();
            }
            else if (!m_receivedConfigFrame && m_allowUseOfCachedConfiguration)
            {
                // If data is being received but a configuration has yet to be loaded, attempt to load last known good configuration
                if (!m_cachedConfigLoadAttempted)
                {
                    OnStatusMessage("Configuration frame has yet to be received, attempting to load cached configuration...");
                    m_cachedConfigLoadAttempted = true;
                    LoadCachedConfiguration();
                }
                else if (m_frameParser.DeviceSupportsCommands)
                {
                    OnStatusMessage("\r\nConfiguration frame has yet to be received even after attempt to load from cache, restarting connect cycle...\r\n");
                    Start();
                }
            }

            m_bytesReceived = 0;
        }

        #endregion
    }
}
