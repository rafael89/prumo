type ExecutedAllocation = { assetId?: string | null; candidateKey?: string | null }

export function contributionExecution(guidanceId: string, allocation: ExecutedAllocation, quantity: number, unitPrice: number) {
  return {
    guidanceId,
    items: [{
      assetId: allocation.assetId ?? '00000000-0000-0000-0000-000000000000',
      candidateKey: allocation.candidateKey,
      quantity,
      unitPrice,
    }],
  }
}
