// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Generic;
using System.Threading.Tasks;
using Ethereum.Test.Base;
using NUnit.Framework;

namespace Ethereum.Blockchain.Block.Legacy.Test
{
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public class ForgedTests : BlockchainTestBase
    {
        [TestCaseSource(nameof(LoadTests))]
        public async Task Test(BlockchainTest test)
        {
            await RunTest(test, null, false);
        }
        public static IEnumerable<BlockchainTest> LoadTests()
        {
            var loader = new TestsSourceLoader(new LoadLegacyBlockchainTestsStrategy(), "bcForgedTest");
            return loader.LoadTests<BlockchainTest>();
        }
    }
}
