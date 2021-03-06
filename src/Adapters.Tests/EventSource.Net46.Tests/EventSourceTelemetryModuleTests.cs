﻿//-----------------------------------------------------------------------
// <copyright file="EventSourceTelemetryModuleTests.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace Microsoft.ApplicationInsights.EventSourceListener.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Tracing;
    using System.Linq;
    using System.Threading.Tasks;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    using Microsoft.ApplicationInsights.DataContracts;
    using Microsoft.ApplicationInsights.Extensibility;
    using Microsoft.ApplicationInsights.EventSourceListener;
    using Microsoft.ApplicationInsights.EventSourceListener.Implementation;

    using Microsoft.ApplicationInsights.Tests;
    using Microsoft.ApplicationInsights.Tracing.Tests;
    using Microsoft.ApplicationInsights.CommonTestShared;
    using Microsoft.ApplicationInsights.Extensibility.Implementation;

    [TestClass]
    public sealed class EventSourceTelemetryModuleTests : IDisposable
    {
        private readonly AdapterHelper adapterHelper = new AdapterHelper();

        public void Dispose()
        {
            this.adapterHelper.Dispose();
        }

        [TestMethod]
        [TestCategory("EventSourceListener")]
        public void ThrowsWhenNullConfigurationPassedToInitialize()
        {
            using (var module = new EventSourceTelemetryModule())
            {
                ExceptionAssert.Throws<ArgumentNullException>(() =>
                {
                    module.Initialize(null);
                });
            }
        }

        [TestMethod]
        [TestCategory("EventSourceListener")]
        public void WarnsIfNoSourcesConfigured()
        {
            using (var eventListener = new EventSourceModuleDiagnosticListener())
            using (var module = new EventSourceTelemetryModule())
            {
                module.Initialize(GetTestTelemetryConfiguration());
                Assert.AreEqual(1, eventListener.EventsReceived.Count);
                Assert.AreEqual(nameof(EventSourceListenerEventSource.NoEventSourcesConfigured), eventListener.EventsReceived[0]);
            }
        }

        [TestMethod]
        [TestCategory("EventSourceListener")]
        public void ReportsSingleEvent()
        {
            using (var module = new EventSourceTelemetryModule())
            {
                var listeningRequest = new EventSourceListeningRequest();
                listeningRequest.Name = TestEventSource.ProviderName;
                module.Sources.Add(listeningRequest);

                module.Initialize(GetTestTelemetryConfiguration());

                TestEventSource.Default.InfoEvent("Hey!");

                TraceTelemetry telemetry = (TraceTelemetry)this.adapterHelper.Channel.SentItems.First();
                Assert.AreEqual("Hey!", telemetry.Message);
                Assert.AreEqual("Hey!", telemetry.Properties["information"]);
                Assert.AreEqual(SeverityLevel.Information, telemetry.SeverityLevel);
                string expectedVersion = SdkVersionHelper.GetExpectedSdkVersion(typeof(EventSourceTelemetryModule), prefix: "evl:");
                Assert.AreEqual(expectedVersion, telemetry.Context.GetInternalContext().SdkVersion);
            }
        }

        [TestMethod]
        [TestCategory("EventSourceListener")]
        public void ReactsToConfigurationChanges()
        {
            using (var module = new EventSourceTelemetryModule())
            {
                var listeningRequest = new EventSourceListeningRequest();
                listeningRequest.Name = TestEventSource.ProviderName;
                module.Sources.Add(listeningRequest);

                module.Initialize(GetTestTelemetryConfiguration());

                TestEventSource.Default.InfoEvent("Hey!");
                TestEventSource.Default.WarningEvent(1, 2);

                // Now request reporting events only with certain keywords
                listeningRequest.Keywords = TestEventSource.Keywords.NonRoutine;
                module.Initialize(GetTestTelemetryConfiguration(resetChannel: false));

                TestEventSource.Default.InfoEvent("Hey again!");
                TestEventSource.Default.WarningEvent(3, 4);

                List<TraceTelemetry> expectedTelemetry = new List<TraceTelemetry>();
                TraceTelemetry traceTelemetry = new TraceTelemetry("Hey!", SeverityLevel.Information);
                traceTelemetry.Properties["information"] = "Hey!";
                expectedTelemetry.Add(traceTelemetry);
                traceTelemetry = new TraceTelemetry("Warning!", SeverityLevel.Warning);
                traceTelemetry.Properties["i1"] = 1.ToString();
                traceTelemetry.Properties["i2"] = 2.ToString();
                expectedTelemetry.Add(traceTelemetry);
                // Note that second informational event is not expected
                traceTelemetry = new TraceTelemetry("Warning!", SeverityLevel.Warning);
                traceTelemetry.Properties["i1"] = 3.ToString();
                traceTelemetry.Properties["i2"] = 4.ToString();
                expectedTelemetry.Add(traceTelemetry);

                CollectionAssert.AreEqual(expectedTelemetry, this.adapterHelper.Channel.SentItems, new TraceTelemetryComparer(), "Reported events are not what was expected");
            }
        }

        [TestMethod]
        [TestCategory("EventSourceListener")]
        public void ReportsSeverityLevel()
        {
            using (var module = new EventSourceTelemetryModule())
            {
                var listeningRequest = new EventSourceListeningRequest();
                listeningRequest.Name = TestEventSource.ProviderName;
                module.Sources.Add(listeningRequest);

                module.Initialize(GetTestTelemetryConfiguration());

                TestEventSource.Default.InfoEvent("Hey!");
                TestEventSource.Default.WarningEvent(1, 2);
                TestEventSource.Default.ErrorEvent(2.3, "default context");

                TraceTelemetry[] expectedTelemetry = new TraceTelemetry[]
                {
                    new TraceTelemetry("Hey!", SeverityLevel.Information),
                    new TraceTelemetry("Warning!", SeverityLevel.Warning),
                    new TraceTelemetry("Error!", SeverityLevel.Error)
                };

                CollectionAssert.AreEqual(expectedTelemetry, this.adapterHelper.Channel.SentItems, new TraceTelemetryComparer(), "Reported events are not what was expected");
            }
        }

        [TestMethod]
        [TestCategory("EventSourceListener")]
        public void ReportsAllProperties()
        {
            using (var module = new EventSourceTelemetryModule())
            {
                var listeningRequest = new EventSourceListeningRequest();
                listeningRequest.Name = TestEventSource.ProviderName;
                module.Sources.Add(listeningRequest);

                module.Initialize(GetTestTelemetryConfiguration());

                Guid eventId = new Guid("30ba9220-89a4-41e4-987c-9e27ade44b74");
                Guid activityId = new Guid("0724a028-27d7-40a9-a299-acf79ff0db94");
                EventSource.SetCurrentThreadActivityId(activityId);
                TestEventSource.Default.ComplexEvent(eventId);

                TraceTelemetry expected = new TraceTelemetry("Blah blah", SeverityLevel.Verbose);
                expected.Properties.Add("uniqueId", eventId.ToString());
                expected.Properties.Add("EventId", TestEventSource.ComplexEventId.ToString());
                expected.Properties.Add("EventName", nameof(TestEventSource.ComplexEvent));
                expected.Properties.Add("ActivityId", activityId.ToString());
                expected.Properties.Add("Keywords", "0x8000F00000000001");
                expected.Properties.Add("Channel", "Debug");
                expected.Properties.Add("Opcode", "Extension");
                expected.Properties.Add("Tags", "0x00000011");
                expected.Properties.Add("Task", "0x00000020");

                CollectionAssert.AreEqual(new TraceTelemetry[] { expected }, this.adapterHelper.Channel.SentItems, new TraceTelemetryComparer(),
                    "Reported event has properties different from expected");
            }
        }

        // TODO: there is a known issue with EventListner that prevents it from reporting activities properly
        // This problem is fixed in .NET Core 1.1. (https://github.com/dotnet/coreclr/pull/7591), but it won't be ported
        // to .NET Desktop till 4.7.1
        // When that happens, we should add a test verifying that the EventSourceTelemetryModule reports activity IDs
        // correctly, including nested activities.

        [TestMethod]
        [TestCategory("EventSourceListener")]
        public void HandlesDuplicatePropertyNames()
        {
            using (var module = new EventSourceTelemetryModule())
            {
                var listeningRequest = new EventSourceListeningRequest();
                listeningRequest.Name = TestEventSource.ProviderName;
                module.Sources.Add(listeningRequest);

                module.Initialize(GetTestTelemetryConfiguration());

                TestEventSource.Default.Tricky(7, "TrickyEvent", "Actual message");

                Assert.AreEqual(1, this.adapterHelper.Channel.SentItems.Length);
                TraceTelemetry telemetry = (TraceTelemetry)this.adapterHelper.Channel.SentItems[0];
                Assert.AreEqual("Manifest message", telemetry.Message);
                Assert.AreEqual(SeverityLevel.Information, telemetry.SeverityLevel);
                Assert.AreEqual("Actual message", telemetry.Properties["Message"]);
                Assert.AreEqual("7", telemetry.Properties["EventId"]);
                Assert.AreEqual("TrickyEvent", telemetry.Properties["EventName"]);
                Assert.IsTrue(telemetry.Properties[telemetry.Properties.Keys.First(key => key.StartsWith("EventId") && key != "EventId")].Equals("7"));
                Assert.IsTrue(telemetry.Properties[telemetry.Properties.Keys.First(key => key.StartsWith("EventName") && key != "EventName")].Equals("Tricky"));
            }
        }

        [TestMethod]
        [TestCategory("EventSourceListener")]
        public void ActivityPathDecoderDecodesHierarchicalActivityId()
        {
            Guid activityId = new Guid("000000110000000000000000be999d59");
            string activityPath = ActivityPathDecoder.GetActivityPathString(activityId);
            Assert.AreEqual("//1/1/", activityPath);
        }

        [TestMethod]
        [TestCategory("EventSourceListener")]
        public void ActivityPathDecoderHandlesNonhierarchicalActivityIds()
        {
            string guidString = "bf0209f9-bf5e-415e-86ed-0e20b615b406";
            Guid activityId = new Guid(guidString);
            string activityPath = ActivityPathDecoder.GetActivityPathString(activityId);
            Assert.AreEqual(guidString, activityPath);
        }

        [TestMethod]
        [TestCategory("EventSourceListener")]
        public void ActivityPathDecoderHandlesEmptyActivityId()
        {
            string activityPath = ActivityPathDecoder.GetActivityPathString(Guid.Empty);
            Assert.AreEqual(Guid.Empty.ToString(), activityPath);
        }

        private TelemetryConfiguration GetTestTelemetryConfiguration(bool resetChannel = true)
        {
            var configuration = new TelemetryConfiguration();
            configuration.InstrumentationKey = this.adapterHelper.InstrumentationKey;
            if (resetChannel)
            {
                configuration.TelemetryChannel = this.adapterHelper.Channel.Reset();
            }
            else
            {
                configuration.TelemetryChannel = this.adapterHelper.Channel;
            }
            return configuration;
        }
    }
}