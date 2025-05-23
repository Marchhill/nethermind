// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Globalization;
using Nethermind.Blockchain.Synchronization;
using Nethermind.Core.Timers;
using Nethermind.Logging;
using Nethermind.Stats;
using Nethermind.Synchronization.ParallelSync;
using Nethermind.Synchronization.Peers;
using Nethermind.Synchronization.Reporting;
using NSubstitute;
using NUnit.Framework;

namespace Nethermind.Synchronization.Test
{
    [TestFixture, Parallelizable(ParallelScope.All)]
    public class SyncReportTest
    {
        [Test]
        public void Smoke(
            [Values(true, false)]
            bool fastSync)
        {
            ISyncPeerPool pool = Substitute.For<ISyncPeerPool>();
            pool.InitializedPeersCount.Returns(1);
            ITimerFactory timerFactory = Substitute.For<ITimerFactory>();
            ITimer timer = Substitute.For<ITimer>();
            timerFactory.CreateTimer(Arg.Any<TimeSpan>()).Returns(timer);

            Queue<SyncMode> syncModes = new();
            syncModes.Enqueue(SyncMode.WaitingForBlock);
            syncModes.Enqueue(SyncMode.FastSync);
            syncModes.Enqueue(SyncMode.Full);
            syncModes.Enqueue(SyncMode.FastBlocks);
            syncModes.Enqueue(SyncMode.StateNodes);
            syncModes.Enqueue(SyncMode.Disconnected);

            SyncConfig syncConfig = new()
            {
                FastSync = fastSync,
            };

            SyncReport syncReport = new(pool, Substitute.For<INodeStatsManager>(), syncConfig, Substitute.For<IPivot>(), LimboLogs.Instance, timerFactory);

            void UpdateMode()
            {
                syncReport.SyncModeSelectorOnChanged(null, new SyncModeChangedEventArgs(SyncMode.None, syncModes.Count > 0 ? syncModes.Dequeue() : SyncMode.Full));
            }

            timer.Elapsed += Raise.Event();
            UpdateMode();
            syncReport.FastBlocksHeaders.MarkEnd();
            UpdateMode();
            syncReport.FastBlocksBodies.MarkEnd();
            UpdateMode();
            syncReport.FastBlocksReceipts.MarkEnd();
            timer.Elapsed += Raise.Event();
        }

        [Test]
        public void Ancient_bodies_and_receipts_are_reported_correctly()
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            ISyncPeerPool pool = Substitute.For<ISyncPeerPool>();
            pool.InitializedPeersCount.Returns(1);
            ITimerFactory timerFactory = Substitute.For<ITimerFactory>();
            ITimer timer = Substitute.For<ITimer>();
            timerFactory.CreateTimer(Arg.Any<TimeSpan>()).Returns(timer);
            ILogManager logManager = Substitute.For<ILogManager>();
            InterfaceLogger iLogger = Substitute.For<InterfaceLogger>();
            iLogger.IsInfo.Returns(true);
            iLogger.IsError.Returns(true);
            ILogger logger = new(iLogger);
            logManager.GetClassLogger(Arg.Any<string>()).Returns(logger);

            Queue<SyncMode> syncModes = new();
            syncModes.Enqueue(SyncMode.FastHeaders);
            syncModes.Enqueue(SyncMode.FastBodies);
            syncModes.Enqueue(SyncMode.FastReceipts);

            SyncConfig syncConfig = new()
            {
                FastSync = true,
                PivotNumber = "100",
            };

            SyncReport syncReport = new(pool, Substitute.For<INodeStatsManager>(), syncConfig, Substitute.For<IPivot>(), logManager, timerFactory);
            syncReport.FastBlocksHeaders.Reset(0, 100);
            syncReport.FastBlocksHeaders.CurrentQueued = 0;
            syncReport.FastBlocksBodies.Reset(0, 70);
            syncReport.FastBlocksBodies.CurrentQueued = 0;
            syncReport.FastBlocksReceipts.Reset(0, 65);
            syncReport.FastBlocksReceipts.CurrentQueued = 0;
            syncReport.SyncModeSelectorOnChanged(null, new SyncModeChangedEventArgs(SyncMode.None, SyncMode.FastHeaders | SyncMode.FastBodies | SyncMode.FastReceipts));
            timer.Elapsed += Raise.Event();

            iLogger.Received(1).Info("Old Headers           0 /        100 (  0.00 %) [                                     ] queue        0 | current       0 Blk/s");
            iLogger.Received(1).Info("Old Bodies            0 /         70 (  0.00 %) [                                     ] queue        0 | current       0 Blk/s");
            iLogger.Received(1).Info("Old Receipts          0 /         65 (  0.00 %) [                                     ] queue        0 | current       0 Blk/s");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Ancient_bodies_and_receipts_are_not_reported_until_feed_finishes_Initialization(bool setBarriers)
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            ISyncPeerPool pool = Substitute.For<ISyncPeerPool>();
            pool.InitializedPeersCount.Returns(1);
            ITimerFactory timerFactory = Substitute.For<ITimerFactory>();
            ITimer timer = Substitute.For<ITimer>();
            timerFactory.CreateTimer(Arg.Any<TimeSpan>()).Returns(timer);
            ILogManager logManager = Substitute.For<ILogManager>();
            InterfaceLogger iLogger = Substitute.For<InterfaceLogger>();
            iLogger.IsInfo.Returns(true);
            iLogger.IsError.Returns(true);
            ILogger logger = new(iLogger);
            logManager.GetClassLogger().Returns(logger);

            Queue<SyncMode> syncModes = new();
            syncModes.Enqueue(SyncMode.FastHeaders);
            syncModes.Enqueue(SyncMode.FastBodies);
            syncModes.Enqueue(SyncMode.FastReceipts);

            SyncConfig syncConfig = new()
            {
                FastSync = true,
                PivotNumber = "100",
            };
            if (setBarriers)
            {
                syncConfig.AncientBodiesBarrier = 30;
                syncConfig.AncientReceiptsBarrier = 35;
            }

            SyncReport syncReport = new(pool, Substitute.For<INodeStatsManager>(), syncConfig, Substitute.For<IPivot>(), logManager, timerFactory);
            syncReport.SyncModeSelectorOnChanged(null, new SyncModeChangedEventArgs(SyncMode.None, SyncMode.FastHeaders | SyncMode.FastBodies | SyncMode.FastReceipts));
            timer.Elapsed += Raise.Event();

            if (setBarriers)
            {
                iLogger.DidNotReceive().Info("Old Headers    0 / 100 (  0.00 %) | queue         0 | current            0 Blk/s | total            0 Blk/s");
                iLogger.DidNotReceive().Info("Old Bodies     0 / 70 (  0.00 %) | queue         0 | current            0 Blk/s | total            0 Blk/s");
                iLogger.DidNotReceive().Info("Old Receipts   0 / 65 (  0.00 %) | queue         0 | current            0 Blk/s | total            0 Blk/s");
            }
            else
            {
                iLogger.DidNotReceive().Info("Old Headers    0 / 100 (  0.00 %) | queue         0 | current            0 Blk/s | total            0 Blk/s");
                iLogger.DidNotReceive().Info("Old Bodies     0 / 100 (  0.00 %) | queue         0 | current            0 Blk/s | total            0 Blk/s");
                iLogger.DidNotReceive().Info("Old Receipts   0 / 100 (  0.00 %) | queue         0 | current            0 Blk/s | total            0 Blk/s");
            }
        }
    }
}
