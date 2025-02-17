// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Generic;
using System.Linq;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Specs;
using Nethermind.Crypto;
using Nethermind.Evm.Tracing;
using Nethermind.Evm.TransactionProcessing;

namespace Nethermind.Consensus.Validators;

public class InclusionListValidator(
    ISpecProvider specProvider,
    ITransactionProcessor transactionProcessor) : IInclusionListValidator
{
    private readonly ISpecProvider _specProvider = specProvider;
    private readonly ITransactionProcessor _transactionProcessor = transactionProcessor;

    public bool ValidateInclusionList(Block block, out string? error) =>
        ValidateInclusionList(block, _specProvider.GetSpec(block.Header), out error);

    public bool ValidateInclusionList(Block block, IReleaseSpec spec, out string? error)
    {
        error = null;

        if (!spec.InclusionListsEnabled)
        {
            return true;
        }

        if (block.InclusionListTransactions is null)
        {
            error = "Block did not have inclusion list";
            return false;
        }

        if (block.GasUsed >= block.GasLimit)
        {
            return true;
        }

        var blockTxHashes = new HashSet<Hash256>(block.Transactions.Select(tx => tx.Hash));

        foreach (Transaction tx in block.InclusionListTransactions)
        {
            if (blockTxHashes.Contains(tx.Hash))
            {
                continue;
            }

            if (block.GasUsed + tx.GasLimit > block.GasLimit)
            {
                continue;
            }

            bool couldIncludeTx = _transactionProcessor.BuildUp(tx, new(block.Header, spec), NullTxTracer.Instance);
            if (couldIncludeTx)
            {
                error = "Block excludes valid inclusion list transaction";
                return false;
            }
        }

        return true;
    }
}
