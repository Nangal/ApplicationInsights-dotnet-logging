﻿// -----------------------------------------------------------------------
// <copyright file="ApplicationInsightsTraceListener.cs" company="Microsoft">
// Copyright (c) Microsoft Corporation. 
// All rights reserved.  2013
// </copyright>
// -----------------------------------------------------------------------

namespace Microsoft.ApplicationInsights.TraceListener
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Reflection;

    using Microsoft.ApplicationInsights.DataContracts;
    using Microsoft.ApplicationInsights.Extensibility.Implementation;

    /// <summary>
    /// Listener that routes all tracing and debugging output to ApplicationInsights logging framework.
    /// The messages will be uploaded to the Application Insights cloud service.
    /// </summary>
    public sealed class ApplicationInsightsTraceListener : TraceListener
    {
        /// <summary>
        /// Initializes a new instance of the ApplicationInsightsTraceListener class, without specifying a
        /// component ID.
        /// </summary>
        public ApplicationInsightsTraceListener() : this(string.Empty)
        {
        }

        /// <summary>
        /// Initializes a new instance of the ApplicationInsightsTraceListener class, and validates the component
        /// id passed as the initialize-data.
        /// An empty or null Component ID is acceptable because the event source will fall back to the one
        /// set in ApplicationInsights.config, however an invalid Component ID will throw an exception.
        /// </summary>
        /// <param name="instrumentationKey">Instrumentation Key of your application.</param>
        public ApplicationInsightsTraceListener(string instrumentationKey)
        {
            this.TelemetryClient = new TelemetryClient();
            if (!string.IsNullOrEmpty(instrumentationKey))
            {
                this.TelemetryClient.Context.InstrumentationKey = instrumentationKey;
            }

            this.TelemetryClient.Context.GetInternalContext().SdkVersion = "SD: " + GetAssemblyVersion();
        }

        internal TelemetryClient TelemetryClient { get; set; }
        
        /// <summary>
        /// Writes trace information, a message, and event information to the listener specific output.
        /// </summary>
        /// <param name="eventCache">A TraceEventCache object that contains the current process ID, thread ID, and stack trace information.</param>
        /// <param name="source">A name used to identify the output, typically the name of the application that generated the trace event.</param>
        /// <param name="eventType">One of the TraceEventType values specifying the type of event that has caused the trace.</param>
        /// <param name="id">A numeric identifier for the event.</param>
        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id)
        {
            this.TraceEvent(eventCache, source, eventType, id, id.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Writes trace information, a message, and event information to the listener specific output.
        /// </summary>
        /// <param name="eventCache">A TraceEventCache object that contains the current process ID, thread ID, and stack trace information.</param>
        /// <param name="source">A name used to identify the output, typically the name of the application that generated the trace event.</param>
        /// <param name="eventType">One of the TraceEventType values specifying the type of event that has caused the trace.</param>
        /// <param name="id">A numeric identifier for the event.</param>
        /// <param name="format">A format string that contains zero or more format items, which correspond to objects in the args array.</param>
        /// <param name="args">An object array containing zero or more objects to format.</param>
        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string format, params object[] args)
        {
            if (this.Filter != null &&
                this.Filter.ShouldTrace(eventCache, source, eventType, id, format, args, null, null) == false)
            {
                return;
            }

            string message = args == null ? format : string.Format(CultureInfo.InvariantCulture, format, args);
            this.TraceEvent(eventCache, source, eventType, id, message);
        }

        /// <summary>
        /// Writes trace information, a message, and event information to the listener specific output.
        /// </summary>
        /// <param name="eventCache">A TraceEventCache object that contains the current process ID, thread ID, and stack trace information.</param>
        /// <param name="source">A name used to identify the output, typically the name of the application that generated the trace event.</param>
        /// <param name="eventType">One of the TraceEventType values specifying the type of event that has caused the trace.</param>
        /// <param name="id">A numeric identifier for the event.</param>
        /// <param name="message">A message to write.</param>
        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string message)
        {
            if (this.Filter != null &&
                this.Filter.ShouldTrace(eventCache, source, eventType, id, message, null, null, null) == false)
            {
                return;
            }

            var trace = new TraceTelemetry(message);
            this.CreateTraceData(eventCache, eventType, id, trace);
            this.TelemetryClient.Track(trace);
        }

        /// <summary>
        /// Writes trace data to the listener specific output.
        /// </summary>
        /// <param name="eventCache">A TraceEventCache object that contains the current process ID, thread ID, and stack trace information.</param>
        /// <param name="source">A name used to identify the output, typically the name of the application that generated the trace event.</param>
        /// <param name="eventType">One of the TraceEventType values specifying the type of event that has caused the trace.</param>
        /// <param name="id">A numeric identifier for the event.</param>
        /// <param name="data">The trace data to emit.</param>
        public override void TraceData(TraceEventCache eventCache, string source, TraceEventType eventType, int id, object data)
        {
            if (this.Filter != null &&
                this.Filter.ShouldTrace(eventCache, source, eventType, id, string.Empty, null, data, null) == false)
            {
                return;
            }

            this.TraceData(eventCache, source, eventType, id, new[] { data });
        }

        /// <summary>
        /// Writes trace data to the listener specific output.
        /// </summary>
        /// <param name="eventCache">A TraceEventCache object that contains the current process ID, thread ID, and stack trace information.</param>
        /// <param name="source">A name used to identify the output, typically the name of the application that generated the trace event.</param>
        /// <param name="eventType">One of the TraceEventType values specifying the type of event that has caused the trace.</param>
        /// <param name="id">A numeric identifier for the event.</param>
        /// <param name="data">An array of objects to emit as data.</param>
        public override void TraceData(TraceEventCache eventCache, string source, TraceEventType eventType, int id, params object[] data)
        {
            if (this.Filter != null &&
                this.Filter.ShouldTrace(eventCache, source, eventType, id, string.Empty, null, null, data) == false)
            {
                return;
            }

            string message = string.Join(", ", data.Select(d => d == null ? string.Empty : d.ToString()));
            var trace = new TraceTelemetry(message);
            this.CreateTraceData(eventCache, eventType, id, trace);                       
            this.TelemetryClient.Track(trace);
        }

        /// <summary>
        /// Writes the specified message to the listener.
        /// </summary>
        /// <param name="message">A message to write.</param>
        public override void Write(string message)
        {
            if (this.Filter != null &&
                this.Filter.ShouldTrace(null, string.Empty, TraceEventType.Verbose, 0, message, null, null, null) == false)
            {
                return;
            }

            var trace = new TraceTelemetry(message);
            this.CreateTraceData(new TraceEventCache(), TraceEventType.Verbose, null, trace);
            this.TelemetryClient.Track(trace);
        }

        /// <summary>
        /// Writes the specified message to the listener followed by a line terminator.
        /// </summary>
        /// <param name="message">A message to write.</param>
        public override void WriteLine(string message)
        {
            this.Write(message + Environment.NewLine);
        }

        private static string GetAssemblyVersion()
        {
            return typeof(ApplicationInsightsTraceListener).Assembly.GetCustomAttributes(false)
                    .OfType<AssemblyFileVersionAttribute>()
                    .First()
                    .Version;
        }

        private void CreateTraceData(TraceEventCache eventCache, TraceEventType eventType, int? id, TraceTelemetry trace)
        {
            trace.SeverityLevel = this.GetSeverityLevel(eventType);
            
            IDictionary<string, string> metaData = trace.Properties;
            
            if (id.HasValue)
            {
                metaData.Add("EventId", id.Value.ToString(CultureInfo.InvariantCulture));
            }
        }

        private SeverityLevel GetSeverityLevel(TraceEventType eventType)
        {
            // TraceEventType.Resume, TraceEventType.Start, TraceEventType.Stop,
            // TraceEventType.Suspend, TraceEventType.Transfer, TraceEventType.Verbose
            // will fall into default Verbose
            switch (eventType)
            {
                case TraceEventType.Information:
                    return SeverityLevel.Information;
                case TraceEventType.Warning:
                    return SeverityLevel.Warning;
                case TraceEventType.Error:
                    return SeverityLevel.Error;
                case TraceEventType.Critical:
                    return SeverityLevel.Critical;
                default:
                    return SeverityLevel.Verbose;
            }
        }
    }
}
