// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Threading;
using Nethermind.Core.Specs;

namespace Nethermind.Specs.Forks
{
    public class Dao : Homestead
    {
        private static IReleaseSpec _instance;

        protected Dao()
        {
            Name = "DAO";
        }

        public new static IReleaseSpec Instance => LazyInitializer.EnsureInitialized(ref _instance, static () => new Dao());
    }
}
